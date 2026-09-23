"""
Push-to-talk worker: keyboard hook + microphone capture + transcription.

The engine is held behind a lock so loading/unloading it never races
the transcribe path.

Phase 2 integration:
- Full PostProcessor pipeline (terms → fillers → ITN → punctuation → capitalization)
- SQLite storage (every transcription saved to vox_data.db)
- CorrectionTracker (auto-learning user corrections)
- CorrectionMonitor (clipboard-based correction detection + toast UI)
"""

from __future__ import annotations

import contextlib
import gc
import queue
import sys
import threading
import time
import winsound

import keyboard
import numpy as np
import pyperclip
import sounddevice as sd

from voxapp.config import (
    AUTO_PASTE,
    CURSOR_CONTEXT_ENABLED,
    ENABLE_BEEPS,
    MIN_RECORD_MS,
    SAMPLE_RATE,
    get_db_path,
    get_hotkey_display,
    initial_hotkey,
)
from voxapp.correction_monitor import CorrectionMonitor, CorrectionTracker, apply_dictionary
from voxapp.cursor_context import read_preceding_text, resolve_preceding_context
from voxapp.engines import Engine, build_engine
from voxapp.post_processor import PostProcessor
from voxapp.registry import MODELS
from voxapp.spacing import split_leading_separator
from voxapp.storage import VoxDB

# ---------------------------------------------------------------------------
# Module-level state (mutated by tray callbacks too)
# ---------------------------------------------------------------------------

_engine: Engine | None = None
_engine_lock = threading.Lock()
_active_model_id: str | None = None
_post_processor: PostProcessor | None = None

_audio_queue: queue.Queue = queue.Queue()
_recording = False
_record_start_time = 0.0
_suppress_until = 0.0  # timestamp until which keyboard events are ignored

# Fallback cursor context: remember our own last pasted output so that the
# join-space + capitalization still work in windows where reading the screen
# is unsafe/unreliable (terminals, browser address bars) and
# read_preceding_text() returns None. Using our own clean text avoids the
# ";2H" terminal-echo garbage entirely. See cursor_context.resolve_preceding_context.
_last_output_text: str = ""
_last_output_ts: float = 0.0

HOTKEY = initial_hotkey()

# Storage + corrections (initialised in run())
_db: VoxDB | None = None
_tracker: CorrectionTracker | None = None
_monitor: CorrectionMonitor | None = None
_user_corrections: dict[str, str] = {}


# ---------------------------------------------------------------------------
# Engine management (called from tray / GUI)
# ---------------------------------------------------------------------------


def set_engine(model_id: str) -> None:
    """Atomically swap the active engine. Blocks until the new one is loaded."""
    global _engine, _active_model_id, _post_processor
    new_engine = build_engine(model_id)
    info = MODELS.get(model_id, {})
    new_pp = PostProcessor.from_model_config(info)
    with _engine_lock:
        old = _engine
        _engine = new_engine
        _active_model_id = model_id
        _post_processor = new_pp
    if old is not None:
        with contextlib.suppress(Exception):
            old.unload()
        del old
        gc.collect()
    print(f"[PP] PostProcessor configured for {model_id}")


def get_active_model_id() -> str | None:
    return _active_model_id


def get_db() -> VoxDB | None:
    """Return the shared VoxDB instance (used by history UI)."""
    return _db


def get_tracker() -> CorrectionTracker | None:
    """Return the shared CorrectionTracker instance."""
    return _tracker


def set_hotkey(event_name: str) -> None:
    """Change the push-to-talk key at runtime."""
    global HOTKEY
    HOTKEY = event_name


# ---------------------------------------------------------------------------
# Audio
# ---------------------------------------------------------------------------


def _beep_start() -> None:
    if ENABLE_BEEPS:
        winsound.Beep(800, 80)  # type: ignore[attr-defined]


def _beep_stop() -> None:
    if ENABLE_BEEPS:
        winsound.Beep(500, 80)  # type: ignore[attr-defined]


def _audio_callback(indata, frames, time_info, status):
    if status:
        print(f"[AUDIO] {status}", file=sys.stderr)
    if _recording:
        _audio_queue.put(indata.copy())


# ---------------------------------------------------------------------------
# Transcription
# ---------------------------------------------------------------------------


def _drain_audio() -> np.ndarray | None:
    chunks = []
    while not _audio_queue.empty():
        try:
            chunks.append(_audio_queue.get_nowait())
        except queue.Empty:
            break
    if not chunks:
        return None
    audio = np.concatenate(chunks).flatten()
    if np.max(np.abs(audio)) < 100:
        return None
    return audio


def _transcribe() -> str:
    audio = _drain_audio()
    if audio is None:
        return ""
    with _engine_lock:
        engine = _engine
    if engine is None:
        print("[STT] No engine loaded")
        return ""
    return engine.transcribe(audio)


