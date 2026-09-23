"""
Tkinter UI for Vox:
  - First-run wizard (download the single bundled model).
  - Model manager (download / delete / reinstall).
  - History window (browse / search transcription history).

Since D028 Vox ships a single STT model (Parakeet TDT v3), so the
first-run wizard is one step: it just downloads that model. There is no
language or model choice to make.
"""

from __future__ import annotations

import contextlib
import threading
import time
import tkinter as tk
from collections.abc import Callable
from tkinter import messagebox, ttk
from typing import TYPE_CHECKING, Any

from voxapp.downloader import download_model, uninstall_model
from voxapp.registry import (
    MODELS,
    is_installed,
)

# D028: Vox ships a single STT model; the wizard always downloads this one.
WIZARD_MODEL_ID = "parakeet-tdt-0.6b-v3"

if TYPE_CHECKING:
    from voxapp.storage import VoxDB

# ---------------------------------------------------------------------------
# Shared helpers
# ---------------------------------------------------------------------------


def _human_size(mb: int) -> str:
    if mb >= 1024:
        return f"{mb / 1024:.1f} ГБ"
    return f"{mb} МБ"


class _ProgressDialog:
    """Modal-ish progress window with status text + progressbar."""

    def __init__(self, parent: tk.Tk | tk.Toplevel, title: str):
        self.top = tk.Toplevel(parent)
        self.top.title(title)
        self.top.geometry("420x140")
        self.top.resizable(False, False)
        self.top.transient(parent)
        self.top.grab_set()

        self.label_var = tk.StringVar(value="Подготовка...")
        tk.Label(
            self.top, textvariable=self.label_var, anchor="w", justify="left", wraplength=400
        ).pack(padx=16, pady=(16, 8), fill="x")

        self.pb = ttk.Progressbar(self.top, mode="determinate", length=380, maximum=1000)
        self.pb.pack(padx=16, pady=4)

        self.detail_var = tk.StringVar(value="")
        tk.Label(self.top, textvariable=self.detail_var, anchor="w", fg="#666").pack(
            padx=16, pady=(4, 12), fill="x"
        )

    def update_progress(self, done: int, total: int, stage: str) -> None:
        def _apply():
            if total > 0:
                pct = int(done * 1000 / total)
                self.pb["value"] = pct
                if stage == "Загрузка":
                    self.detail_var.set(
                        f"{stage}: {done / 1024 / 1024:.1f} / "
                        f"{total / 1024 / 1024:.1f} МБ ({pct / 10:.0f}%)"
                    )
                else:
                    self.detail_var.set(f"{stage}: {done} / {total}")
            else:
                self.pb["value"] = 0
                self.detail_var.set(f"{stage}: {done / 1024 / 1024:.1f} МБ")
            self.label_var.set(stage + "...")

        with contextlib.suppress(tk.TclError):
            self.top.after(0, _apply)

    def close(self) -> None:
        try:
            self.top.grab_release()
            self.top.destroy()
        except tk.TclError:
            pass


def _enable_mousewheel_scroll(canvas: tk.Canvas) -> None:
    """Enable mouse-wheel scrolling on a tkinter Canvas (Windows + Linux)."""

    def _on_mousewheel(event: tk.Event) -> None:  # type: ignore[type-arg]
        # Windows: event.delta is ±120 per notch
        # macOS:   event.delta is ±1
        # Linux:   handled via Button-4 / Button-5 below
        if event.delta:
            canvas.yview_scroll(int(-1 * (event.delta / 120)), "units")

    def _on_linux_scroll_up(event: tk.Event) -> None:  # type: ignore[type-arg]
        canvas.yview_scroll(-3, "units")

    def _on_linux_scroll_down(event: tk.Event) -> None:  # type: ignore[type-arg]
        canvas.yview_scroll(3, "units")

    def _bind_wheel(event: tk.Event) -> None:  # type: ignore[type-arg]
        canvas.bind_all("<MouseWheel>", _on_mousewheel)
        canvas.bind_all("<Button-4>", _on_linux_scroll_up)
        canvas.bind_all("<Button-5>", _on_linux_scroll_down)

    def _unbind_wheel(event: tk.Event) -> None:  # type: ignore[type-arg]
        canvas.unbind_all("<MouseWheel>")
        canvas.unbind_all("<Button-4>")
        canvas.unbind_all("<Button-5>")

    canvas.bind("<Enter>", _bind_wheel)
    canvas.bind("<Leave>", _unbind_wheel)


