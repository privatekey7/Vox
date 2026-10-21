"""
Push-to-talk worker: keyboard hook + microphone capture + transcription.

The engine is held behind a lock so loading/unloading it never races
the transcribe path.

The keyboard hook only starts/stops a recording; recognition, post-processing
and pasting run on a separate "VoxProcessor" thread so a slow (long)
dictation never blocks the next key press.

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
    AUDIO_BLOCK_SIZE,
    AUDIO_TAIL_SEC,
    AUTO_PASTE,
    CLIPBOARD_ATTEMPTS,
    CLIPBOARD_RESTORE_DELAY_SEC,
    CURSOR_CONTEXT_ENABLED,
    ENABLE_BEEPS,
    ERROR_BEEP_MIN_MS,
    MIN_AUDIO_PEAK,
    MIN_RECORD_MS,
    RESTORE_CLIPBOARD,
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

# Chunks of the recording in progress. The key-press creates a fresh list, the
# audio callback appends to whichever list is current, and the processor
# thread detaches it AUDIO_TAIL_SEC after the key-release so the last words
# are not cut off.
_active_chunks: list[np.ndarray] | None = None
_recording = False
_chord_used = False  # another key was pressed while the hotkey was held
_record_start_time = 0.0
_suppress_until = 0.0  # timestamp until which keyboard events are ignored

# Finished recordings waiting for recognition: (chunks, released_at, elapsed_ms);
# None stops the processor thread.
_jobs: queue.Queue = queue.Queue()

_stream: sd.InputStream | None = None
_stream_lock = threading.Lock()

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


def _beep_error() -> None:
    """Low double beep: the dictation produced no text."""
    if ENABLE_BEEPS:
        winsound.Beep(300, 120)  # type: ignore[attr-defined]
        winsound.Beep(250, 160)  # type: ignore[attr-defined]


def _audio_callback(indata, frames, time_info, status):
    if status:
        print(f"[AUDIO] {status}", file=sys.stderr)
    chunks = _active_chunks
    if chunks is not None:
        chunks.append(indata.copy())


def _open_microphone() -> None:
    """Open the default input device and start streaming into _audio_callback."""
    global _stream
    stream = sd.InputStream(
        samplerate=SAMPLE_RATE,
        blocksize=AUDIO_BLOCK_SIZE,
        dtype="int16",
        channels=1,
        callback=_audio_callback,
    )
    stream.start()
    _stream = stream
    print(f"[AUDIO] Microphone open: {sd.query_devices(kind='input')['name']}")


def _reopen_microphone() -> None:
    """Restart audio capture, picking up a new default device if it changed."""
    global _stream
    with _stream_lock:
        old, _stream = _stream, None
        if old is not None:
            with contextlib.suppress(Exception):
                old.abort()
                old.close()
        # PortAudio caches the device list; re-initialising refreshes it
        # (e.g. after a headset was plugged in or the default mic changed).
        with contextlib.suppress(Exception):
            sd._terminate()
            sd._initialize()
        try:
            _open_microphone()
        except Exception as e:
            print(f"[AUDIO] Failed to reopen microphone: {e}")


# ---------------------------------------------------------------------------
# Transcription
# ---------------------------------------------------------------------------


def _transcribe(chunks: list[np.ndarray]) -> str:
    """Recognise one recording; logs *why* when no text comes out."""
    if not chunks:
        # The stream delivered nothing at all: the device stalled or vanished.
        print("[STT] No audio from microphone — reopening it")
        _reopen_microphone()
        return ""
    audio = np.concatenate(chunks).flatten()
    peak = int(np.max(np.abs(audio)))
    if peak < MIN_AUDIO_PEAK:
        print(f"[STT] Too quiet (peak {peak} < {MIN_AUDIO_PEAK}) — check the microphone")
        return ""
    with _engine_lock:
        engine = _engine
    if engine is None:
        print("[STT] No engine loaded")
        return ""
    started = time.time()
    text = engine.transcribe(audio)
    print(
        f"[STT] Recognised {len(audio) / SAMPLE_RATE:.1f}s audio (peak {peak}) "
        f"in {time.time() - started:.1f}s"
    )
    if not text:
        print("[STT] Model returned no text")
    return text


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
    global _chord_used
    if time.time() < _suppress_until:
        return
    if event.name != HOTKEY:
        # Another key while the hotkey is held: it is a shortcut (Ctrl+C…),
        # not a dictation.
        if _recording and event.event_type == keyboard.KEY_DOWN:
            _chord_used = True
        return
    if event.event_type == keyboard.KEY_DOWN:
        _on_key_press(event)
    elif event.event_type == keyboard.KEY_UP:
        _on_key_release(event)


def _on_key_press(event):
    global _recording, _record_start_time, _active_chunks, _chord_used
    if _recording:
        return
    _chord_used = False
    _active_chunks = []
    _record_start_time = time.time()
    _recording = True
    _beep_start()
    print(f"[REC] Listening... ({time.strftime('%H:%M:%S')})")


def _on_key_release(event):
    """Hand the recording to the processor thread; never blocks the hook."""
    global _recording, _active_chunks
    if not _recording:
        return
    _recording = False
    elapsed_ms = (time.time() - _record_start_time) * 1000
    _beep_stop()
    if _chord_used:
        _active_chunks = None
        print("[REC] Hotkey was part of a shortcut — not a dictation, ignored")
        return
    _jobs.put((_active_chunks, time.time(), elapsed_ms))


def _processor_loop() -> None:
    """Recognise and paste finished recordings, one at a time, in order."""
    while True:
        job = _jobs.get()
        if job is None:  # stop signal
            return
        chunks, released_at, elapsed_ms = job
        try:
            _finish_recording(chunks, released_at, elapsed_ms)
        except Exception as e:
            # Never let one bad dictation kill the thread (and all later ones).
            print(f"[ERROR] Processing dictation failed: {e}")
            import traceback

            traceback.print_exc()
            _beep_error()


def _finish_recording(chunks: list[np.ndarray] | None, released_at: float, elapsed_ms: float):
    global _active_chunks, _last_output_text, _last_output_ts
    # Keep capturing briefly after the release: the last audio block is
    # still in flight and holds the end of the final word.
    time.sleep(max(0.0, released_at + AUDIO_TAIL_SEC - time.time()))
    if _active_chunks is chunks:
        _active_chunks = None
    chunks = list(chunks or [])

    if elapsed_ms < MIN_RECORD_MS:
        print(f"[REC] Too short ({elapsed_ms:.0f}ms), ignored")
        return

    print(f"[REC] Stopped ({elapsed_ms:.0f}ms), transcribing...")
    raw_text = _transcribe(chunks)
    if not raw_text:
        print("[STT] No speech recognized")
        if elapsed_ms >= ERROR_BEEP_MIN_MS:  # shorter presses are usually accidental
            _beep_error()
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

    _deliver(text)

    # Remember what we just emitted so the next dictation can use it as cursor
    # context when the screen can't be read (terminals, address bars).
    if text:
        _last_output_text = text
        _last_output_ts = time.monotonic()


# ---------------------------------------------------------------------------
# Clipboard + paste
# ---------------------------------------------------------------------------


def _clipboard_call(fn, *args):
    """Run a pyperclip call, retrying while another app holds the clipboard."""
    for attempt in range(CLIPBOARD_ATTEMPTS):
        try:
            return fn(*args)
        except pyperclip.PyperclipException as e:
            if attempt == CLIPBOARD_ATTEMPTS - 1:
                raise
            print(f"[CLIP] Clipboard busy ({e}), retrying")
            time.sleep(0.05)
    return None


def _deliver(text: str) -> None:
    """Paste ``text`` at the cursor, then give the user their clipboard back."""
    global _suppress_until
    # Split off any contextual leading separator. Some targets — most notably
    # the Chrome/Edge address bar (omnibox) — strip leading whitespace from
    # pasted clipboard content (base::CollapseWhitespace), which glues
    # consecutive dictations together ("Привет." + " Как дела" →
    # "Привет.Как дела"). When auto-pasting we type that separator as a real
    # keystroke (which the omnibox does NOT trim) and paste only the body.
    # Without auto-paste the user pastes manually, so the full text — separator
    # included — stays on the clipboard.
    separator, body = split_leading_separator(text) if AUTO_PASTE else ("", text)

    previous: str | None = None
    if AUTO_PASTE and RESTORE_CLIPBOARD:
        try:
            previous = _clipboard_call(pyperclip.paste)
        except pyperclip.PyperclipException as e:
            print(f"[CLIP] Could not read clipboard, it will not be restored: {e}")

    # Mark our clipboard write so CorrectionMonitor skips it
    if _monitor is not None:
        _monitor.mark_own_clipboard(body)

    _clipboard_call(pyperclip.copy, body)
    print("[CLIP] Copied to clipboard")

    if not AUTO_PASTE:
        return

    # Don't let the synthetic ctrl+v re-trigger PTT when HOTKEY is ctrl
    _suppress_until = time.time() + 0.2
    time.sleep(0.05)
    if separator:
        # Typed, not pasted, so the address-bar whitespace-trim can't eat
        # it. In normal fields a typed space is identical to a pasted one.
        keyboard.send("space")
    keyboard.send("ctrl+v")
    print("[PASTE] Pasted")

    if previous is None or previous == body:
        return
    # The target app reads the clipboard asynchronously after Ctrl+V; wait
    # before putting the user's own content back.
    # debt: fixed delay; a very busy app could still read after the restore.
    # Upgrade path: delayed clipboard rendering (WM_RENDERFORMAT) to learn
    # exactly when the paste happened.
    time.sleep(CLIPBOARD_RESTORE_DELAY_SEC)
    try:
        if _clipboard_call(pyperclip.paste) != body:
            return  # the user copied something new meanwhile — keep it
        if _monitor is not None:
            _monitor.mark_own_clipboard(previous)
        _clipboard_call(pyperclip.copy, previous)
        print("[CLIP] Restored previous clipboard")
    except pyperclip.PyperclipException as e:
        print(f"[CLIP] Could not restore clipboard: {e}")


def get_last_text() -> str:
    """The most recent dictation (for the tray's "copy last dictation")."""
    return _last_output_text.lstrip()


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

    threading.Thread(target=_processor_loop, daemon=True, name="VoxProcessor").start()
    keyboard.hook(_on_event)
    print(f"[INIT] Opening microphone ({SAMPLE_RATE} Hz)")
    try:
        _open_microphone()
    except Exception as e:
        # Keep running: the next dictation retries via _reopen_microphone().
        print(f"[AUDIO] Could not open microphone: {e}")

    try:
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
