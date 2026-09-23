"""
Inverse Text Normalization (ITN) — convert spoken number-words to digits.

Lightweight pure-Python implementation optimised for real-time STT
post-processing. No heavy dependencies (no NeMo / pynini).

Supported languages: Russian (ru), English (en).
For unsupported languages the text is returned unchanged.

Usage:
    from voxapp.itn import apply_itn
    apply_itn("сто двадцать три рубля", lang="ru")  # → "123 рубля"
    apply_itn("twenty one servers", lang="en")       # → "21 servers"
"""

from __future__ import annotations

from dataclasses import dataclass

# ---------------------------------------------------------------------------
# Russian number vocabulary
# ---------------------------------------------------------------------------
_RU_ONES: dict[str, int] = {
    "ноль": 0,
    "нуль": 0,
    "один": 1,
    "одна": 1,
    "одно": 1,
    "два": 2,
    "две": 2,
    "три": 3,
    "четыре": 4,
    "пять": 5,
    "шесть": 6,
    "семь": 7,
    "восемь": 8,
    "девять": 9,
    "десять": 10,
    "одиннадцать": 11,
    "двенадцать": 12,
    "тринадцать": 13,
    "четырнадцать": 14,
    "пятнадцать": 15,
    "шестнадцать": 16,
    "семнадцать": 17,
    "восемнадцать": 18,
    "девятнадцать": 19,
}

_RU_TENS: dict[str, int] = {
    "двадцать": 20,
    "тридцать": 30,
    "сорок": 40,
    "пятьдесят": 50,
    "шестьдесят": 60,
    "семьдесят": 70,
    "восемьдесят": 80,
    "девяносто": 90,
}

_RU_HUNDREDS: dict[str, int] = {
    "сто": 100,
    "двести": 200,
    "триста": 300,
    "четыреста": 400,
    "пятьсот": 500,
    "шестьсот": 600,
    "семьсот": 700,
    "восемьсот": 800,
    "девятьсот": 900,
}

# Multipliers with all grammatical forms
_RU_MULTIPLIERS: dict[str, int] = {
    "тысяча": 1_000,
    "тысячи": 1_000,
    "тысяч": 1_000,
    "миллион": 1_000_000,
    "миллиона": 1_000_000,
    "миллионов": 1_000_000,
    "миллиард": 1_000_000_000,
    "миллиарда": 1_000_000_000,
    "миллиардов": 1_000_000_000,
}

_RU_ALL_NUMBER_WORDS: set[str] = (
    set(_RU_ONES) | set(_RU_TENS) | set(_RU_HUNDREDS) | set(_RU_MULTIPLIERS)
)

# ---------------------------------------------------------------------------
# English number vocabulary
# ---------------------------------------------------------------------------
_EN_ONES: dict[str, int] = {
    "zero": 0,
    "one": 1,
    "two": 2,
    "three": 3,
    "four": 4,
    "five": 5,
    "six": 6,
    "seven": 7,
    "eight": 8,
    "nine": 9,
    "ten": 10,
    "eleven": 11,
    "twelve": 12,
    "thirteen": 13,
    "fourteen": 14,
    "fifteen": 15,
    "sixteen": 16,
    "seventeen": 17,
    "eighteen": 18,
    "nineteen": 19,
}

_EN_TENS: dict[str, int] = {
    "twenty": 20,
    "thirty": 30,
    "forty": 40,
    "fifty": 50,
    "sixty": 60,
    "seventy": 70,
    "eighty": 80,
    "ninety": 90,
}

_EN_MULTIPLIERS: dict[str, int] = {
    "hundred": 100,
    "thousand": 1_000,
    "million": 1_000_000,
    "billion": 1_000_000_000,
    "trillion": 1_000_000_000_000,
}

_EN_ALL_NUMBER_WORDS: set[str] = set(_EN_ONES) | set(_EN_TENS) | set(_EN_MULTIPLIERS)
# "and" is handled specially — only valid *between* number-words

# ---------------------------------------------------------------------------
# Parsing helpers
# ---------------------------------------------------------------------------


@dataclass
class _LangVocab:
    ones: dict[str, int]
    tens: dict[str, int]
    hundreds: dict[str, int]
    multipliers: dict[str, int]
    all_words: set[str]


_VOCABS: dict[str, _LangVocab] = {
    "ru": _LangVocab(_RU_ONES, _RU_TENS, _RU_HUNDREDS, _RU_MULTIPLIERS, _RU_ALL_NUMBER_WORDS),
    "en": _LangVocab(_EN_ONES, _EN_TENS, {}, _EN_MULTIPLIERS, _EN_ALL_NUMBER_WORDS),
}


