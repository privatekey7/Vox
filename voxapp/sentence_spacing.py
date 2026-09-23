"""
Normalize spacing around punctuation *inside* a single dictation.

Models with built-in punctuation (``needs_punctuation=False`` in the
registry) emit sentence boundaries glued to the next word::

    "Привет.Что ты умеешь?Как у тебя дела?"   ← model output, unreadable

This module inserts the missing separator space and capitalizes the start
of each new sentence::

    "Привет. Что ты умеешь? Как у тебя дела?"  ← normalized

It is deliberately conservative to avoid corrupting non-sentence dots:

* A space is inserted after ``? ! …`` whenever a letter follows
  (these are unambiguous sentence ends), and the following letter is
  upper-cased.
* A space is inserted after ``.`` only when it is preceded by **two or
  more letters** — this protects single-letter abbreviations (``т.е.``,
  ``U.S.A.``) and decimals/versions (``3.14``, ``v2.0`` — a digit, not a
  letter, sits on one side). The following letter is upper-cased.
* A space is inserted after ``, ; :`` when a letter follows (case kept).
  Decimals (``3,14``) and times (``12:30``) are protected because a digit,
  not a letter, follows.

This is separate from :mod:`voxapp.spacing`, which decides the *leading*
space relative to the cursor context. Here we only fix spacing *within*
the dictation text itself, independent of any cursor context.
"""

from __future__ import annotations

import re

# Any Unicode letter, but not a digit or underscore (so decimals/versions,
# where a digit borders the punctuation, are never split).
_LETTER = r"[^\W\d_]"

# Sentence enders that are unambiguous (a question/exclamation always ends a
# sentence). A run like "?!" or "…" is treated as a single boundary.
_RE_STRONG_END = re.compile(rf"([?!…]+)({_LETTER})")

# A full stop only starts a new sentence when at least two letters precede it.
# This protects "т.е.", "U.S.A." (single-letter tokens) and "3.14"/"v2.0"
# (a digit borders the dot, so _LETTER does not match).
_RE_DOT_END = re.compile(rf"({_LETTER}{_LETTER}\.+)({_LETTER})")

# Mid-sentence separators: add a space but keep the following letter's case.
_RE_MIDDLE = re.compile(rf"([,;:])({_LETTER})")


def _space_and_capitalize(match: re.Match[str]) -> str:
    """Keep the punctuation, insert a space, upper-case the next letter."""
    return f"{match.group(1)} {match.group(2).upper()}"


def _space_only(match: re.Match[str]) -> str:
    """Keep the punctuation, insert a space, preserve the next letter."""
    return f"{match.group(1)} {match.group(2)}"


def normalize_sentence_spacing(text: str) -> str:
    """
    Insert missing spaces (and sentence-initial capitals) around glued
    punctuation produced by punctuation-aware STT models.

    Args:
        text: Raw text that may contain ``Привет.Что`` style glueing.

    Returns:
        Text with a single space after sentence/clause punctuation that was
        glued to the following word. Idempotent: already-spaced text is
        returned unchanged.
    """
    if not text:
        return text

    text = _RE_STRONG_END.sub(_space_and_capitalize, text)
    text = _RE_DOT_END.sub(_space_and_capitalize, text)
    text = _RE_MIDDLE.sub(_space_only, text)
    return text
