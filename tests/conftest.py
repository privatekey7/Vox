"""Test setup: fake the Windows-only / hardware modules the worker imports.

Only external boundaries are faked (keyboard hook, microphone, clipboard,
winsound); Vox's own code runs for real.
"""

from __future__ import annotations

import sys
import types


class FakeClipboard:
    def __init__(self) -> None:
        self.text = ""
        self.writes: list[str] = []

    def copy(self, text: str) -> None:
        self.text = text
        self.writes.append(text)

    def paste(self) -> str:
        return self.text


class FakeKeyboard(types.ModuleType):
    KEY_DOWN = "down"
    KEY_UP = "up"

    def __init__(self) -> None:
        super().__init__("keyboard")
        self.sent: list[str] = []

    def send(self, combo: str) -> None:
        self.sent.append(combo)

    def hook(self, *_a, **_k) -> None: ...

    def wait(self, *_a, **_k) -> None: ...


class PyperclipException(RuntimeError):  # noqa: N818 — mirrors the real pyperclip name
    pass


clipboard = FakeClipboard()
keyboard = FakeKeyboard()

_pyperclip = types.ModuleType("pyperclip")
_pyperclip.copy = lambda text: clipboard.copy(text)  # type: ignore[attr-defined]
_pyperclip.paste = lambda: clipboard.paste()  # type: ignore[attr-defined]
_pyperclip.PyperclipException = PyperclipException  # type: ignore[attr-defined]

_winsound = types.ModuleType("winsound")
_winsound.Beep = lambda *_a: None  # type: ignore[attr-defined]

_sounddevice = types.ModuleType("sounddevice")
_sounddevice.InputStream = object  # type: ignore[attr-defined]

sys.modules.setdefault("winsound", _winsound)
sys.modules["keyboard"] = keyboard
sys.modules["pyperclip"] = _pyperclip
sys.modules["sounddevice"] = _sounddevice
