"""
Streaming downloader + archive extractor with progress callback.

Supports .zip and .tar.bz2 archives. Designed to be called from a
background thread; the progress callback is invoked from that thread.
"""

from __future__ import annotations

import contextlib
import os
import shutil
import tarfile
import tempfile
import urllib.request
import zipfile
from collections.abc import Callable

from voxapp.config import get_models_dir
from voxapp.registry import MODELS, model_dir

ProgressCB = Callable[[int, int, str], None]
# (downloaded_bytes, total_bytes_or_-1, stage_label)


_CHUNK = 256 * 1024  # 256 KiB


def _stream_download(url: str, dest_path: str, on_progress: ProgressCB) -> None:
    req = urllib.request.Request(url, headers={"User-Agent": "Vox/1.1"})
    with urllib.request.urlopen(req) as resp:  # nosec: trusted release URLs
        total = int(resp.headers.get("Content-Length", "-1"))
        downloaded = 0
        on_progress(0, total, "Загрузка")
        with open(dest_path, "wb") as f:
            while True:
                chunk = resp.read(_CHUNK)
                if not chunk:
                    break
                f.write(chunk)
                downloaded += len(chunk)
                on_progress(downloaded, total, "Загрузка")


def _extract(
    archive_path: str, archive_format: str, dest_dir: str, on_progress: ProgressCB
) -> None:
    os.makedirs(dest_dir, exist_ok=True)
    if archive_format == "zip":
        with zipfile.ZipFile(archive_path, "r") as zf:
            members = zf.infolist()
            total = len(members)
            for i, m in enumerate(members):
                zf.extract(m, dest_dir)
                on_progress(i + 1, total, "Распаковка")
    elif archive_format == "tar.bz2":
        with tarfile.open(archive_path, "r:bz2") as tf:
            tar_members = tf.getmembers()
            total = len(tar_members)
            for i, tm in enumerate(tar_members):
                tf.extract(tm, dest_dir)
                on_progress(i + 1, total, "Распаковка")
    else:
        raise ValueError(f"Unknown archive format: {archive_format}")


def download_model(
    model_id: str,
    on_progress: ProgressCB | None = None,
    on_log: Callable[[str], None] | None = None,
) -> str:
    """
    Download and install a model. Returns the absolute path to its
    extracted directory. Safe to call if the model is already installed
    (in which case it returns immediately).
    """
    if model_id not in MODELS:
        raise KeyError(f"Unknown model id: {model_id}")

    on_progress = on_progress or (lambda *a, **k: None)
    on_log = on_log or (lambda _msg: None)

    target_dir = model_dir(model_id)
    if os.path.isdir(target_dir):
        on_log(f"[DL] Model already installed: {target_dir}")
        return target_dir

    info = MODELS[model_id]
    models_root = get_models_dir()
    os.makedirs(models_root, exist_ok=True)

    # Download to a temp file (alongside, so rename is atomic on same FS)
    fd, tmp_archive = tempfile.mkstemp(
        prefix=f"{model_id}-",
        suffix="." + info["archive_format"],
        dir=models_root,
    )
    os.close(fd)

    try:
        on_log(f"[DL] {info['name']} ({info['size_mb']} МБ)")
        on_log(f"[DL] URL: {info['url']}")
        _stream_download(info["url"], tmp_archive, on_progress)

        on_log("[DL] Распаковка...")
        # Extract into a staging dir, then move into place atomically.
        staging = tempfile.mkdtemp(prefix=f"{model_id}-stage-", dir=models_root)
        try:
            _extract(tmp_archive, info["archive_format"], staging, on_progress)

            extracted = os.path.join(staging, info["extracted_dir"])
            if not os.path.isdir(extracted):
                # Some archives extract without a top-level folder (rare for
                # our list, but handle it): treat the staging dir as the
                # model itself.
                candidates = [
                    d for d in os.listdir(staging) if os.path.isdir(os.path.join(staging, d))
                ]
                if len(candidates) == 1:
                    extracted = os.path.join(staging, candidates[0])
                else:
                    raise FileNotFoundError(
                        f"Expected '{info['extracted_dir']}' in archive, found: {candidates}"
                    )

            # Move into final location.
            if os.path.isdir(target_dir):
                shutil.rmtree(target_dir)
            shutil.move(extracted, target_dir)
        finally:
            shutil.rmtree(staging, ignore_errors=True)

        on_log(f"[DL] Установлено: {target_dir}")
        on_progress(1, 1, "Готово")
        return target_dir
    finally:
        with contextlib.suppress(OSError):
            os.remove(tmp_archive)


def uninstall_model(model_id: str) -> bool:
    """Delete the extracted folder for a model. Returns True if removed."""
    if model_id not in MODELS:
        return False
    target_dir = model_dir(model_id)
    if not os.path.isdir(target_dir):
        return False
    shutil.rmtree(target_dir, ignore_errors=True)
    return not os.path.isdir(target_dir)
