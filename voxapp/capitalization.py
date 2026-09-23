"""
Contextual capitalization based on surrounding text.

The module determines whether to capitalize the first letter of new
transcription text by examining what precedes the insertion point
(the last character before the cursor).

Rules:
    - After sentence-ending punctuation (. ! ? …) → capitalize
    - After newline or empty context → capitalize
    - After opening brackets/quotes → capitalize
    - After colon → capitalize (new clause)
    - After everything else (letter, comma, semicolon, space…) → lowercase
    - Always preserve already-uppercase words (acronyms: API, USB, etc.)
"""

from __future__ import annotations

# Characters after which the next sentence starts (→ capitalize)
_SENTENCE_ENDERS = frozenset(".!?…")

# Opening delimiters that start a new context (→ capitalize)
_OPENERS = frozenset("([{«\"'")


def _should_capitalize(preceding_text: str) -> bool:
    """
    Decide whether to capitalize based on text before the cursor.

    Args:
        preceding_text: The text immediately before the insertion point.
                        Can be empty (start of document), a single char,
                        or any length (only the tail matters).

    Returns:
        True if the first letter of new text should be capitalized.
    """
    if not preceding_text:
        # Start of document / empty field
        return True

    # Check for trailing newline before stripping (new paragraph)
    if preceding_text.rstrip(" \t").endswith("\n"):
        return True

    # Walk backward past trailing whitespace to find the last "real" char
    stripped = preceding_text.rstrip()
    if not stripped:
        # Only whitespace before cursor — treat as start of paragraph
        return True

    last_char = stripped[-1]

    if last_char in _SENTENCE_ENDERS:
        return True
    if last_char in _OPENERS:
        return True

    return last_char == ":"


def apply_capitalization(text: str, preceding_text: str = "") -> str:
    """
    Adjust the first letter of *text* based on *preceding_text*.

    If *preceding_text* suggests the start of a sentence, the first letter
    is capitalized. Otherwise, the first letter is lowercased — unless
    the first word is fully uppercase (e.g. "API", "USB"), which is
    preserved as-is.

    Args:
        text: The transcribed text to adjust.
        preceding_text: Whatever is in the text field before the cursor.

    Returns:
        The text with contextually adjusted capitalization.
    """
    if not text:
        return text

    # Find the first alphabetic character
    first_alpha_idx = -1
    for i, ch in enumerate(text):
        if ch.isalpha():
            first_alpha_idx = i
            break

    if first_alpha_idx == -1:
        # No letters at all (e.g. "123")
        return text

    # Extract the first word to check for acronyms
    first_word_end = first_alpha_idx
    while first_word_end < len(text) and (
        text[first_word_end].isalpha() or text[first_word_end] == "'"
    ):
        first_word_end += 1
    first_word = text[first_alpha_idx:first_word_end]

    # Preserve all-uppercase words (acronyms): API, USB, IT, etc.
    if len(first_word) >= 2 and first_word.isupper():
        return text

    capitalize = _should_capitalize(preceding_text)

    if capitalize:
        adjusted = (
            text[:first_alpha_idx] + text[first_alpha_idx].upper() + text[first_alpha_idx + 1 :]
        )
    else:
        adjusted = (
            text[:first_alpha_idx] + text[first_alpha_idx].lower() + text[first_alpha_idx + 1 :]
        )

    return adjusted
