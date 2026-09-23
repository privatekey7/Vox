"""
Catalog of speech-to-text models available to Vox.

Since D028 Vox ships a single model — Parakeet TDT v3 (25 EU languages,
one model). The multi-model catalog (Vosk / GigaAM / Whisper / SenseVoice)
was removed.

Each entry describes:

    engine:               "sherpa-onnx"
    engine_subtype:       sherpa-onnx factory variant ("nemo-transducer")
    url:                  archive URL (tar.bz2 for sherpa-onnx)
    archive_format:       "tar.bz2"
    extracted_dir:        the top-level folder produced by extracting
                          the archive into the models dir
    files:                dict of relative paths inside extracted_dir,
                          named by the engine factory (see engines.py)
    needs_term_replace:   apply TERM_REPLACEMENTS post-processing
    needs_punctuation:    apply add_punctuation post-processing
"""

from __future__ import annotations

import os
from typing import Any

from voxapp.config import get_models_dir

_SHERPA_BASE = "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models"


MODELS: dict[str, dict[str, Any]] = {
    # ---------------- Parakeet TDT v3 (25 EU languages) — the only model ----------------
    "parakeet-tdt-0.6b-v3": {
        "name": "Parakeet TDT v3 (25 EU языков)",
        "engine": "sherpa-onnx",
        "engine_subtype": "nemo-transducer",
        "language": "multi",
        "size_mb": 670,
        "url": f"{_SHERPA_BASE}/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8.tar.bz2",
        "archive_format": "tar.bz2",
        "extracted_dir": "sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8",
        "files": {
            "encoder": "encoder.int8.onnx",
            "decoder": "decoder.int8.onnx",
            "joiner": "joiner.int8.onnx",
            "tokens": "tokens.txt",
        },
        "needs_term_replace": True,
        "needs_punctuation": False,
        "description": (
            "Универсальная модель от NVIDIA: английский + 24 европейских "
            "языка, включая русский. Быстрая, очень точная. Числа пишет "
            "словами."
        ),
    },
}


DEFAULT_MODEL_ID = "parakeet-tdt-0.6b-v3"
RECOMMENDED_MODEL_ID = "parakeet-tdt-0.6b-v3"


def model_dir(model_id: str) -> str:
    """Absolute path where the model's extracted folder should live."""
    return os.path.join(get_models_dir(), MODELS[model_id]["extracted_dir"])


def is_installed(model_id: str) -> bool:
    """A model is installed if its extracted_dir exists on disk."""
    if model_id not in MODELS:
        return False
    return os.path.isdir(model_dir(model_id))


def resolved_files(model_id: str) -> dict[str, str]:
    """Return absolute paths for the model's files dict."""
    base = model_dir(model_id)
    return {key: os.path.join(base, rel) for key, rel in MODELS[model_id]["files"].items()}
