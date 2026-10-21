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

import re
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


# ---------------------------------------------------------------------------
# Russian: slot-based parser that also understands digit tokens
# ---------------------------------------------------------------------------
#
# Parakeet often emits half-normalised numbers ("30 шесть", "2 тысячи двадцать
# пять"), so digits and number-words must be parsed together. Each element
# occupies decimal slots (1 = ones, 2 = tens, 3 = hundreds); an element may
# only extend the current number if its highest slot is below the lowest slot
# already filled. Otherwise a new number starts — "одиннадцать тридцать" is
# 11 and 30, not 41.

_TRAILING_PUNCT = ".,!?;:…"
_RU_PRONOUN_ONES = {"один", "одна", "одно"}
_RU_LONE_MULTIPLIERS = {"тысяча"}
_RU_ORDINAL_RE = re.compile(
    r"^(перв|втор|трет|четв[её]рт|пят|шест|седьм|восьм|девят|десят|\w+надцат|двадцат"
    r"|тридцат|сороков|\w+десят|девяност|\w*сот|тысячн|миллионн)"
    r"(ый|ой|ий|ая|ое|ья|ье|ьи|ого|ему|ому|ым|ом|ую|ых|ыми|ые|ьего|ьему|ьим|ьем|ью|ьей|ьих)$"
)


@dataclass
class _RuPiece:
    token: str  # original token, including any trailing punctuation
    value: int
    hi: int  # highest decimal slot occupied (0 for multipliers)
    lo: int  # lowest decimal slot occupied
    is_digit: bool
    is_multiplier: bool


def _split_trailing_punct(token: str) -> tuple[str, str]:
    core = token.rstrip(_TRAILING_PUNCT)
    return core, token[len(core) :]


def _slots(value: int) -> tuple[int, int]:
    """(highest, lowest) decimal slot of a sub-1000 value; teens fill both tens and ones."""
    if value == 0:
        return 1, 1
    digits = [value // 100, (value // 10) % 10, value % 10]  # slots 3, 2, 1
    hi = 3 - next(i for i, d in enumerate(digits) if d)
    lo = 1 + next(i for i, d in enumerate(reversed(digits)) if d)
    if digits[1] == 1:  # 10..19 is a single word occupying tens + ones
        lo = 1
    return hi, lo


def _ru_piece(token: str) -> _RuPiece | None:
    core, _ = _split_trailing_punct(token)
    word = core.lower()
    if word.isdigit() and len(word) <= 3 and (word == "0" or not word.startswith("0")):
        hi, lo = _slots(int(word))
        return _RuPiece(token, int(word), hi, lo, is_digit=True, is_multiplier=False)
    if word in _RU_MULTIPLIERS:
        return _RuPiece(token, _RU_MULTIPLIERS[word], 0, 0, is_digit=False, is_multiplier=True)
    for table in (_RU_ONES, _RU_TENS, _RU_HUNDREDS):
        if word in table:
            hi, lo = _slots(table[word])
            return _RuPiece(token, table[word], hi, lo, is_digit=False, is_multiplier=False)
    return None


def _render_ru_number(pieces: list[_RuPiece]) -> list[str]:
    """Digits for a parsed number, or the original tokens when converting would hurt."""
    words = [p for p in pieces if not p.is_digit]
    plain_words = [p for p in words if not p.is_multiplier]
    first = _split_trailing_punct(pieces[0].token)[0].lower()
    keep_original = (
        not words  # pure digit run, e.g. a phone number
        or (not plain_words and any(p.is_digit for p in pieces))  # "150 тысяч"
        or (len(pieces) == 1 and first in _RU_PRONOUN_ONES)  # "один из"
        or (len(pieces) == 1 and pieces[0].is_multiplier and first not in _RU_LONE_MULTIPLIERS)
    )
    if keep_original:
        return [p.token for p in pieces]

    total = current = 0
    for p in pieces:
        if p.is_multiplier:
            total += (current or 1) * p.value
            current = 0
        else:
            current += p.value
    trailing = _split_trailing_punct(pieces[-1].token)[1]
    return [f"{total + current}{trailing}"]


def _group_ru_numbers(pieces: list[_RuPiece]) -> list[list[_RuPiece]]:
    """Split one contiguous run of number tokens into separate numbers."""
    numbers: list[list[_RuPiece]] = []
    cur: list[_RuPiece] = []
    free_below = 4  # next element's highest slot must be below this
    last_mult = float("inf")

    for p in pieces:
        if p.is_multiplier:
            joins = p.value < last_mult and not (cur and cur[-1].is_multiplier)
        else:
            joins = (
                bool(cur)
                and p.hi < free_below
                and not (p.is_digit and cur[-1].is_digit)  # never merge "900 55"
            )
        if cur and not joins:
            numbers.append(cur)
            cur, free_below, last_mult = [], 4, float("inf")
        cur.append(p)
        if p.is_multiplier:
            free_below, last_mult = 4, p.value
        else:
            free_below = p.lo
    if cur:
        numbers.append(cur)
    return numbers


def _apply_itn_ru(text: str) -> str:
    tokens = text.split()
    out: list[str] = []
    i = 0
    while i < len(tokens):
        span: list[_RuPiece] = []
        while i < len(tokens) and (piece := _ru_piece(tokens[i])) is not None:
            span.append(piece)
            i += 1
            if _split_trailing_punct(piece.token)[1]:
                break  # punctuation closes the number
        if not span:
            out.append(tokens[i])
            i += 1
            continue
        next_word = _split_trailing_punct(tokens[i])[0].lower() if i < len(tokens) else ""
        if _RU_ORDINAL_RE.match(next_word):
            out.extend(p.token for p in span)  # "двадцать шестой" stays words
            continue
        for number in _group_ru_numbers(span):
            out.extend(_render_ru_number(number))
    return " ".join(out)


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

    if lang == "ru":
        return _apply_itn_ru(text)

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

    parser = _parse_number_words_en

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
