"""
Speech recognition engines.

One backend: sherpa-onnx, running a NeMo transducer model — GigaAM v3
(Russian) or Parakeet TDT v3 (English + 24 European languages). Long
recordings are split at pauses before decoding (see segmenter.py).

The engine takes int16 PCM mono at SAMPLE_RATE Hz and returns text.
"""

from __future__ import annotations

import gc
import json
import os
import shutil
import sys
import tempfile
from abc import ABC, abstractmethod
from urllib.request import Request, urlopen

import numpy as np

from voxapp.config import MAX_SEGMENT_SEC, SAMPLE_RATE
from voxapp.registry import MODELS, model_dir
from voxapp.segmenter import split_on_pauses

# ---------------------------------------------------------------------------
# DLL preload (Windows only)
#
# sherpa-onnx-core bundles its own onnxruntime.dll inside
# `site-packages/sherpa_onnx/lib/`.  On Windows the system DLL search order
# may pick up an *older* onnxruntime.dll from another location (e.g.
# C:\Python3XX\, another venv in PATH, or a DirectML install), causing an
# ORT API version mismatch at startup:
#
#   "The requested API version [24] is not available, only API versions
#    [1, 17] are supported in this build."
#
# Fix: explicitly add the bundled lib dir to the DLL search path *before*
# Python loads the native extension.  `os.add_dll_directory()` (Python 3.8+)
# is the recommended approach on Windows.
# ---------------------------------------------------------------------------

_sherpa_dll_preloaded = False


def _get_sherpa_lib_dir() -> str | None:
    """Return the ``sherpa_onnx/lib/`` directory inside the current venv."""
    import importlib.util

    spec = importlib.util.find_spec("sherpa_onnx")
    if spec and spec.origin:
        lib_dir = os.path.join(os.path.dirname(spec.origin), "lib")
        if os.path.isdir(lib_dir):
            return lib_dir
    return None


def _find_ort_dll() -> str | None:
    """Locate the onnxruntime.dll bundled with sherpa-onnx-core.

    Tries multiple strategies because different installers (pip, uv)
    place files differently — especially when two wheels share the
    ``sherpa_onnx`` namespace:

    1. ``sherpa_onnx/lib/`` next to the package's ``__init__.py``
       (works with pip, merged installs, and after strategy 3 runs once).
    2. Package metadata for ``sherpa-onnx-core`` (works when uv keeps
       the dist-info even without merging namespace directories).
    3. Download the ``sherpa-onnx-core`` wheel from PyPI and extract
       all ``sherpa_onnx/lib/*`` files into the venv (robust workaround
       for uv not merging files from two packages sharing a namespace).
    """
    import importlib.metadata

    lib_dir = _get_sherpa_lib_dir()

    # Strategy 1: DLL already in sherpa_onnx/lib/ (pip or previous extract)
    if lib_dir:
        candidate = os.path.join(lib_dir, "onnxruntime.dll")
        if os.path.isfile(candidate):
            return candidate

    # Strategy 2: metadata-based
    try:
        dist = importlib.metadata.distribution("sherpa-onnx-core")
        for f in dist.files or []:
            if f.name == "onnxruntime.dll":
                candidate = str(f.locate())
                if os.path.isfile(candidate):
                    return candidate
    except importlib.metadata.PackageNotFoundError:
        pass

    # Strategy 3: download wheel from PyPI and extract lib/ files
    if lib_dir:
        extracted = _download_sherpa_core_dlls(lib_dir)
        if extracted:
            return extracted

    return None


