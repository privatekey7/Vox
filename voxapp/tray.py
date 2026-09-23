"""
System tray icon, menus, and the bridge between the worker and the GUI.
"""

from __future__ import annotations

import contextlib
import os
import sys
import threading

from voxapp import worker
from voxapp.config import (
    HOTKEY_CHOICES,
    VALID_HOTKEYS,
    get_hotkey_display,
    get_models_dir,
    update_config,
)
from voxapp.downloader import uninstall_model
from voxapp.registry import DEFAULT_MODEL_ID, MODELS, is_installed


def _get_icon_image():
    from PIL import Image

    icon_path = (
        os.path.join(sys._MEIPASS, "vox.ico")  # type: ignore[attr-defined]
        if getattr(sys, "frozen", False)
        else os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "vox.ico")
    )
    if os.path.exists(icon_path):
        return Image.open(icon_path)
    return Image.new("RGB", (64, 64), color=(59, 130, 246))


def setup_tray(log_dir: str) -> None:
    """Build and run the tray icon (blocks the calling thread)."""
    import pystray

    # ---- callbacks ------------------------------------------------------

    def on_open_logs(icon, item):
        try:
            os.startfile(log_dir)  # type: ignore[attr-defined]
        except Exception as e:
            print(f"[TRAY] Failed to open logs: {e}")

    def on_open_models(icon, item):
        try:
            models_dir = get_models_dir()
            os.makedirs(models_dir, exist_ok=True)
            os.startfile(models_dir)  # type: ignore[attr-defined]
        except Exception as e:
            print(f"[TRAY] Failed to open models: {e}")

    def _active_model_id() -> str:
        """The single recognition model Vox runs on."""
        return worker.get_active_model_id() or DEFAULT_MODEL_ID

    def on_redownload_model(icon, item):
        """Re-download (repair) the recognition model in its own thread."""

        def _run():
            from voxapp.gui import run_model_download

            run_model_download(
                _active_model_id(),
                force=True,
                on_done=lambda _ok: _refresh_menu(),
            )

        threading.Thread(target=_run, daemon=True, name="VoxModelRedownload").start()

    def on_delete_model(icon, item):
        """Delete the recognition model files (after a clear warning)."""

        def _run():
            from tkinter import Tk, messagebox

            mid = _active_model_id()
            name = MODELS.get(mid, {}).get("name", mid)
            root = Tk()
            root.withdraw()
            try:
                ok = messagebox.askyesno(
                    "Удалить модель",
                    f"Удалить файлы модели «{name}»?\n\n"
                    "Распознавание продолжит работать до перезапуска Vox, "
                    "а при следующем запуске модель нужно будет скачать заново.",
                    icon="warning",
                )
                if ok:
                    uninstall_model(mid)
            finally:
                with contextlib.suppress(Exception):
                    root.destroy()
            _refresh_menu()

        threading.Thread(target=_run, daemon=True, name="VoxModelDelete").start()

    def _refresh_menu():
        """Rebuild the tray menu (e.g. after the install state changed)."""
        with contextlib.suppress(Exception):
            icon.update_menu()

    def on_history(icon, item):
        """Open the history window in its own thread."""
        db = worker.get_db()
        if db is None:
            print("[TRAY] Database not available")
            return

        def _run():
            from voxapp.gui import run_history

            run_history(db=db)

        threading.Thread(target=_run, daemon=True, name="VoxHistory").start()

    def on_set_hotkey(icon, event_name: str):
        if event_name not in VALID_HOTKEYS or event_name == worker.HOTKEY:
            return
        worker.set_hotkey(event_name)
        update_config(hotkey=event_name)
        print(f"[TRAY] Hotkey changed to {get_hotkey_display(event_name)}")
        icon.update_menu()

    def on_quit(icon, item):
        print("[TRAY] Quit requested")
        icon.stop()
        os._exit(0)

    # ---- menu builders --------------------------------------------------

    def make_hotkey_item(display, evt):
        def _action(icon, item):
            on_set_hotkey(icon, evt)

        def _checked(item):
            return evt == worker.HOTKEY

        return pystray.MenuItem(
            display,
            _action,
            radio=True,
            checked=_checked,
        )

    modifier_items = [make_hotkey_item(d, e) for d, e in HOTKEY_CHOICES[:6]]
    lock_items = [make_hotkey_item(d, e) for d, e in HOTKEY_CHOICES[6:9]]
    fkey_items = [make_hotkey_item(d, e) for d, e in HOTKEY_CHOICES[9:]]

    hotkey_submenu = pystray.Menu(
        *modifier_items,
        pystray.Menu.SEPARATOR,
        *lock_items,
        pystray.Menu.SEPARATOR,
        *fkey_items,
    )

    def model_status_label(item):
        mid = _active_model_id()
        name = MODELS.get(mid, {}).get("name", mid)
        mark = "✓" if is_installed(mid) else "⏳ не загружена"
        return f"Модель: {name} {mark}"

    model_submenu = pystray.Menu(
        pystray.MenuItem("Перекачать модель…", on_redownload_model),
        pystray.MenuItem("Удалить модель…", on_delete_model),
    )

    menu = pystray.Menu(
        pystray.MenuItem("Vox — Голосовой ввод", None, default=True, enabled=False),
        pystray.Menu.SEPARATOR,
        pystray.MenuItem(
            lambda item: f"Клавиша: {get_hotkey_display(worker.HOTKEY)}", None, enabled=False
        ),
        pystray.MenuItem("Сменить клавишу", hotkey_submenu),
        pystray.Menu.SEPARATOR,
        pystray.MenuItem(model_status_label, None, enabled=False),
        pystray.MenuItem("Модель распознавания", model_submenu),
        pystray.Menu.SEPARATOR,
        pystray.MenuItem("История…", on_history),
        pystray.MenuItem("Открыть логи", on_open_logs),
        pystray.MenuItem("Открыть папку с моделями", on_open_models),
        pystray.Menu.SEPARATOR,
        pystray.MenuItem("Выход", on_quit),
    )

    icon = pystray.Icon("Vox", _get_icon_image(), "Vox — Голосовой ввод", menu)
    icon.run()
