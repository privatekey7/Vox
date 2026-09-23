"""
SQLite storage for Vox — single-file database for transcription history,
user dictionary, and settings.

Database file: ``vox_data.db`` in the app data directory.
Uses FTS5 for full-text search over transcription history.

Usage:
    from voxapp.storage import VoxDB

    db = VoxDB()                       # opens / creates vox_data.db
    db.add_transcription("привет мир", model="parakeet-tdt-0.6b-v3", lang="multi")
    results = db.search("привет")      # FTS5 search
    history = db.get_history(limit=50)  # recent transcriptions
    db.close()
"""

from __future__ import annotations

import os
import sqlite3
import time
from dataclasses import dataclass
from typing import Any

from voxapp.config import _appdata_root

# ---------------------------------------------------------------------------
# Data classes
# ---------------------------------------------------------------------------

DB_VERSION = 1


@dataclass
class Transcription:
    """A single transcription record."""

    id: int
    raw_text: str
    processed_text: str
    model: str
    lang: str
    duration_ms: int
    app_context: str
    created_at: float  # Unix timestamp


@dataclass
class DictEntry:
    """A user dictionary entry (auto-learned or manual)."""

    id: int
    wrong: str
    correct: str
    lang: str
    source: str  # "auto" | "manual"
    created_at: float


# ---------------------------------------------------------------------------
# Database
# ---------------------------------------------------------------------------

_SCHEMA_SQL = """\
-- Core transcription history
CREATE TABLE IF NOT EXISTS transcriptions (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    raw_text        TEXT    NOT NULL,
    processed_text  TEXT    NOT NULL,
    model           TEXT    NOT NULL DEFAULT '',
    lang            TEXT    NOT NULL DEFAULT '',
    duration_ms     INTEGER NOT NULL DEFAULT 0,
    app_context     TEXT    NOT NULL DEFAULT '',
    created_at      REAL    NOT NULL
);

-- FTS5 virtual table for full-text search
CREATE VIRTUAL TABLE IF NOT EXISTS transcriptions_fts USING fts5(
    processed_text,
    content='transcriptions',
    content_rowid='id',
    tokenize='unicode61'
);

-- Triggers to keep FTS in sync
CREATE TRIGGER IF NOT EXISTS transcriptions_ai AFTER INSERT ON transcriptions BEGIN
    INSERT INTO transcriptions_fts(rowid, processed_text)
    VALUES (new.id, new.processed_text);
END;

CREATE TRIGGER IF NOT EXISTS transcriptions_ad AFTER DELETE ON transcriptions BEGIN
    INSERT INTO transcriptions_fts(transcriptions_fts, rowid, processed_text)
    VALUES ('delete', old.id, old.processed_text);
END;

CREATE TRIGGER IF NOT EXISTS transcriptions_au AFTER UPDATE ON transcriptions BEGIN
    INSERT INTO transcriptions_fts(transcriptions_fts, rowid, processed_text)
    VALUES ('delete', old.id, old.processed_text);
    INSERT INTO transcriptions_fts(rowid, processed_text)
    VALUES (new.id, new.processed_text);
END;

-- User dictionary for corrections
CREATE TABLE IF NOT EXISTS dictionary (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    wrong      TEXT    NOT NULL,
    correct    TEXT    NOT NULL,
    lang       TEXT    NOT NULL DEFAULT '',
    source     TEXT    NOT NULL DEFAULT 'manual',
    created_at REAL    NOT NULL,
    UNIQUE(wrong, lang)
);

-- App settings (key-value)
CREATE TABLE IF NOT EXISTS settings (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

-- Indexes
CREATE INDEX IF NOT EXISTS idx_transcriptions_created
    ON transcriptions(created_at DESC);
CREATE INDEX IF NOT EXISTS idx_transcriptions_model
    ON transcriptions(model);
CREATE INDEX IF NOT EXISTS idx_dictionary_wrong
    ON dictionary(wrong);
"""


