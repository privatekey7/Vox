"""
Dictionary auto-learning via user corrections (Decision D003, Variant A).

Flow
----
1. Vox transcribes speech and pastes text.  The worker calls
   ``tracker.record_transcription(text)`` to remember the words.
2. The user double-clicks a wrong word (selecting it) and retypes the
   correct version.
3. ``CorrectionTracker.detect_correction(selected, replacement)`` checks
   if the selected word belongs to a recent transcription and returns a
   ``Correction`` object.
4. A toast asks the user to confirm; on confirm the correction is saved
   to ``VoxDB.add_dict_entry()``.
5. Future transcriptions apply saved corrections automatically via
   ``apply_dictionary()``.

The ``CorrectionTracker`` is pure logic and fully testable without Win32.
The ``CorrectionMonitor`` provides the Win32 glue: clipboard monitoring
and toast notifications.
"""

from __future__ import annotations

import contextlib
import threading
import time
from collections.abc import Callable
from dataclasses import dataclass, field

# Maximum seconds after transcription during which a correction is accepted.
_CORRECTION_WINDOW_SEC = 120.0

# Maximum number of recent transcriptions to keep.
_MAX_HISTORY = 20


@dataclass
class Correction:
    """A detected correction pair."""

    wrong: str
    correct: str
    timestamp: float = field(default_factory=time.time)


@dataclass
class _TranscriptionRecord:
    """Internal record of a recent transcription."""

    text: str
    words: set[str]
    timestamp: float


class CorrectionTracker:
    """
    Pure-logic tracker that detects user corrections against recent
    transcriptions.

    Thread-safe for single-writer use (the Vox worker thread).
    """

    def __init__(
        self,
        *,
        window_sec: float = _CORRECTION_WINDOW_SEC,
        max_history: int = _MAX_HISTORY,
    ) -> None:
        self._window_sec = window_sec
        self._max_history = max_history
        self._history: list[_TranscriptionRecord] = []

    @property
    def history_size(self) -> int:
        """Number of transcription records currently tracked."""
        return len(self._history)

    def record_transcription(self, text: str) -> None:
        """
        Store a transcription so its words can be matched later.

        Words are normalized to lowercase for comparison.
        """
        if not text or not text.strip():
            return

        words = {w.strip(".,!?;:—–-\"'«»()[]{}").lower() for w in text.split()}
        words.discard("")
        if not words:
            return

        self._history.append(_TranscriptionRecord(text=text, words=words, timestamp=time.time()))

        # Trim old entries
        if len(self._history) > self._max_history:
            self._history = self._history[-self._max_history :]

    def detect_correction(self, selected: str, replacement: str) -> Correction | None:
        """
        Check if *selected* (the double-clicked word) belongs to a recent
        transcription and *replacement* is a different word.

        Returns a ``Correction`` if a match is found, otherwise ``None``.
        """
        selected_clean = selected.strip()
        replacement_clean = replacement.strip()

        if not selected_clean or not replacement_clean:
            return None

        # Same word → not a correction
        if selected_clean.lower() == replacement_clean.lower():
            return None

        now = time.time()
        sel_lower = selected_clean.lower()

        # Search from most recent to oldest
        for record in reversed(self._history):
            if now - record.timestamp > self._window_sec:
                break  # Everything older is also expired
            if sel_lower in record.words:
                return Correction(wrong=selected_clean.lower(), correct=replacement_clean)

        return None

    def has_recent_word(self, word: str) -> bool:
        """Check if a word appears in any non-expired transcription."""
        now = time.time()
        word_lower = word.lower()
        for record in reversed(self._history):
            if now - record.timestamp > self._window_sec:
                break
            if word_lower in record.words:
                return True
        return False

    def clear(self) -> None:
        """Remove all tracked transcriptions."""
        self._history.clear()

    def prune_expired(self) -> int:
        """
        Remove transcriptions older than the correction window.

        Returns the number of records removed.
        """
        cutoff = time.time() - self._window_sec
        before = len(self._history)
        self._history = [r for r in self._history if r.timestamp > cutoff]
        return before - len(self._history)