def _download_sherpa_core_dlls(target_dir: str) -> str | None:
    """Download the sherpa-onnx-core wheel from PyPI and extract DLLs.

    When *uv* installs ``sherpa-onnx`` and ``sherpa-onnx-core`` (two
    wheels sharing the ``sherpa_onnx/`` namespace directory), it keeps
    only one package's files — the core DLLs never reach the venv and
    may not even appear in the uv cache on Windows.

    This function downloads the matching wheel directly from PyPI and
    extracts all ``sherpa_onnx/lib/*`` files (DLLs, .lib stubs) into
    the venv's ``sherpa_onnx/lib/``.  Runs only once — subsequent
    starts find the DLLs via strategy 1.
    """
    import importlib.metadata
    import struct
    import zipfile
    from urllib.error import URLError

    # Match the installed sherpa-onnx version
    try:
        version = importlib.metadata.version("sherpa-onnx")
    except importlib.metadata.PackageNotFoundError:
        return None

    # Determine platform tag for the current OS + arch
    bits = struct.calcsize("P") * 8  # 32 or 64
    if sys.platform == "win32":
        plat_tag = "win_amd64" if bits == 64 else "win32"
    elif sys.platform == "darwin":
        import platform as _plat

        plat_tag = "arm64" if _plat.machine() == "arm64" else "x86_64"
    else:
        plat_tag = "x86_64"

    # Query PyPI JSON API for download URLs
    pypi_url = f"https://pypi.org/pypi/sherpa-onnx-core/{version}/json"
    try:
        resp = urlopen(
            Request(pypi_url, headers={"Accept": "application/json"}),
            timeout=15,
        )
        data = json.loads(resp.read())
    except (URLError, OSError, ValueError):
        return None

    # Find the matching platform wheel
    wheel_url: str | None = None
    for entry in data.get("urls", []):
        if entry.get("packagetype") != "bdist_wheel":
            continue
        if plat_tag in entry["filename"]:
            wheel_url = entry["url"]
            break

    if wheel_url is None:
        return None

    # Download wheel to a temp directory and extract lib/ files
    print(f"[INIT] Downloading sherpa-onnx-core DLLs ({version})...")
    with tempfile.TemporaryDirectory() as tmpdir:
        whl_path = os.path.join(tmpdir, "sherpa_onnx_core.whl")
        try:
            resp = urlopen(wheel_url, timeout=120)
            with open(whl_path, "wb") as fout:
                shutil.copyfileobj(resp, fout)
        except (URLError, OSError):
            return None

        try:
            with zipfile.ZipFile(whl_path) as zf:
                lib_prefix = "sherpa_onnx/lib/"
                for name in zf.namelist():
                    if not name.startswith(lib_prefix) or name.endswith("/"):
                        continue
                    basename = os.path.basename(name)
                    dst = os.path.join(target_dir, basename)
                    if not os.path.exists(dst):
                        with zf.open(name) as src, open(dst, "wb") as out:
                            shutil.copyfileobj(src, out)
        except (zipfile.BadZipFile, OSError):
            return None

    ort_dll = os.path.join(target_dir, "onnxruntime.dll")
    if os.path.isfile(ort_dll):
        print(f"[INIT] Installed sherpa-onnx-core DLLs to {target_dir}")
        return ort_dll
    return None


def _preload_sherpa_dlls() -> None:
    """Pre-load the correct onnxruntime.dll before ``import sherpa_onnx``.

    On Windows the system DLL search order can pick up an older
    ``onnxruntime.dll`` from ``System32``, ``PATH``, or another install
    instead of the one bundled with ``sherpa-onnx-core``.  We find the
    bundled DLL and load it via ``ctypes.CDLL`` so that the correct
    version is already in the process address space when the native PYD
    extension asks for it.

    Safe to call multiple times and on non-Windows platforms (no-op).
    """
    global _sherpa_dll_preloaded
    if _sherpa_dll_preloaded or sys.platform != "win32":
        return

    try:
        import ctypes

        dll_path = _find_ort_dll()
        if dll_path is None:
            print("[INIT] Warning: onnxruntime.dll not found in sherpa-onnx-core")
            _sherpa_dll_preloaded = True
            return

        dll_dir = os.path.dirname(dll_path)
        os.add_dll_directory(dll_dir)
        ctypes.CDLL(dll_path)
        print(f"[INIT] Pre-loaded: {dll_path}")
    except Exception as exc:
        print(f"[INIT] Warning: could not preload sherpa-onnx DLLs: {exc}")

    _sherpa_dll_preloaded = True


# ---------------------------------------------------------------------------
# Path safety helper (sherpa-onnx can choke on non-ASCII paths on Windows
# because its C core receives narrow-char strings).
# ---------------------------------------------------------------------------


