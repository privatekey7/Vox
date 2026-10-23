"""Общее для скриптов Colab: моно 16 кГц без наложения частот (np.interp без фильтра заворачивает 8–12 кГц в полосу модели)."""
from math import gcd

import numpy as np
from scipy.signal import resample_poly


def to16k(pcm, rate):
    pcm = np.asarray(pcm, dtype=np.float32)
    if pcm.ndim > 1:
        pcm = pcm.mean(axis=1)
    if rate != 16000:
        g = gcd(16000, int(rate))
        pcm = resample_poly(pcm, 16000 // g, int(rate) // g).astype(np.float32)
    return pcm
