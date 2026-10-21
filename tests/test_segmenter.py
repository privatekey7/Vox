"""Splitting long recordings at pauses (long dictations returned empty text)."""

import numpy as np

from voxapp.segmenter import split_on_pauses

SR = 16000


def _speech(seconds: float) -> np.ndarray:
    rng = np.random.default_rng(0)
    return (rng.standard_normal(int(seconds * SR)) * 3000).astype(np.int16)


def _silence(seconds: float) -> np.ndarray:
    return np.zeros(int(seconds * SR), dtype=np.int16)


def test_short_audio_is_returned_as_single_piece():
    audio = _speech(8)
    pieces = split_on_pauses(audio, SR, max_seconds=12)
    assert len(pieces) == 1
    assert np.array_equal(pieces[0], audio)


def test_long_audio_is_split_into_pieces_not_longer_than_max():
    audio = np.concatenate([_speech(7), _silence(0.5)] * 6)  # ~45 s
    pieces = split_on_pauses(audio, SR, max_seconds=12)
    assert len(pieces) >= 4
    assert all(len(p) <= 12 * SR for p in pieces)


def test_split_loses_no_samples():
    audio = np.concatenate([_speech(7), _silence(0.5)] * 6)
    pieces = split_on_pauses(audio, SR, max_seconds=12)
    assert np.array_equal(np.concatenate(pieces), audio)


def test_cuts_land_inside_pauses():
    audio = np.concatenate([_speech(9), _silence(0.6), _speech(9), _silence(0.6), _speech(9)])
    pieces = split_on_pauses(audio, SR, max_seconds=12)
    pos = 0
    for p in pieces[:-1]:
        pos += len(p)
        assert np.all(audio[pos - 160 : pos + 160] == 0), (
            f"cut at {pos / SR:.2f}s is not in silence"
        )


def test_audio_without_pauses_is_still_split():
    audio = _speech(30)
    pieces = split_on_pauses(audio, SR, max_seconds=12)
    assert all(len(p) <= 12 * SR for p in pieces)
    assert np.array_equal(np.concatenate(pieces), audio)


def test_empty_audio_returns_no_pieces():
    assert split_on_pauses(np.zeros(0, dtype=np.int16), SR, max_seconds=12) == []
