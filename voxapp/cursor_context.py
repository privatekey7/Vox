"""
Read text preceding the cursor in the active input field.

Uses the clipboard approach:
    1. Save clipboard
    2. Put a sentinel on clipboard
    3. Shift+Home — select from cursor to start of line
    4. Ctrl+C    — copy selection to clipboard
    5. Read clipboard (preceding text or sentinel if nothing was selected)
    6. Right     — deselect and restore cursor position
    7. Restore original clipboard

This is the most universal method across Windows applications.
It adds ~100–150 ms of latency but works in virtually every text
control (Notepad, VS Code, browser <textarea>, etc.).

Fallback: returns "" on any error, which makes the capitalization
module treat the fragment as the start of a sentence (capitalize).
"""

from __future__ import annotations

import contextlib
import re
import sys
import time

import keyboard
import pyperclip

# Sentinel that is unlikely to appear in real clipboard content.
_SENTINEL = "\x00__VOX_CTX__"

# Delays (seconds) — enough for the OS to process synthetic keypresses.
_DELAY_SELECT = 0.05
_DELAY_COPY = 0.06
_DELAY_DESELECT = 0.02

# Window classes of terminal/console emulators. In these, Shift+Home / Ctrl+C
# do not select-and-copy like a GUI text field — instead the terminal echoes
# raw ANSI sequences (e.g. ";2H") and Ctrl+C may send SIGINT. We must NOT run
# the clipboard-selection trick there.
_TERMINAL_WINDOW_CLASSES = frozenset(
    {
        "ConsoleWindowClass",  # cmd.exe / PowerShell / conhost
        "CASCADIA_HOSTING_WINDOW_CLASS",  # Windows Terminal
        "VirtualConsoleClass",  # ConEmu / Cmder
        "mintty",  # Git Bash / MSYS2
        "PuTTY",  # PuTTY
    }
)

# ANSI/VT escape sequences (CSI, OSC, single ESC-prefixed) emitted by terminals.
_ANSI_ESCAPE = re.compile(r"\x1b[@-_][0-?]*[ -/]*[@-~]|\x1b[@-Z\\-_]")
# Control characters except common whitespace (tab, newline, carriage return).
_CONTROL_CHARS = re.compile(r"[\x00-\x08\x0b\x0c\x0e-\x1f\x7f]")


def _foreground_window_class() -> str | None:
    """Return the Win32 class name of the foreground window, or None.

    Returns None on non-Windows platforms or if the lookup fails — callers
    treat that as "unknown, proceed normally".
    """
    if sys.platform != "win32":
        return None
    try:
        import ctypes

        user32 = ctypes.windll.user32  # type: ignore[attr-defined]
        hwnd = user32.GetForegroundWindow()
        if not hwnd:
            return None
        buffer = ctypes.create_unicode_buffer(256)
        user32.GetClassNameW(hwnd, buffer, 256)
        return buffer.value or None
    except Exception:
        return None


def _active_window_is_terminal() -> bool:
    """True when the focused window is a known terminal/console emulator."""
    return _foreground_window_class() in _TERMINAL_WINDOW_CLASSES


def _looks_like_terminal_garbage(text: str) -> bool:
    """True if *text* contains ANSI escapes or stray control characters.

    A reliable GUI selection never contains these; their presence means the
    clipboard captured terminal echo (e.g. ";2H") rather than real text.
    """
    return bool(_ANSI_ESCAPE.search(text) or _CONTROL_CHARS.search(text))