# ---------------------------------------------------------------------------
# Post-processing + storage
# ---------------------------------------------------------------------------


def _post_process_and_store(
    raw_text: str, duration_ms: int, *, preceding_text: str | None = None
) -> str:
    """
    Apply the full pipeline to raw STT output, save to DB, and return
    the final text ready for pasting.
    """
    if not raw_text:
        return ""

    # 1. Apply user dictionary corrections (auto-learned)
    text = raw_text
    if _user_corrections:
        text = apply_dictionary(text, _user_corrections)
        if text != raw_text:
            print(f'[DICT] Applied corrections: "{raw_text}" → "{text}"')

    # 2. Run post-processing pipeline
    pp = _post_processor
    if pp is not None:
        result = pp.process(text, preceding_text=preceding_text)
        processed = result.processed
        # Log pipeline steps if anything changed
        for step_name, step_text in result.steps.items():
            if step_text != text:
                print(f"[PP] {step_name}: {step_text}")
    else:
        processed = text

    # 3. Save to SQLite
    model_id = _active_model_id or ""
    model_info = MODELS.get(model_id, {})
    lang = model_info.get("language", "")

    if _db is not None:
        try:
            row_id = _db.add_transcription(
                raw_text=raw_text,
                processed_text=processed,
                model=model_id,
                lang=lang,
                duration_ms=duration_ms,
            )
            print(f"[DB] Saved transcription #{row_id}")
        except Exception as e:
            print(f"[DB] Failed to save: {e}")

    # 4. Record in correction tracker for auto-learning
    if _tracker is not None:
        _tracker.record_transcription(processed)

    return processed


# ---------------------------------------------------------------------------
# User corrections management
# ---------------------------------------------------------------------------


def _load_corrections_from_db() -> None:
    """Load all dictionary entries from the DB into memory."""
    global _user_corrections
    if _db is None:
        return
    try:
        entries = _db.get_dict_entries()
        _user_corrections = {e.wrong: e.correct for e in entries}
        if _user_corrections:
            print(f"[DICT] Loaded {len(_user_corrections)} correction(s) from DB")
    except Exception as e:
        print(f"[DICT] Failed to load corrections: {e}")


def add_correction(wrong: str, correct: str) -> None:
    """
    Add a correction to the dictionary (called from correction monitor UI).

    Saves to DB and updates the in-memory dict.
    """
    global _user_corrections
    model_info = MODELS.get(_active_model_id or "", {})
    lang = model_info.get("language", "")

    if _db is not None:
        try:
            _db.add_dict_entry(wrong, correct, lang=lang, source="auto")
        except Exception as e:
            print(f"[DICT] Failed to save correction: {e}")

    _user_corrections[wrong] = correct
    print(f'[DICT] Added correction: "{wrong}" → "{correct}"')


def _on_correction_confirmed(correction) -> None:
    """Callback for CorrectionMonitor when user confirms a correction."""
    add_correction(correction.wrong, correction.correct)


# ---------------------------------------------------------------------------
# Keyboard hook
# ---------------------------------------------------------------------------


def _on_event(event):
    """Dispatch only when event.name matches the configured HOTKEY."""
    if time.time() < _suppress_until:
        return
    if event.name != HOTKEY:
        return
    if event.event_type == keyboard.KEY_DOWN:
        _on_key_press(event)
    elif event.event_type == keyboard.KEY_UP:
        _on_key_release(event)


def _on_key_press(event):
    global _recording, _record_start_time
    if _recording:
        return
    # Drop stale audio
    while not _audio_queue.empty():
        try:
            _audio_queue.get_nowait()
        except queue.Empty:
            break
    _record_start_time = time.time()
    _recording = True
    _beep_start()
    print("[REC] Listening...")