def _ascii_safe_path(original_path: str, cache_subdir: str) -> str:
    """If `original_path` is non-ASCII, mirror it into %TEMP%/<cache_subdir>."""
    try:
        original_path.encode("ascii")
        return original_path
    except UnicodeEncodeError:
        pass

    cache_base = os.path.join(tempfile.gettempdir(), cache_subdir)
    safe_path = os.path.join(cache_base, os.path.basename(original_path))

    if os.path.isdir(safe_path):
        # Assume it's valid if a marker file is present; lightweight check.
        return safe_path

    print(f"[INIT] Path contains non-ASCII; copying model to {safe_path}")
    if os.path.exists(safe_path):
        shutil.rmtree(safe_path)
    shutil.copytree(original_path, safe_path)
    return safe_path


# ---------------------------------------------------------------------------
# Base class
# ---------------------------------------------------------------------------


class Engine(ABC):
    """Stateful recognizer wrapping one loaded model."""

    name: str = "base"

    @abstractmethod
    def transcribe(self, pcm_int16: np.ndarray) -> str:
        """Take 1-D int16 PCM @ SAMPLE_RATE Hz and return recognised text."""

    def unload(self) -> None:
        """Release resources. Default: no-op (GC handles it)."""


# ---------------------------------------------------------------------------
# sherpa-onnx
# ---------------------------------------------------------------------------


class SherpaEngine(Engine):
    name = "sherpa-onnx"

    def __init__(self, model_id: str):
        _preload_sherpa_dlls()
        import sherpa_onnx  # lazy import — must come AFTER DLL preload

        info = MODELS[model_id]
        subtype = info["engine_subtype"]
        base = _ascii_safe_path(model_dir(model_id), "sherpa_models")
        # Build a `files` dict with paths inside the safe-copied base.
        files = {key: os.path.join(base, rel) for key, rel in info["files"].items()}

        print(f"[INIT] Loading sherpa-onnx model ({subtype}): {model_id}")

        cpu_threads = max(1, (os.cpu_count() or 4) // 2)

        if subtype == "nemo-transducer":
            self._recognizer = sherpa_onnx.OfflineRecognizer.from_transducer(
                encoder=files["encoder"],
                decoder=files["decoder"],
                joiner=files["joiner"],
                tokens=files["tokens"],
                num_threads=cpu_threads,
                sample_rate=SAMPLE_RATE,
                feature_dim=80,
                model_type="nemo_transducer",
                provider="cpu",
            )
        else:
            raise ValueError(f"Unsupported sherpa-onnx subtype: {subtype}")

        print(f"[INIT] sherpa-onnx model loaded ({subtype})")

    def transcribe(self, pcm_int16: np.ndarray) -> str:
        # Long input degrades both models (empty text, dropped words), so
        # recognise pause-separated pieces and join them.
        pieces = split_on_pauses(pcm_int16, SAMPLE_RATE, max_seconds=MAX_SEGMENT_SEC)
        streams = []
        for piece in pieces:
            stream = self._recognizer.create_stream()
            # sherpa-onnx expects float32 normalised to [-1, 1]
            stream.accept_waveform(SAMPLE_RATE, piece.astype(np.float32) / 32768.0)
            streams.append(stream)
        self._recognizer.decode_streams(streams)
        texts = [(s.result.text or "").strip() for s in streams]
        if len(pieces) > 1:
            print(f"[STT] Split into {len(pieces)} pieces; empty: {texts.count('')}")
        return " ".join(t for t in texts if t)

    def unload(self) -> None:
        self._recognizer = None
        gc.collect()


# ---------------------------------------------------------------------------
# Factory
# ---------------------------------------------------------------------------


def build_engine(model_id: str) -> Engine:
    """Construct the right Engine instance for the given catalog entry."""
    if model_id not in MODELS:
        raise KeyError(f"Unknown model id: {model_id}")
    info = MODELS[model_id]
    eng_type = info["engine"]
    if eng_type == "sherpa-onnx":
        return SherpaEngine(model_id)
    raise ValueError(f"Unknown engine type: {eng_type}")