def _set_icon(window: tk.Tk) -> None:
    """Set the Vox icon on a Tk window (best-effort)."""
    try:
        import os
        import sys

        icon_dir = (
            sys._MEIPASS  # type: ignore[attr-defined]
            if getattr(sys, "frozen", False)
            else os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
        )
        window.iconbitmap(os.path.join(icon_dir, "vox.ico"))
    except Exception:
        pass


# ═══════════════════════════════════════════════════════════════════════════
# First-run wizard (2-step: language → model)
# ═══════════════════════════════════════════════════════════════════════════


class FirstRunWizard:
    """
    Single-step first-run wizard (D028: Parakeet-only).

    Shows a welcome screen and downloads the single bundled model
    (Parakeet TDT v3). There is no language or model choice — Vox ships
    one model.

    ``on_apply`` receives ``(model_id, lang)`` on successful completion.
    """

    MODEL_ID = WIZARD_MODEL_ID
    LANG = "multi"

    def __init__(self, on_apply: Callable[[str, str], None]):
        self.on_apply = on_apply
        self._cancelled = True

        self.root = tk.Tk()
        self.root.title("Vox — Первый запуск")
        self.root.geometry("560x420")
        self.root.resizable(False, False)
        _set_icon(self.root)

        self._container = tk.Frame(self.root)
        self._container.pack(fill="both", expand=True)
        self._build_ui()

    def _build_ui(self) -> None:
        """Render the single welcome + download page."""
        for w in self._container.winfo_children():
            w.destroy()

        frame = tk.Frame(self._container)
        frame.pack(fill="both", expand=True, padx=32, pady=28)

        info = MODELS.get(self.MODEL_ID, {})
        name = info.get("name", self.MODEL_ID)
        size = _human_size(info.get("size_mb", 0))

        tk.Label(
            frame,
            text="Добро пожаловать в Vox!",
            font=("Segoe UI", 14, "bold"),
        ).pack(anchor="w")
        tk.Label(
            frame,
            text=(
                "Vox — голосовой ввод, который работает полностью офлайн.\n"
                "Чтобы распознавать речь, нужно один раз скачать модель."
            ),
            wraplength=480,
            justify="left",
        ).pack(anchor="w", pady=(8, 16))

        box = tk.Frame(frame, bd=1, relief="solid", padx=12, pady=10)
        box.pack(fill="x")
        tk.Label(box, text=name, font=("Segoe UI", 11, "bold")).pack(anchor="w")
        tk.Label(box, text=f"Размер: {size}", fg="#666", anchor="w").pack(fill="x", pady=(2, 0))
        desc = info.get("description", "")
        if desc:
            tk.Label(box, text=desc, wraplength=460, anchor="w", justify="left").pack(
                fill="x", pady=(6, 0)
            )

        # Spacer
        tk.Frame(frame).pack(expand=True)

        # Buttons
        btn_row = tk.Frame(frame)
        btn_row.pack(fill="x", pady=(16, 0))

        tk.Button(
            btn_row,
            text="Отмена",
            width=12,
            command=self._on_cancel,
        ).pack(side="right", padx=(8, 0))

        tk.Button(
            btn_row,
            text="Скачать и начать",
            width=22,
            command=self._on_confirm,
            default="active",
        ).pack(side="right")

    # ── Actions ───────────────────────────────────────────────────────

    def _on_confirm(self) -> None:
        # Guard against double-clicks (fixes name+URL printing twice)
        if getattr(self, "_downloading", False):
            return

        mid = self.MODEL_ID
        model_name = MODELS.get(mid, {}).get("name", mid)

        if is_installed(mid):
            # Already installed — just apply
            self._apply_and_close(mid)
            return

        # Download, then apply
        self._downloading = True
        dlg = _ProgressDialog(self.root, f"Загрузка: {model_name}")
        result_holder: dict = {}

        def worker():
            try:
                download_model(mid, on_progress=dlg.update_progress, on_log=lambda m: print(m))
                result_holder["ok"] = True
            except Exception as e:
                result_holder["error"] = str(e)
                print(f"[DL] Failed: {e}")
                import traceback

                traceback.print_exc()
            finally:
                self.root.after(0, _finish)

        def _finish():
            dlg.close()
            self._downloading = False
            if "error" in result_holder:
                messagebox.showerror(
                    "Ошибка загрузки",
                    f"Не удалось скачать модель:\n\n{result_holder['error']}",
                )
                return
            self._apply_and_close(mid)

        threading.Thread(target=worker, daemon=True, name="VoxModelDownload").start()

    def _apply_and_close(self, model_id: str) -> None:
        try:
            self.on_apply(model_id, self.LANG)
            self._cancelled = False
            self.root.destroy()
        except Exception as e:
            messagebox.showerror(
                "Не удалось активировать",
                f"Скачивание прошло, но запустить не получилось:\n\n{e}",
            )

    def _on_cancel(self) -> None:
        if messagebox.askyesno(
            "Выйти из Vox?", "Без модели Vox не сможет распознавать речь.\nЗакрыть программу?"
        ):
            self.root.destroy()

    def run(self) -> bool:
        """Block until window closed. Returns True if a model was applied."""
        self.root.mainloop()
        return not self._cancelled