def _on_key_release(event):
    global _recording, _suppress_until, _last_output_text, _last_output_ts
    if not _recording:
        return
    _recording = False
    elapsed_ms = (time.time() - _record_start_time) * 1000
    _beep_stop()

    if elapsed_ms < MIN_RECORD_MS:
        print(f"[REC] Too short ({elapsed_ms:.0f}ms), ignored")
        return

    print(f"[REC] Stopped ({elapsed_ms:.0f}ms), transcribing...")
    raw_text = _transcribe()
    if not raw_text:
        print("[STT] No speech recognized")
        return

    print(f'[STT] Raw: "{raw_text}"')

    # Read text before cursor for contextual capitalization.
    # Only meaningful when auto-paste is on (we know where text will go).
    # None = "no cursor context" (skip capitalization step).
    # "" = "start of document" (capitalize).
    # Gated behind CURSOR_CONTEXT_ENABLED: the clipboard-selection trick
    # disturbs the caret in browsers/editors ("reversed text" bug). Off by
    # default. See D020.
    preceding: str | None = None
    if AUTO_PASTE and CURSOR_CONTEXT_ENABLED:
        screen_context = read_preceding_text()
        preceding = resolve_preceding_context(
            screen_context,
            last_output_text=_last_output_text,
            last_output_ts=_last_output_ts,
            now=time.monotonic(),
        )
        if preceding is not None and preceding:
            source = "screen" if screen_context is not None else "last output"
            print(f'[CTX] Preceding text ({source}): "…{preceding[-40:]}"')

    # Full pipeline: dictionary → post-processing → storage
    text = _post_process_and_store(raw_text, duration_ms=int(elapsed_ms), preceding_text=preceding)

    print(f'[STT] Final: "{text}"')

    # Split off any contextual leading separator. Some targets — most notably
    # the Chrome/Edge address bar (omnibox) — strip leading whitespace from
    # pasted clipboard content (base::CollapseWhitespace), which glues
    # consecutive dictations together ("Привет." + " Как дела" →
    # "Привет.Как дела"). When auto-pasting we type that separator as a real
    # keystroke (which the omnibox does NOT trim) and paste only the body.
    # Without auto-paste the user pastes manually, so the full text — separator
    # included — stays on the clipboard.
    separator, body = split_leading_separator(text) if AUTO_PASTE else ("", text)

    # Mark our clipboard write so CorrectionMonitor skips it
    if _monitor is not None:
        _monitor.mark_own_clipboard(body)

    pyperclip.copy(body)
    print("[CLIP] Copied to clipboard")

    if AUTO_PASTE:
        # Don't let the synthetic ctrl+v re-trigger PTT when HOTKEY is ctrl
        _suppress_until = time.time() + 0.2
        time.sleep(0.05)
        if separator:
            # Typed, not pasted, so the address-bar whitespace-trim can't eat
            # it. In normal fields a typed space is identical to a pasted one.
            keyboard.send("space")
        keyboard.send("ctrl+v")
        print("[PASTE] Pasted")

    # Remember what we just emitted so the next dictation can use it as cursor
    # context when the screen can't be read (terminals, address bars).
    if text:
        _last_output_text = text
        _last_output_ts = time.monotonic()


# ---------------------------------------------------------------------------
# Worker entry point (runs in background thread)
# ---------------------------------------------------------------------------


def run(initial_model_id: str) -> None:
    """Load the engine, hook keyboard, open the mic, block forever."""
    global _db, _tracker, _monitor

    print(f"[INIT] Active model: {initial_model_id}")

    # Initialise SQLite storage
    try:
        db_path = get_db_path()
        _db = VoxDB(db_path, check_same_thread=False)
        print(f"[INIT] Database opened: {db_path}")
    except Exception as e:
        print(f"[INIT] Warning: could not open database: {e}")
        _db = None

    # Initialise correction tracker + monitor
    _tracker = CorrectionTracker()
    _monitor = CorrectionMonitor(
        tracker=_tracker,
        on_correction=_on_correction_confirmed,
    )
    _monitor.start()

    # Load saved corrections from DB
    _load_corrections_from_db()

    # Load the STT engine + PostProcessor
    try:
        set_engine(initial_model_id)
    except Exception as e:
        print(f"[FATAL] Failed to load engine for {initial_model_id}: {e}")
        import traceback

        traceback.print_exc()
        return

    keyboard.hook(_on_event)
    print(f"[INIT] Opening microphone ({SAMPLE_RATE} Hz)")

    try:
        with sd.InputStream(
            samplerate=SAMPLE_RATE,
            blocksize=4000,
            dtype="int16",
            channels=1,
            callback=_audio_callback,
        ):
            print("=" * 50)
            print(" Vox — Voice Input")
            print("=" * 50)
            print(f" Model:      {initial_model_id}")
            print(f" Hotkey:     [{get_hotkey_display(HOTKEY).upper()}] (hold to record)")
            print(f" Auto-paste: {AUTO_PASTE}")
            print(f" Beeps:      {ENABLE_BEEPS}")
            print(" Pipeline:   PostProcessor (full)")
            if _db is not None:
                print(f" Database:   {_db.path}")
            if _user_corrections:
                print(f" Dictionary: {len(_user_corrections)} correction(s)")
            print(" Monitor:    CorrectionMonitor (clipboard)")
            print("=" * 50)
            print("Ready. Hold the hotkey and speak.")
            keyboard.wait()  # blocks forever
    except Exception as e:
        print(f"[ERROR] {e}")
        import traceback

        traceback.print_exc()
    finally:
        if _monitor is not None:
            _monitor.stop()
        if _db is not None:
            with contextlib.suppress(Exception):
                _db.close()