def apply_dictionary(
    text: str, corrections: dict[str, str], *, case_sensitive: bool = False
) -> str:
    """
    Apply dictionary corrections to a transcription.

    Args:
        text: Raw transcription text.
        corrections: Mapping of ``{wrong: correct}`` pairs.
        case_sensitive: If False (default), match case-insensitively
                        and preserve the replacement's casing.

    Returns:
        Text with corrections applied.
    """
    if not corrections or not text:
        return text

    words = text.split(" ")
    result: list[str] = []

    for word in words:
        # Strip punctuation for lookup, preserve it for output
        stripped = word.strip(".,!?;:—–-\"'«»()[]{}…")
        prefix = word[: word.index(stripped)] if stripped and stripped in word else ""
        suffix = word[len(prefix) + len(stripped) :] if stripped else ""

        lookup_key = stripped if case_sensitive else stripped.lower()

        if lookup_key in corrections:
            replacement = corrections[lookup_key]
            # Preserve original capitalization pattern
            if stripped and stripped[0].isupper() and replacement[0].islower():
                replacement = replacement[0].upper() + replacement[1:]
            result.append(f"{prefix}{replacement}{suffix}")
        else:
            result.append(word)

    return " ".join(result)


# ═══════════════════════════════════════════════════════════════════════════
# CorrectionMonitor — clipboard monitoring + toast UI (Win32)
# ═══════════════════════════════════════════════════════════════════════════


class CorrectionMonitor:
    """
    Clipboard-based correction monitor.

    Watches clipboard for copied words that match recent transcriptions.
    When two sequential single-word clipboard changes occur within the
    timeout window and the first word belongs to a recent transcription,
    a toast asks the user to confirm the correction.

    Typical user flow:
        1. Vox transcribes and pastes text.
        2. User selects (or double-clicks) the wrong word → Ctrl+C.
        3. User types the correct version → selects it → Ctrl+C.
        4. Toast: «Запомнить: 'wrong' → 'correct'?»
        5. User clicks «Да» → saved to dictionary.
    """

    POLL_INTERVAL = 0.5  # seconds between clipboard polls
    PENDING_TIMEOUT = 60.0  # seconds to wait for the second copy

    def __init__(
        self,
        tracker: CorrectionTracker,
        on_correction: Callable[[Correction], None],
    ) -> None:
        self._tracker = tracker
        self._on_correction = on_correction
        self._pending_wrong: str | None = None
        self._pending_time: float = 0.0
        self._own_clipboard: str | None = None
        self._running = False
        self._thread: threading.Thread | None = None

    def mark_own_clipboard(self, text: str) -> None:
        """Mark text as Vox's own clipboard write (to skip detection)."""
        self._own_clipboard = text

    def start(self) -> None:
        """Start clipboard monitoring in a daemon thread."""
        if self._running:
            return
        self._running = True
        self._thread = threading.Thread(
            target=self._poll_loop, daemon=True, name="VoxCorrectionMonitor"
        )
        self._thread.start()
        print("[MONITOR] Correction monitor started")

    def stop(self) -> None:
        """Stop monitoring."""
        self._running = False

    def _poll_loop(self) -> None:
        """Main polling loop — reads clipboard periodically."""
        import pyperclip

        last_clipboard = ""
        while self._running:
            time.sleep(self.POLL_INTERVAL)
            try:
                current = pyperclip.paste()
            except Exception:
                continue

            if current == last_clipboard:
                continue
            last_clipboard = current

            # Skip our own clipboard writes
            if current == self._own_clipboard:
                self._own_clipboard = None
                continue

            self._process_clipboard_change(current)

    def _process_clipboard_change(self, text: str) -> None:
        """Evaluate a clipboard change for correction detection."""
        stripped = text.strip()
        words = stripped.split()

        # Only consider single-word clipboard content
        if len(words) != 1:
            self._pending_wrong = None
            return

        word = words[0].strip(".,!?;:—–-\"'«»()[]{}…")
        if not word:
            return

        # If we have a pending wrong word, check for correction
        if self._pending_wrong is not None:
            if time.time() - self._pending_time <= self.PENDING_TIMEOUT:
                correction = self._tracker.detect_correction(self._pending_wrong, word)
                if correction is not None:
                    print(
                        f'[MONITOR] Correction detected: "{correction.wrong}" → "{correction.correct}"'
                    )
                    self._show_toast(correction)
            self._pending_wrong = None

        # Check if this word belongs to a recent transcription
        if self._tracker.has_recent_word(word):
            self._pending_wrong = word
            self._pending_time = time.time()
            print(f'[MONITOR] Pending correction for: "{word}"')

    def _show_toast(self, correction: Correction) -> None:
        """Show a toast notification for correction confirmation."""

        def _run() -> None:
            toast = _CorrectionToast(
                wrong=correction.wrong,
                correct=correction.correct,
                on_confirm=lambda: self._on_correction(correction),
                on_dismiss=lambda: None,
            )
            toast.run()

        threading.Thread(target=_run, daemon=True, name="VoxToast").start()