class VoxDB:
    """
    Lightweight SQLite wrapper for Vox data.

    Thread-safety: each thread should use its own ``VoxDB`` instance,
    or pass ``check_same_thread=False`` to share one.
    """

    def __init__(self, db_path: str | None = None, *, check_same_thread: bool = True) -> None:
        if db_path is None:
            db_path = os.path.join(_appdata_root(), "vox_data.db")
        os.makedirs(os.path.dirname(db_path) or ".", exist_ok=True)

        self._path = db_path
        self._conn = sqlite3.connect(
            db_path,
            check_same_thread=check_same_thread,
        )
        self._conn.row_factory = sqlite3.Row
        self._conn.execute("PRAGMA journal_mode=WAL")
        self._conn.execute("PRAGMA foreign_keys=ON")
        self._init_schema()

    def _init_schema(self) -> None:
        """Create tables if they don't exist."""
        self._conn.executescript(_SCHEMA_SQL)
        # Store schema version
        self._conn.execute(
            "INSERT OR IGNORE INTO settings(key, value) VALUES (?, ?)",
            ("db_version", str(DB_VERSION)),
        )
        self._conn.commit()

    # -- Transcriptions ---------------------------------------------------

    def add_transcription(
        self,
        raw_text: str,
        processed_text: str | None = None,
        *,
        model: str = "",
        lang: str = "",
        duration_ms: int = 0,
        app_context: str = "",
    ) -> int:
        """
        Insert a transcription record. Returns the new row ID.

        If ``processed_text`` is None, it defaults to ``raw_text``.
        """
        if processed_text is None:
            processed_text = raw_text

        cur = self._conn.execute(
            """INSERT INTO transcriptions
               (raw_text, processed_text, model, lang, duration_ms, app_context, created_at)
               VALUES (?, ?, ?, ?, ?, ?, ?)""",
            (raw_text, processed_text, model, lang, duration_ms, app_context, time.time()),
        )
        self._conn.commit()
        return cur.lastrowid  # type: ignore[return-value]

    def get_history(
        self,
        limit: int = 50,
        offset: int = 0,
        model: str | None = None,
        lang: str | None = None,
    ) -> list[Transcription]:
        """Return recent transcriptions, newest first."""
        clauses: list[str] = []
        params: list[Any] = []

        if model is not None:
            clauses.append("model = ?")
            params.append(model)
        if lang is not None:
            clauses.append("lang = ?")
            params.append(lang)

        where = (" WHERE " + " AND ".join(clauses)) if clauses else ""
        sql = f"SELECT * FROM transcriptions{where} ORDER BY created_at DESC LIMIT ? OFFSET ?"
        params.extend([limit, offset])

        rows = self._conn.execute(sql, params).fetchall()
        return [
            Transcription(
                id=r["id"],
                raw_text=r["raw_text"],
                processed_text=r["processed_text"],
                model=r["model"],
                lang=r["lang"],
                duration_ms=r["duration_ms"],
                app_context=r["app_context"],
                created_at=r["created_at"],
            )
            for r in rows
        ]

    def search(self, query: str, limit: int = 50) -> list[Transcription]:
        """
        Full-text search over processed transcription text using FTS5.

        The query supports FTS5 syntax (e.g. ``"hello world"``, ``hello OR world``).
        """
        sql = """
            SELECT t.*
            FROM transcriptions_fts fts
            JOIN transcriptions t ON t.id = fts.rowid
            WHERE transcriptions_fts MATCH ?
            ORDER BY rank
            LIMIT ?
        """
        rows = self._conn.execute(sql, (query, limit)).fetchall()
        return [
            Transcription(
                id=r["id"],
                raw_text=r["raw_text"],
                processed_text=r["processed_text"],
                model=r["model"],
                lang=r["lang"],
                duration_ms=r["duration_ms"],
                app_context=r["app_context"],
                created_at=r["created_at"],
            )
            for r in rows
        ]

    def delete_transcription(self, transcription_id: int) -> bool:
        """Delete a transcription by ID. Returns True if a row was deleted."""
        cur = self._conn.execute("DELETE FROM transcriptions WHERE id = ?", (transcription_id,))
        self._conn.commit()
        return cur.rowcount > 0

    def clear_history(self) -> int:
        """Delete all transcriptions. Returns the number of rows removed.

        The dictionary, settings and other tables are left untouched. The
        per-row ``AFTER DELETE`` trigger keeps the FTS index consistent.
        """
        cur = self._conn.execute("DELETE FROM transcriptions")
        self._conn.commit()
        return cur.rowcount

    def count_transcriptions(self) -> int:
        """Return total number of transcriptions."""
        row = self._conn.execute("SELECT COUNT(*) AS cnt FROM transcriptions").fetchone()
        return int(row["cnt"])  # type: ignore[index]

    # -- Dictionary -------------------------------------------------------

    def add_dict_entry(
        self, wrong: str, correct: str, *, lang: str = "", source: str = "manual"
    ) -> int:
        """
        Add or update a dictionary correction. Returns the row ID.

        If ``wrong`` already exists for this ``lang``, the entry is updated.
        """
        cur = self._conn.execute(
            """INSERT INTO dictionary (wrong, correct, lang, source, created_at)
               VALUES (?, ?, ?, ?, ?)
               ON CONFLICT(wrong, lang) DO UPDATE SET
                   correct = excluded.correct,
                   source  = excluded.source""",
            (wrong, correct, lang, source, time.time()),
        )
        self._conn.commit()
        return cur.lastrowid  # type: ignore[return-value]

    def get_dict_entries(self, lang: str | None = None) -> list[DictEntry]:
        """Return all dictionary entries, optionally filtered by language."""
        if lang is not None:
            rows = self._conn.execute(
                "SELECT * FROM dictionary WHERE lang = ? ORDER BY wrong", (lang,)
            ).fetchall()
        else:
            rows = self._conn.execute("SELECT * FROM dictionary ORDER BY wrong").fetchall()
        return [
            DictEntry(
                id=r["id"],
                wrong=r["wrong"],
                correct=r["correct"],
                lang=r["lang"],
                source=r["source"],
                created_at=r["created_at"],
            )
            for r in rows
        ]

    def lookup_correction(self, wrong: str, lang: str = "") -> str | None:
        """Look up a correction for a misspelled word. Returns None if not found."""
        row = self._conn.execute(
            "SELECT correct FROM dictionary WHERE wrong = ? AND lang = ?",
            (wrong, lang),
        ).fetchone()
        return row["correct"] if row else None

    def delete_dict_entry(self, entry_id: int) -> bool:
        """Delete a dictionary entry by ID. Returns True if a row was deleted."""
        cur = self._conn.execute("DELETE FROM dictionary WHERE id = ?", (entry_id,))
        self._conn.commit()
        return cur.rowcount > 0

    # -- Settings ---------------------------------------------------------

    def get_setting(self, key: str, default: str | None = None) -> str | None:
        """Read a setting value."""
        row = self._conn.execute("SELECT value FROM settings WHERE key = ?", (key,)).fetchone()
        return row["value"] if row else default

    def set_setting(self, key: str, value: str) -> None:
        """Write a setting value (upsert)."""
        self._conn.execute(
            "INSERT OR REPLACE INTO settings(key, value) VALUES (?, ?)",
            (key, value),
        )
        self._conn.commit()

    # -- Stats ------------------------------------------------------------

    def get_stats(self) -> dict[str, Any]:
        """Return usage statistics."""
        total = self.count_transcriptions()
        models = self._conn.execute(
            "SELECT model, COUNT(*) AS cnt FROM transcriptions GROUP BY model ORDER BY cnt DESC"
        ).fetchall()
        dict_count = self._conn.execute("SELECT COUNT(*) AS cnt FROM dictionary").fetchone()

        return {
            "total_transcriptions": total,
            "models_usage": {r["model"]: r["cnt"] for r in models},
            "dictionary_entries": dict_count["cnt"] if dict_count else 0,
        }

    # -- Lifecycle --------------------------------------------------------

    @property
    def path(self) -> str:
        return self._path

    def close(self) -> None:
        """Close the database connection."""
        self._conn.close()

    def __enter__(self) -> VoxDB:
        return self

    def __exit__(self, *exc: object) -> None:
        self.close()