def _word_value(word: str, vocab: _LangVocab) -> int | None:
    """Return numeric value for a single word, or None if not a number-word."""
    w = word.lower()
    for d in (vocab.ones, vocab.tens, vocab.hundreds, vocab.multipliers):
        if w in d:
            return d[w]
    return None


def _is_multiplier(word: str, vocab: _LangVocab) -> bool:
    return word.lower() in vocab.multipliers


def _multiplier_value(word: str, vocab: _LangVocab) -> int:
    return vocab.multipliers[word.lower()]


def _parse_number_words_ru(words: list[str]) -> str:
    """
    Parse a sequence of Russian number-words into a digit string.

    Algorithm: accumulate sub-1000 groups, multiply by multipliers,
    and sum everything.  E.g. "две тысячи триста сорок пять" →
    (2 × 1000) + 300 + 40 + 5 = 2345.
    """
    vocab = _VOCABS["ru"]
    total = 0
    current = 0  # accumulator for the current sub-group

    for w in words:
        wl = w.lower()
        if wl in vocab.hundreds:
            current += vocab.hundreds[wl]
        elif wl in vocab.tens:
            current += vocab.tens[wl]
        elif wl in vocab.ones:
            current += vocab.ones[wl]
        elif wl in vocab.multipliers:
            mult = vocab.multipliers[wl]
            if current == 0:
                current = 1  # "тысяча" alone = 1 × 1000
            total += current * mult
            current = 0

    total += current
    return str(total)


def _parse_number_words_en(words: list[str]) -> str:
    """
    Parse a sequence of English number-words into a digit string.

    Handles "hundred" as an internal multiplier and "thousand" / "million"
    etc. as group multipliers.
    """
    vocab = _VOCABS["en"]
    total = 0
    current = 0

    filtered = [w for w in words if w.lower() != "and"]

    for w in filtered:
        wl = w.lower()
        if wl in vocab.ones:
            current += vocab.ones[wl]
        elif wl in vocab.tens:
            current += vocab.tens[wl]
        elif wl == "hundred":
            if current == 0:
                current = 1
            current *= 100
        elif wl in vocab.multipliers:
            mult = vocab.multipliers[wl]
            if current == 0:
                current = 1
            total += current * mult
            current = 0

    total += current
    return str(total)


# ---------------------------------------------------------------------------
# Main entry point
# ---------------------------------------------------------------------------


def _is_number_word(word: str, vocab: _LangVocab) -> bool:
    """Check if word is a number-word."""
    return word.lower() in vocab.all_words


def _is_bridge_word(word: str, lang: str) -> bool:
    """Check if word is a bridge word (like 'and') that can appear inside number spans."""
    return lang == "en" and word.lower() == "and"


def _extract_number_spans(
    tokens: list[str], vocab: _LangVocab, lang: str
) -> list[tuple[int, int]]:
    """
    Find contiguous spans of number-words in the token list.

    Returns list of (start, end) index pairs (end is exclusive).
    Bridge words like "and" are included only when surrounded by number-words.
    """
    spans: list[tuple[int, int]] = []
    i = 0
    n = len(tokens)

    while i < n:
        if _is_number_word(tokens[i], vocab):
            start = i
            i += 1
            while i < n:
                if _is_number_word(tokens[i], vocab):
                    i += 1
                elif (
                    _is_bridge_word(tokens[i], lang)
                    and i + 1 < n
                    and _is_number_word(tokens[i + 1], vocab)
                ):
                    # "and" followed by a number-word — include both
                    i += 2
                else:
                    break
            spans.append((start, i))
        else:
            i += 1

    return spans


def apply_itn(text: str, lang: str = "ru") -> str:
    """
    Apply inverse text normalization: convert number-words to digits.

    Args:
        text: Input text (typically raw STT output).
        lang: Language code ("ru" or "en"). Unsupported → text returned as-is.

    Returns:
        Text with number-words replaced by digit representations.
    """
    if not text or not text.strip():
        return text

    vocab = _VOCABS.get(lang)
    if vocab is None:
        return text

    tokens = text.split()
    spans = _extract_number_spans(tokens, vocab, lang)

    if not spans:
        return text

    # Build result by replacing spans with digits
    result_parts: list[str] = []
    prev_end = 0

    parser = _parse_number_words_ru if lang == "ru" else _parse_number_words_en

    for start, end in spans:
        # Add non-number tokens before this span
        if prev_end < start:
            result_parts.extend(tokens[prev_end:start])

        number_words = tokens[start:end]
        digit_str = parser(number_words)
        result_parts.append(digit_str)
        prev_end = end

    # Add remaining tokens
    if prev_end < len(tokens):
        result_parts.extend(tokens[prev_end:])

    return " ".join(result_parts)
