#!/usr/bin/env python3
"""
Vox — Universal Voice Input (entry point).

Push-to-talk voice recognition daemon for Windows.
Hold a configurable hotkey → speak → release → text is pasted at the cursor.

Recognition runs fully offline on a single model — Parakeet TDT v3
(25 European languages) via sherpa-onnx. The model is downloaded on first
run and can be repaired or removed from the system tray.
"""

from __future__ import annotations

import contextlib
import os
import sys
import threading
import time

from voxapp.config import (
    get_log_dir,
    load_config,
    update_config,
)
from voxapp.registry import (
    DEFAULT_MODEL_ID,
    MODELS,
    RECOMMENDED_MODEL_ID,
    is_installed,
)


def _setup_logging() -> tuple[str, str]:
    """Redirect stdout/stderr to a log file (required for --windowed exe)."""
    log_dir = get_log_dir()
    os.makedirs(log_dir, exist_ok=True)
    log_file = os.path.join(log_dir, "vox.log")

    if os.path.exists(log_file) and os.path.getsize(log_file) > 5 * 1024 * 1024:
        os.remove(log_file)

    log_fp = open(log_file, "a", encoding="utf-8", buffering=1)

    if getattr(sys, "frozen", False):
        # Packaged .exe has no console — redirect everything to the log file.
        sys.stdout = log_fp
        sys.stderr = log_fp
    else:
        # Dev mode: also mirror to the original terminal so the user sees
        # `[REC] Listening...` etc. live.
        class _Tee:
            def __init__(self, *streams):
                self._streams = streams

            def write(self, data):
                for s in self._streams:
                    try:
                        s.write(data)
                        s.flush()
                    except Exception:
                        pass

            def flush(self):
                for s in self._streams:
                    with contextlib.suppress(Exception):
                        s.flush()

        sys.stdout = _Tee(sys.__stdout__, log_fp)
        sys.stderr = _Tee(sys.__stderr__, log_fp)

    return log_dir, log_file


def _resolve_initial_model() -> str | None:
    """
    Decide which model to load on startup.

    Priority:
        1. config.active_model, if set and installed.
        2. Any installed model (prefer recommended, then default).
        3. None — meaning we must show the first-run wizard.
    """
    cfg = load_config()
    active = cfg.get("active_model")
    if active and active in MODELS and is_installed(active):
        return active

    for candidate in (RECOMMENDED_MODEL_ID, DEFAULT_MODEL_ID):
        if is_installed(candidate):
            return candidate

    for mid in MODELS:
        if is_installed(mid):
            return mid

    return None


def _show_first_run_wizard() -> str | None:
    """Block on the tkinter picker. Returns the chosen model id, or None."""
    from voxapp.gui import run_first_run_wizard

    chosen: dict[str, str] = {}

    def _on_apply(model_id: str, lang: str) -> None:
        chosen["id"] = model_id
        chosen["lang"] = lang
        update_config(active_model=model_id, language=lang)

    ok = run_first_run_wizard(on_apply=_on_apply)
    if not ok or "id" not in chosen:
        return None
    return chosen["id"]


def main() -> None:
    log_dir, _log_file = _setup_logging()
    print(f"\n=== Vox started at {time.strftime('%Y-%m-%d %H:%M:%S')} ===")

    model_id = _resolve_initial_model()
    if model_id is None:
        print("[INIT] No model installed — showing first-run wizard")
        model_id = _show_first_run_wizard()
        if model_id is None:
            print("[INIT] First-run cancelled, exiting")
            return

    # Background worker thread (engine + mic + keyboard hook)
    from voxapp import worker

    t = threading.Thread(
        target=worker.run,
        args=(model_id,),
        daemon=True,
        name="VoxWorker",
    )
    t.start()

    # Tray icon (blocks the main thread until the user quits)
    try:
        from voxapp.tray import setup_tray

        setup_tray(log_dir)
    except Exception as e:
        print(f"[FATAL] Tray failed: {e}")
        import traceback

        traceback.print_exc()


if __name__ == "__main__":
    main()