# ═══════════════════════════════════════════════════════════════════════════
# History Window (transcription history from tray)
# ═══════════════════════════════════════════════════════════════════════════


class HistoryWindow:
    """
    Browse and search transcription history stored in SQLite.

    Opens from the tray menu. Shows recent transcriptions with search,
    copy-to-clipboard, and delete functionality.
    """

    PAGE_SIZE = 50

    def __init__(self, db: VoxDB) -> None:
        self._db = db

        self.root = tk.Tk()
        self.root.title("Vox — История")
        self.root.geometry("720x540")
        self.root.resizable(True, True)
        _set_icon(self.root)

        self._search_debounce_id: str | None = None
        self._build_ui()
        self._load_history()

    def _build_ui(self) -> None:
        # Top: stats + search
        top = tk.Frame(self.root)
        top.pack(fill="x", padx=16, pady=(14, 8))

        tk.Label(top, text="История диктовок", font=("Segoe UI", 13, "bold")).pack(side="left")

        self._stats_var = tk.StringVar(value="")
        tk.Label(top, textvariable=self._stats_var, fg="#888").pack(side="left", padx=(12, 0))

        # Search bar
        search_row = tk.Frame(self.root)
        search_row.pack(fill="x", padx=16, pady=(0, 8))

        tk.Label(search_row, text="🔍").pack(side="left")
        self._search_var = tk.StringVar()
        self._search_var.trace_add("write", self._on_search_changed)
        search_entry = tk.Entry(search_row, textvariable=self._search_var, width=40)
        search_entry.pack(side="left", padx=(4, 8), fill="x", expand=True)

        # Clears only the search field — disabled while the field is empty so
        # it never looks like a dead button.
        self._clear_search_btn = tk.Button(
            search_row, text="Очистить поиск", command=self._clear_search, state="disabled"
        )
        self._clear_search_btn.pack(side="right")

        # Scrollable history list
        outer = tk.Frame(self.root)
        outer.pack(fill="both", expand=True, padx=16, pady=(0, 8))

        canvas = tk.Canvas(outer, borderwidth=0, highlightthickness=0)
        scrollbar = tk.Scrollbar(outer, orient="vertical", command=canvas.yview)
        self._list_frame = tk.Frame(canvas)
        self._list_frame.bind(
            "<Configure>", lambda e: canvas.configure(scrollregion=canvas.bbox("all"))
        )
        self._canvas_window = canvas.create_window(
            (0, 0), window=self._list_frame, anchor="nw", width=670
        )
        canvas.configure(yscrollcommand=scrollbar.set)
        canvas.pack(side="left", fill="both", expand=True)
        scrollbar.pack(side="right", fill="y")
        _enable_mousewheel_scroll(canvas)
        self._canvas = canvas

        # Resize inner frame width with canvas
        canvas.bind("<Configure>", self._on_canvas_resize)

        # Bottom buttons
        btn_row = tk.Frame(self.root)
        btn_row.pack(fill="x", padx=16, pady=(4, 14))
        tk.Button(btn_row, text="Закрыть", width=12, command=self._on_close).pack(side="right")
        self._clear_history_btn = tk.Button(
            btn_row, text="Очистить историю", command=self._clear_history
        )
        self._clear_history_btn.pack(side="left")

    def _on_canvas_resize(self, event: Any) -> None:
        self._canvas.itemconfig(self._canvas_window, width=event.width - 4)

    def _load_history(self, query: str | None = None) -> None:
        """Load transcriptions from the DB and display them."""
        for w in self._list_frame.winfo_children():
            w.destroy()

        try:
            if query:
                rows = self._db.search(query, limit=self.PAGE_SIZE)
            else:
                rows = self._db.get_history(limit=self.PAGE_SIZE)
        except Exception as e:
            tk.Label(self._list_frame, text=f"Ошибка загрузки: {e}", fg="#c00").pack(
                padx=8, pady=8
            )
            return

        # Update stats
        try:
            stats = self._db.get_stats()
            total = stats["total_transcriptions"]
            dict_n = stats["dictionary_entries"]
            self._stats_var.set(f"Всего: {total} · Словарь: {dict_n}")
        except Exception:
            pass

        if not rows:
            msg = "Ничего не найдено." if query else "История пуста."
            tk.Label(self._list_frame, text=msg, fg="#888").pack(padx=8, pady=20)
            return

        for row in rows:
            self._render_history_row(row)

    def _render_history_row(self, row: Any) -> None:
        """Render a single transcription row."""
        fr = tk.Frame(self._list_frame, bd=1, relief="solid", padx=10, pady=6)
        fr.pack(fill="x", pady=2)

        # Top line: timestamp + model + duration
        header = tk.Frame(fr)
        header.pack(fill="x")

        ts_str = time.strftime("%d.%m.%Y %H:%M", time.localtime(row.created_at))
        tk.Label(header, text=ts_str, fg="#888", font=("Segoe UI", 8)).pack(side="left")

        if row.model:
            model_name = MODELS.get(row.model, {}).get("name", row.model)
            tk.Label(header, text=f"  · {model_name}", fg="#aaa", font=("Segoe UI", 8)).pack(
                side="left"
            )

        if row.duration_ms > 0:
            dur = row.duration_ms / 1000
            tk.Label(header, text=f"  · {dur:.1f}с", fg="#aaa", font=("Segoe UI", 8)).pack(
                side="left"
            )

        # Action buttons (right side of header)
        tk.Button(
            header,
            text="✕",
            width=2,
            relief="flat",
            fg="#999",
            command=lambda rid=row.id: self._on_delete(rid),  # type: ignore[misc]
        ).pack(side="right")

        copy_btn = tk.Button(header, text="Копировать", relief="flat", fg="#06c")
        copy_btn.configure(
            command=lambda t=row.processed_text, b=copy_btn: self._on_copy(t, b)  # type: ignore[misc]
        )
        copy_btn.pack(side="right", padx=(0, 2))

        # Text content
        tk.Label(
            fr,
            text=row.processed_text,
            anchor="w",
            justify="left",
            wraplength=620,
        ).pack(fill="x", pady=(2, 0))

        # Show raw text if different
        if row.raw_text != row.processed_text:
            tk.Label(
                fr,
                text=f"raw: {row.raw_text}",
                anchor="w",
                justify="left",
                wraplength=620,
                fg="#aaa",
                font=("Segoe UI", 8),
            ).pack(fill="x")

    def _on_copy(self, text: str, button: tk.Button | None = None) -> None:
        """Copy transcription text to clipboard with visible feedback."""
        try:
            import pyperclip

            pyperclip.copy(text)
            ok = True
        except Exception:
            ok = False

        if button is None:
            return

        # Briefly show the result on the button, then restore it.
        button.configure(
            text="✓ Скопировано" if ok else "✗ Ошибка",
            fg="#0a7" if ok else "#c00",
            state="disabled",
        )
        button.after(
            1500,
            lambda: button.configure(text="Копировать", fg="#06c", state="normal"),
        )

    def _on_delete(self, row_id: int) -> None:
        """Delete a transcription and refresh the list."""
        self._db.delete_transcription(row_id)
        query = self._search_var.get().strip() or None
        self._load_history(query=query)

    def _on_search_changed(self, *_args: object) -> None:
        """Debounced search — waits 400ms after typing stops."""
        # Enable the clear-search button only when there is text to clear.
        has_text = bool(self._search_var.get())
        self._clear_search_btn.configure(state="normal" if has_text else "disabled")
        if self._search_debounce_id is not None:
            self.root.after_cancel(self._search_debounce_id)
        self._search_debounce_id = self.root.after(400, self._do_search)

    def _do_search(self) -> None:
        query = self._search_var.get().strip()
        self._load_history(query=query if query else None)

    def _clear_search(self) -> None:
        """Clear the search field only (not the history)."""
        self._search_var.set("")
        self._load_history()

    def _clear_history(self) -> None:
        """Delete the entire dictation history after confirmation."""
        from tkinter import messagebox

        total = self._db.count_transcriptions()
        if total == 0:
            messagebox.showinfo("Vox", "История уже пуста.", parent=self.root)
            return

        if not messagebox.askyesno(
            "Очистить историю",
            f"Удалить все диктовки ({total})?\nЭто действие нельзя отменить.",
            parent=self.root,
            icon="warning",
        ):
            return

        self._db.clear_history()
        self._search_var.set("")
        self._load_history()

    def _on_close(self) -> None:
        self.root.destroy()

    def run(self) -> None:
        """Block until window closed."""
        self.root.mainloop()


