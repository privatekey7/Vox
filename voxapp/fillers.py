"""
Filler word removal for speech-to-text output.

Removes only *unconditional* hesitation sounds ("ну"-style discourse words are
intentionally NOT touched here). Pure hesitation noise ("ээ", "ммм", "um",
"uh", …) is always safe to strip and is cheap to remove deterministically.

Context-dependent fillers ("ну", "типа", "короче", "вот", "like", …) used to
live here too, but a blind regex cut words that were often meaningful
("вот этот файл", "значит равно"), so they are intentionally kept. See D021.

Usage:
    from voxapp.fillers import remove_fillers

    remove_fillers("ээ типа привет мир")  # "типа привет мир"  (ээ stripped)
    remove_fillers("um so hello world", lang="en")  # "so hello world"
"""

from __future__ import annotations

import re

# ---------------------------------------------------------------------------
# Filler word lists by language
# ---------------------------------------------------------------------------

# Words that are ALWAYS fillers (safe to remove unconditionally): pure
# hesitation sounds that never carry meaning.
_FILLERS: dict[str, list[str]] = {
    "ru": [
        "э-э",
        "э",
        "ээ",
        "эээ",
        "а-а",
        "м-м",
        "мм",
        "ммм",
        "хм",
        "хмм",
    ],
    "en": [
        "um",
        "umm",
        "uh",
        "uhh",
        "er",
        "err",
        "ah",
        "ahh",
        "hmm",
        "hm",
        "mm",
        "mmm",
    ],
}

# ---------------------------------------------------------------------------
# Pre-compiled patterns (built once at import time)
# ---------------------------------------------------------------------------

_ALWAYS_PATTERNS: dict[str, re.Pattern[str]] = {}


def _build_pattern(words: list[str]) -> re.Pattern[str]:
    """Build a regex that matches any of the given words/phrases as whole words."""
    # Sort by length (longest first) so multi-word phrases match before parts.
    sorted_words = sorted(words, key=len, reverse=True)
    alternatives = "|".join(re.escape(w) for w in sorted_words)
    # \b doesn't work well with Cyrillic hyphens, so we use lookaround.
    # Match word boundaries: start/end of string, spaces, or punctuation.
    pattern = rf"(?<![а-яёa-z])(?:{alternatives})(?![а-яёa-z])"
    return re.compile(pattern, re.IGNORECASE)


def _init_patterns() -> None:
    """Lazily compile regex patterns for all languages."""
    for lang, always in _FILLERS.items():
        if lang not in _ALWAYS_PATTERNS and always:
            _ALWAYS_PATTERNS[lang] = _build_pattern(always)


# ---------------------------------------------------------------------------
# Public API
# ---------------------------------------------------------------------------


def remove_fillers(text: str, lang: str = "ru") -> str:
    """Remove unconditional hesitation sounds from text.

    Args:
        text: Transcribed text (raw STT output or after term replacement).
        lang: Language code ("ru", "en"). Unknown languages return text as-is.
              Hesitation sounds for ALL known languages are stripped regardless
              of ``lang`` (speech naturally mixes "ну", "uh", etc.).

    Returns:
        Text with hesitation sounds removed and whitespace normalized.
        Context-dependent fillers are left untouched (see D021).
    """
    if not text or not text.strip():
        return text

    # Lazily compile patterns on first use.
    if not _ALWAYS_PATTERNS:
        _init_patterns()

    result = text
    had_fillers = False

    # Always remove unconditional hesitation sounds for ALL languages.
    # Speech naturally mixes fillers across languages ("ээ", "uh", etc.).
    for pattern in _ALWAYS_PATTERNS.values():
        new_result = pattern.sub("", result)
        if new_result != result:
            had_fillers = True
            result = new_result

    if not had_fillers:
        return result

    # Strip dangling punctuation left after filler removal.
    # e.g. "Э-э, запусти..." → ", запусти..." → "запусти..."
    result = re.sub(r"^[\s,;:]+", "", result)

    # Collapse adjacent orphaned commas/semicolons in the middle.
    # e.g. "Мы , , решили" → "Мы , решили"
    result = re.sub(r"[,;]\s*[,;]", ",", result)

    # Remove lone comma/semicolon between spaces (orphaned).
    # e.g. "Мы , решили" → "Мы решили"
    result = re.sub(r"\s[,;]\s", " ", result)

    # Remove space before sentence-ending punctuation.
    # e.g. "Мы это так ." → "Мы это так."
    result = re.sub(r"\s+([.!?…])", r"\1", result)

    # Normalize whitespace (collapse multiple spaces, strip edges).
    result = re.sub(r"\s{2,}", " ", result).strip()

    # If only punctuation remains (no word characters), return empty.
    # e.g. "Mmm." → "." → ""
    if result and not re.search(r"[а-яёa-z0-9]", result, re.IGNORECASE):
        return ""

    # Re-capitalize first letter if the original started with uppercase
    # and the result now starts with lowercase (filler was at the beginning).
    if (
        result
        and text
        and any(c.isupper() for c in text[:5] if c.isalpha())
        and result[0].isalpha()
        and result[0].islower()
    ):
        result = result[0].upper() + result[1:]

    return result