class _CorrectionToast:
    """
    Small toast notification at the bottom-right corner of the screen.

    Shows a correction suggestion and auto-closes after a timeout.
    Uses tkinter — must be instantiated in its own thread.
    """

    DURATION_MS = 8000

    def __init__(
        self,
        wrong: str,
        correct: str,
        on_confirm: Callable[[], None],
        on_dismiss: Callable[[], None],
    ) -> None:
        import tkinter as tk

        self._on_confirm = on_confirm
        self._on_dismiss = on_dismiss
        self._tk = tk

        self.root = tk.Tk()
        self.root.withdraw()  # hide briefly while we position
        self.root.overrideredirect(True)
        self.root.attributes("-topmost", True)

        # Dark-themed frame
        frame = tk.Frame(self.root, bg="#2d2d2d", padx=16, pady=12)
        frame.pack(fill="both", expand=True)

        tk.Label(
            frame,
            text="Vox — Запомнить исправление?",
            fg="#aaa",
            bg="#2d2d2d",
            font=("Segoe UI", 8),
        ).pack(anchor="w")

        tk.Label(
            frame,
            text=f"«{wrong}» → «{correct}»",
            fg="white",
            bg="#2d2d2d",
            font=("Segoe UI", 11),
            wraplength=320,
        ).pack(anchor="w", pady=(4, 8))

        btn_row = tk.Frame(frame, bg="#2d2d2d")
        btn_row.pack(fill="x")

        tk.Button(
            btn_row, text="Да", width=8, command=self._confirm, bg="#4CAF50", fg="white"
        ).pack(side="left")
        tk.Button(btn_row, text="Нет", width=8, command=self._dismiss, bg="#555", fg="white").pack(
            side="left", padx=(8, 0)
        )

        # Position at bottom-right
        w, h = 380, 110
        screen_w = self.root.winfo_screenwidth()
        screen_h = self.root.winfo_screenheight()
        x = screen_w - w - 20
        y = screen_h - h - 60
        self.root.geometry(f"{w}x{h}+{x}+{y}")
        self.root.deiconify()

        # Auto-close
        self.root.after(self.DURATION_MS, self._dismiss)

    def _confirm(self) -> None:
        with contextlib.suppress(Exception):
            self._on_confirm()
        with contextlib.suppress(self._tk.TclError):
            self.root.destroy()

    def _dismiss(self) -> None:
        with contextlib.suppress(Exception):
            self._on_dismiss()
        with contextlib.suppress(self._tk.TclError):
            self.root.destroy()

    def run(self) -> None:
        """Run the toast event loop (blocks until closed)."""
        self.root.mainloop()