# ---------------------------------------------------------------------------
# Convenience wrappers
# ---------------------------------------------------------------------------


def run_first_run_wizard(on_apply: Callable[[str, str], None]) -> bool:
    """
    Show the 2-step first-run wizard (language → model).

    ``on_apply`` receives ``(model_id, lang)`` on success.
    Returns True if user picked & confirmed a model.
    """
    wiz = FirstRunWizard(on_apply=on_apply)
    return wiz.run()


def run_history(db: VoxDB) -> None:
    """Show the history window (blocks until closed)."""
    win = HistoryWindow(db=db)
    win.run()


# ═══════════════════════════════════════════════════════════════════════════
# Model (re)download dialog — opened from the tray to repair the single model
# ═══════════════════════════════════════════════════════════════════════════


class ModelDownloadWindow:
    """
    Standalone window that downloads (or re-downloads) the speech model.

    Vox ships with a single mandatory model (D028: Parakeet-only), so there is
    no catalog — just a small progress window used to repair a corrupt install
    or fetch the model again after it was deleted.
    """

    def __init__(
        self,
        model_id: str,
        *,
        force: bool = False,
        on_done: Callable[[bool], None] | None = None,
    ) -> None:
        self._model_id = model_id
        self._force = force
        self.on_done = on_done
        self._success = False

        model_name = MODELS.get(model_id, {}).get("name", model_id)

        self.root = tk.Tk()
        self.root.title("Vox — модель распознавания")
        self.root.geometry("480x190")
        self.root.resizable(False, False)
        _set_icon(self.root)

        action = "Перекачать" if force else "Скачать"
        tk.Label(
            self.root,
            text=f"{action} модель «{model_name}»?",
            wraplength=440,
            justify="left",
            anchor="w",
        ).pack(padx=20, pady=(20, 8), fill="x")

        self._status_var = tk.StringVar(value="")
        tk.Label(self.root, textvariable=self._status_var, anchor="w", fg="#666").pack(
            padx=20, fill="x"
        )
        self._pb = ttk.Progressbar(self.root, mode="determinate", length=430, maximum=1000)
        self._pb.pack(padx=20, pady=(4, 0), fill="x")
        self._pb.pack_forget()

        btn_frame = tk.Frame(self.root)
        btn_frame.pack(padx=20, pady=(16, 20), fill="x")
        self._btn_go = tk.Button(btn_frame, text=action, width=14, command=self._start_download)
        self._btn_go.pack(side="right", padx=(8, 0))
        self._btn_cancel = tk.Button(btn_frame, text="Отмена", width=10, command=self._on_cancel)
        self._btn_cancel.pack(side="right")
        self.root.protocol("WM_DELETE_WINDOW", self._on_cancel)

    def _start_download(self) -> None:
        self._btn_go.config(state="disabled")
        self._pb.pack(padx=20, pady=(4, 0), fill="x", before=self.root.winfo_children()[-1])
        self._status_var.set("Начинаю загрузку...")

        def _worker() -> None:
            try:
                # Repair: drop the (possibly corrupt) install so download_model
                # actually re-fetches instead of returning the existing dir.
                if self._force:
                    uninstall_model(self._model_id)
                download_model(
                    self._model_id, on_progress=self._on_progress, on_log=lambda m: print(m)
                )
                self._success = True
                self.root.after(0, self._on_finish)
            except Exception:
                import traceback

                err = traceback.format_exc()
                self.root.after(0, lambda: self._on_error(err))

        threading.Thread(target=_worker, daemon=True, name="VoxModelDownload").start()

    def _on_progress(self, done: int, total: int, stage: str) -> None:
        def _apply() -> None:
            if total > 0:
                pct = int(done * 1000 / total)
                self._pb["value"] = pct
                self._status_var.set(
                    f"{stage}: {done / 1024 / 1024:.0f}/{total / 1024 / 1024:.0f} МБ"
                    f" ({pct // 10}%)"
                )
            else:
                self._status_var.set(f"{stage}: {done / 1024 / 1024:.0f} МБ")

        with contextlib.suppress(tk.TclError):
            self.root.after(0, _apply)

    def _on_finish(self) -> None:
        if self.on_done:
            self.on_done(True)
        with contextlib.suppress(tk.TclError):
            self.root.destroy()

    def _on_error(self, error: str) -> None:
        messagebox.showerror("Ошибка загрузки", f"Не удалось скачать модель:\n\n{error}")
        if self.on_done:
            self.on_done(False)
        with contextlib.suppress(tk.TclError):
            self.root.destroy()

    def _on_cancel(self) -> None:
        if self.on_done:
            self.on_done(self._success)
        with contextlib.suppress(tk.TclError):
            self.root.destroy()

    def run(self) -> bool:
        self.root.mainloop()
        return self._success


def run_model_download(
    model_id: str,
    *,
    force: bool = False,
    on_done: Callable[[bool], None] | None = None,
) -> bool:
    """Show the model (re)download window. Returns True on success."""
    win = ModelDownloadWindow(model_id, force=force, on_done=on_done)
    return win.run()
