"""Recording → recognition → paste flow, with hardware boundaries faked."""

from __future__ import annotations

import threading
import time

import numpy as np
import pytest

from tests import conftest
from voxapp import worker
from voxapp.engines import Engine


class FakeEngine(Engine):
    def __init__(self, text: str = "Привет, мир.") -> None:
        self.text = text
        self.received: list[np.ndarray] = []

    def transcribe(self, pcm_int16: np.ndarray) -> str:
        self.received.append(pcm_int16)
        return self.text


class Event:
    def __init__(self, event_type: str) -> None:
        self.name = worker.HOTKEY
        self.event_type = event_type


def _speech(seconds: float) -> np.ndarray:
    return (np.ones((int(seconds * 16000), 1)) * 2000).astype(np.int16)


@pytest.fixture(autouse=True)
def setup(monkeypatch):
    engine = FakeEngine()
    monkeypatch.setattr(worker, "_engine", engine)
    monkeypatch.setattr(worker, "_post_processor", None)
    monkeypatch.setattr(worker, "_db", None)
    monkeypatch.setattr(worker, "_monitor", None)
    monkeypatch.setattr(worker, "_tracker", None)
    monkeypatch.setattr(worker, "_suppress_until", 0.0)
    monkeypatch.setattr(worker, "CLIPBOARD_RESTORE_DELAY_SEC", 0.0)
    conftest.clipboard.text = "https://example.com/link"
    conftest.clipboard.writes.clear()
    conftest.keyboard.sent.clear()
    yield engine


def _dictate(audio: np.ndarray | None, *, hold_sec: float = 0.3, late: np.ndarray | None = None):
    worker._on_event(Event("down"))
    if audio is not None:
        worker._audio_callback(audio, len(audio), None, None)
    time.sleep(hold_sec)
    worker._on_event(Event("up"))
    if late is not None:
        worker._audio_callback(late, len(late), None, None)  # block still in flight
    worker._finish_recording(*worker._jobs.get_nowait())


def test_dictation_is_pasted_at_cursor(setup):
    _dictate(_speech(1))
    assert conftest.keyboard.sent[-1] == "ctrl+v"
    assert "Привет, мир." in conftest.clipboard.writes


def test_users_clipboard_is_restored_after_paste(setup):
    _dictate(_speech(1))
    assert conftest.clipboard.text == "https://example.com/link"


def test_clipboard_copied_by_user_during_paste_is_kept(setup, monkeypatch):
    real_send = conftest.keyboard.send

    def send_and_user_copies(combo):
        real_send(combo)
        if combo == "ctrl+v":
            conftest.clipboard.text = "new link"

    monkeypatch.setattr(conftest.keyboard, "send", send_and_user_copies)
    _dictate(_speech(1))
    assert conftest.clipboard.text == "new link"


def test_audio_arriving_right_after_release_is_kept(setup):
    _dictate(_speech(1), late=_speech(0.1))
    assert len(setup.received[0]) == int(1.1 * 16000)


def test_silent_recording_is_not_pasted(setup):
    _dictate(np.zeros((16000, 1), dtype=np.int16))
    assert conftest.keyboard.sent == []
    assert setup.received == []


def test_missing_audio_reopens_microphone(setup, monkeypatch):
    calls = []
    monkeypatch.setattr(worker, "_reopen_microphone", lambda: calls.append(1))
    _dictate(None)
    assert calls == [1]
    assert conftest.keyboard.sent == []


def test_too_short_press_is_ignored(setup):
    _dictate(_speech(0.1), hold_sec=0.0)
    assert setup.received == []


def test_failed_dictation_does_not_stop_later_ones(setup, monkeypatch):
    texts = iter([RuntimeError("model crashed"), "Вторая диктовка."])

    def flaky(_audio):
        result = next(texts)
        if isinstance(result, Exception):
            raise result
        return result

    monkeypatch.setattr(setup, "transcribe", flaky)
    processor = threading.Thread(target=worker._processor_loop, daemon=True)
    processor.start()
    for _ in range(2):
        worker._on_event(Event("down"))
        worker._audio_callback(_speech(1), 16000, None, None)
        time.sleep(0.3)
        worker._on_event(Event("up"))
    worker._jobs.put(None)
    processor.join(timeout=5)
    assert not processor.is_alive()
    assert "Вторая диктовка." in conftest.clipboard.writes


class OtherKey(Event):
    def __init__(self, event_type: str) -> None:
        super().__init__(event_type)
        self.name = "c"


def test_hotkey_used_in_shortcut_is_not_dictation(setup):
    worker._on_event(Event("down"))
    worker._audio_callback(_speech(1), 16000, None, None)
    worker._on_event(OtherKey("down"))  # e.g. Right Ctrl + C
    worker._on_event(OtherKey("up"))
    time.sleep(0.3)
    worker._on_event(Event("up"))
    assert worker._jobs.empty()
    assert conftest.keyboard.sent == []


def test_next_dictation_after_shortcut_works(setup):
    test_hotkey_used_in_shortcut_is_not_dictation(setup)
    _dictate(_speech(1))
    assert conftest.keyboard.sent[-1] == "ctrl+v"
