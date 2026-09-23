"""
Paths, config.json read/write, and the hotkey choices list.

Lives in %APPDATA%\\vox\\ for frozen builds, next to the script otherwise.
"""

from __future__ import annotations

import json
import os
import sys

# ---------------------------------------------------------------------------
# Defaults
# ---------------------------------------------------------------------------

DEFAULT_HOTKEY = "right ctrl"
SAMPLE_RATE = 16000
MIN_RECORD_MS = 250
ENABLE_BEEPS = True
AUTO_PASTE = True

# Read the text before the cursor (via Shift+Home → Ctrl+C → Right) to drive
# contextual capitalization and the leading separator space. Disabled by
# default: the clipboard-selection trick disturbs the cursor in browsers and
# many editors, causing dictations to land at the start of the line ("reversed
# text" bug). Re-enable only if cursor reading is made reliable. See D020.
CURSOR_CONTEXT_ENABLED = False

# Cursor-free separator strategy: always prepend exactly one space before each
# dictation so consecutive dictations never glue together ("Привет.Как дела").
# This replaces the cursor-context leading-space step (which is off by default,
# see CURSOR_CONTEXT_ENABLED / D020) with a simple unconditional rule. The only
# cost is a leading space on the very first dictation in an empty field, which
# is irrelevant for dictation. Reversible — set False to fall back to the
# cursor-context behaviour. See D029.
LEADING_SPACE_ALWAYS = True


# ---------------------------------------------------------------------------
# Hotkey catalog (display name, event.name as emitted by `keyboard` lib)
# ---------------------------------------------------------------------------

HOTKEY_CHOICES = [
    ("Правый Ctrl", "right ctrl"),
    ("Левый Ctrl", "ctrl"),
    ("Правый Shift", "right shift"),
    ("Левый Shift", "shift"),
    ("Правый Alt", "right alt"),
    ("Левый Alt", "alt"),
    ("Caps Lock", "caps lock"),
    ("Scroll Lock", "scroll lock"),
    ("Pause", "pause"),
    *[(f"F{i}", f"f{i}") for i in range(1, 13)],
]
VALID_HOTKEYS = {evt for _, evt in HOTKEY_CHOICES}


def get_hotkey_display(event_name: str) -> str:
    """Human-readable label for an event.name; falls back to upper()."""
    for display, evt in HOTKEY_CHOICES:
        if evt == event_name:
            return display
    return event_name.upper() if event_name else "<unknown>"


# ---------------------------------------------------------------------------
# Filesystem paths
# ---------------------------------------------------------------------------


def _appdata_root() -> str:
    """Per-user data root (frozen: %APPDATA%\\vox, otherwise: project dir)."""
    if getattr(sys, "frozen", False):
        base = os.environ.get("APPDATA", os.path.expanduser("~"))
        return os.path.join(base, "vox")
    # Dev mode: keep state right next to the source for easy inspection.
    return os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def get_models_dir() -> str:
    return os.path.join(_appdata_root(), "models")


def get_log_dir() -> str:
    return os.path.join(_appdata_root(), "logs")


def get_db_path() -> str:
    return os.path.join(_appdata_root(), "vox_data.db")


def get_config_path() -> str:
    return os.path.join(_appdata_root(), "config.json")


# ---------------------------------------------------------------------------
# Config I/O
# ---------------------------------------------------------------------------


def load_config() -> dict:
    """Read config from disk; return {} on missing/corrupt file."""
    path = get_config_path()
    try:
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
        if isinstance(data, dict):
            return data
    except (FileNotFoundError, json.JSONDecodeError, OSError):
        pass
    return {}


def save_config(config: dict) -> None:
    """Atomically write config to disk."""
    path = get_config_path()
    os.makedirs(os.path.dirname(path), exist_ok=True)
    tmp_path = path + ".tmp"
    with open(tmp_path, "w", encoding="utf-8") as f:
        json.dump(config, f, indent=2, ensure_ascii=False)
    os.replace(tmp_path, path)


def update_config(**changes) -> dict:
    """Read-modify-write helper. Returns the updated config dict."""
    cfg = load_config()
    cfg.update(changes)
    save_config(cfg)
    return cfg


# ---------------------------------------------------------------------------
# Resolved settings used by the worker on startup
# ---------------------------------------------------------------------------


def initial_hotkey() -> str:
    raw = load_config().get("hotkey", DEFAULT_HOTKEY)
    if isinstance(raw, str) and raw in VALID_HOTKEYS:
        return raw
    return DEFAULT_HOTKEY
