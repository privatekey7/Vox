"""
Catalog of speech-to-text models available to Vox.

Two models (D030): GigaAM v3 for Russian (default) and Parakeet TDT v3 for
English / other European languages. Both run on sherpa-onnx.

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
    needs_itn:            convert number-words to digits (False when the
                          model already writes numbers as digits)
"""

from __future__ import annotations

import os
from typing import Any

from voxapp.config import get_models_dir

_SHERPA_BASE = "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models"


MODELS: dict[str, dict[str, Any]] = {
    # ---------------- GigaAM v3 RNNT + punctuation (Russian) — default ----------------
    "gigaam-v3-rnnt-punct": {
        "name": "GigaAM v3 (русский)",
        "engine": "sherpa-onnx",
        "engine_subtype": "nemo-transducer",
        "language": "ru",
        "size_mb": 162,
        "url": f"{_SHERPA_BASE}/sherpa-onnx-nemo-transducer-punct-giga-am-v3-russian-2025-12-16.tar.bz2",
        "archive_format": "tar.bz2",
        "extracted_dir": "sherpa-onnx-nemo-transducer-punct-giga-am-v3-russian-2025-12-16",
        "files": {
            "encoder": "encoder.int8.onnx",
            "decoder": "decoder.onnx",
            "joiner": "joiner.onnx",
            "tokens": "tokens.txt",
        },
        "needs_term_replace": True,
        "needs_punctuation": False,
        # GigaAM writes "11:30", "150 000 ₽" itself; our ITN would only
        # damage that ("Один из" → "1 из").
        "needs_itn": False,
        "description": (
            "Лучший выбор, если говорите по-русски. Модель от Сбера: "
            "точнее понимает русскую речь, сама ставит знаки препинания "
            "и пишет числа цифрами. Английские слова пишет кириллицей, "
            "IT-термины исправляет словарь Vox."
        ),
    },
    # ---------------- Parakeet TDT v3 (25 EU languages) ----------------
    "parakeet-tdt-0.6b-v3": {
        "name": "Parakeet TDT v3 (25 EU языков)",
        "engine": "sherpa-onnx",
        "engine_subtype": "nemo-transducer",
        "language": "multi",
        "size_mb": 465,
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
        "needs_itn": True,
        "description": (
            "Выбирайте, если говорите в основном по-английски или на другом "
            "европейском языке. Модель от NVIDIA: английский + 24 европейских "
            "языка, включая русский. Английские термины пишет латиницей."
        ),
    },
}


DEFAULT_MODEL_ID = "gigaam-v3-rnnt-punct"
RECOMMENDED_MODEL_ID = "gigaam-v3-rnnt-punct"


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
