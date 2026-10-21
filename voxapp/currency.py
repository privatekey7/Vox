"""
Spell out the ruble sign in Russian text.

GigaAM v3 normalises money to "500 ₽". In dictated prose the word reads
better, so the sign becomes "рубль / рубля / рублей" agreeing with the number
right before it::

    "Переведи 500 ₽ на карту."  → "Переведи 500 рублей на карту."
    "21 ₽"                      → "21 рубль"
    "1,5 ₽"                     → "1,5 рубля"

Usage:
    from voxapp.currency import spell_out_currency
"""

from __future__ import annotations

import re

# A number (digits, optionally grouped by spaces or with a decimal part)
# followed by the sign, or the sign on its own.
_RUBLE_RE = re.compile(r"(?:(\d+(?:[  ]\d{3})*(?:[.,]\d+)?)[  ]?)?₽")


def _ruble_word(number: str | None) -> str:
    if number is None:
        return "рублях"  # "цена в ₽" → "цена в рублях"
    if "," in number or "." in number:
        return "рубля"  # fractional amounts take the genitive singular
    n = int(number.replace(" ", "").replace(" ", ""))
    if 11 <= n % 100 <= 14:
        return "рублей"
    if n % 10 == 1:
        return "рубль"
    if 2 <= n % 10 <= 4:
        return "рубля"
    return "рублей"


def _replace(match: re.Match[str]) -> str:
    number = match.group(1)
    word = _ruble_word(number)
    return f"{number} {word}" if number is not None else word


def spell_out_currency(text: str) -> str:
    """Replace every "₽" with the agreeing Russian word."""
    if "₽" not in text:
        return text
    return _RUBLE_RE.sub(_replace, text)
