"""
Split long recordings at pauses before recognition.

Both models degrade on long input: in vox.log 21% of Parakeet dictations
longer than 15 s came back empty (vs 2% for 5–15 s), and GigaAM drops text
after ~100 s and fails outright beyond that. Recognising short pieces and
joining the text avoids both.

The cut points are the quietest moments (lowest short-term energy) inside a
search window, so pieces break between words. No samples are dropped:
concatenating the pieces gives back the original audio exactly.

Usage:
    from voxapp.segmenter import split_on_pauses
    pieces = split_on_pauses(pcm_int16, 16000, max_seconds=12)
"""

from __future__ import annotations

import numpy as np

# Pieces are cut no earlier than this (shorter pieces lose context).
_MIN_PIECE_FRACTION = 0.5
# Energy is measured over frames of this length...
_FRAME_SEC = 0.02
# ...and smoothed over this window, so a cut needs a real pause, not a plosive.
_SMOOTH_SEC = 0.3


def _quietest_frame(energy: np.ndarray, lo: int, hi: int) -> int:
    window = max(1, int(_SMOOTH_SEC / _FRAME_SEC))
    smoothed = np.convolve(energy[lo:hi], np.ones(window) / window, mode="same")
    return lo + int(np.argmin(smoothed))


def split_on_pauses(
    audio: np.ndarray, sample_rate: int, *, max_seconds: float
) -> list[np.ndarray]:
    """Return consecutive slices of ``audio``, each at most ``max_seconds`` long."""
    if len(audio) == 0:
        return []
    max_len = int(max_seconds * sample_rate)
    if len(audio) <= max_len:
        return [audio]

    frame = max(1, int(_FRAME_SEC * sample_rate))
    n_frames = len(audio) // frame
    framed = audio[: n_frames * frame].astype(np.float32).reshape(n_frames, frame)
    energy = np.sqrt(np.mean(framed**2, axis=1))

    pieces: list[np.ndarray] = []
    start = 0
    while len(audio) - start > max_len:
        lo = (start + int(max_len * _MIN_PIECE_FRACTION)) // frame
        hi = min(n_frames, (start + max_len) // frame)
        cut = _quietest_frame(energy, lo, hi) * frame + frame // 2
        cut = min(cut, start + max_len)
        pieces.append(audio[start:cut])
        start = cut
    pieces.append(audio[start:])
    return pieces