def read_preceding_text() -> str | None:
    """
    Return the text that sits before the cursor in the focused field.

    Returns:
        * The preceding text (possibly ``""`` = start of line/document, which
          tells the pipeline to capitalise the first letter).
        * ``None`` when context is unavailable or unreliable — a terminal
          window, or a clipboard read polluted with ANSI escape sequences.
          ``None`` tells the pipeline to skip both capitalization and leading
          spacing and paste the raw text as-is. This avoids injecting ``;2H``
          style terminal echo and avoids force-capitalising shell commands.
    """
    # Never run the Shift+Home / Ctrl+C trick in a terminal: it gets echoed as
    # raw ANSI ("\x1b[1;2H" → ";2H") and Ctrl+C can fire SIGINT.
    if _active_window_is_terminal():
        return None

    try:
        # 1. Save current clipboard content
        saved_clipboard = pyperclip.paste()
    except Exception:
        return None

    try:
        # 2. Place sentinel on clipboard so we can detect "nothing selected"
        pyperclip.copy(_SENTINEL)

        # 3. Select from cursor to line start
        keyboard.send("shift+home")
        time.sleep(_DELAY_SELECT)

        # 4. Copy selection to clipboard
        keyboard.send("ctrl+c")
        time.sleep(_DELAY_COPY)

        # 5. Read clipboard
        content: str = str(pyperclip.paste())

        # 6. Restore cursor to the original position.
        #    On some PCs / apps Ctrl+C deselects the text, leaving the
        #    caret at position 0 (line start).  A single Right then moves
        #    only one character right (position 1) instead of collapsing to
        #    the original cursor location.  The reliable approach:
        #      Home  → guarantee position 0  (works whether the selection
        #                                     is still active or not)
        #      Right × len(content)          → advance to original offset
        #    For the sentinel case (nothing selected, cursor already at
        #    line start) n_chars is 0 so no Right presses are emitted.
        n_chars = 0 if content == _SENTINEL else len(content)
        keyboard.send("home")
        time.sleep(_DELAY_DESELECT)
        for _ in range(n_chars):
            keyboard.press_and_release("right")
        if n_chars:
            time.sleep(_DELAY_DESELECT)

        # 7. If clipboard still holds the sentinel, nothing was selected
        #    (cursor was already at line start, or non-text window).
        if content == _SENTINEL:
            return ""

        # 8. Reject terminal echo / control characters: a real GUI selection
        #    never contains ANSI escapes. Treat as "no context" rather than
        #    feeding garbage like ";2H" into capitalization/spacing.
        if _looks_like_terminal_garbage(content):
            return None

        return content

    except Exception:
        # Best-effort: try to deselect so we don't leave a selection
        with contextlib.suppress(Exception):
            keyboard.send("right")
        return None

    finally:
        # Always restore the original clipboard, no matter what.
        with contextlib.suppress(Exception):
            pyperclip.copy(saved_clipboard)


# Default freshness window (seconds) for the last-output fallback below.
CONTEXT_FALLBACK_WINDOW_S = 60.0


def resolve_preceding_context(
    screen_context: str | None,
    *,
    last_output_text: str,
    last_output_ts: float,
    now: float,
    window_s: float = CONTEXT_FALLBACK_WINDOW_S,
) -> str | None:
    """Decide the cursor context to feed the post-processing pipeline.

    ``screen_context`` is what :func:`read_preceding_text` returned:
        * ``str`` (including ``""``) — the screen was read successfully; trust
          it as-is.
        * ``None`` — the screen could not be read safely (terminal, browser
          address bar, ANSI garbage). Fall back to our OWN last pasted output
          so the join-space + capitalization still work in those windows, but
          only when that output is recent enough to plausibly still sit right
          before the cursor.

    Using our own clean text as the fallback means we never re-read a terminal
    buffer, so ``;2H``-style echo can never leak into the result.

    Args:
        screen_context: Result of reading the screen (``None`` = unreadable).
        last_output_text: The text this app pasted on the previous dictation.
        last_output_ts: Monotonic timestamp of that previous paste.
        now: Current monotonic timestamp.
        window_s: Max age (seconds) for the fallback to be trusted.

    Returns:
        The context string to use, or ``None`` if none is available.
    """
    if screen_context is not None:
        return screen_context
    if last_output_text and (now - last_output_ts) < window_s:
        return last_output_text
    return None
