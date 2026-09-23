"""
Leading-separator spacing based on cursor context.

Vox inserts each dictation by pasting at the cursor. On its own that means
two consecutive dictations collide with no separator::

    "Я пишу сейчас." + "И вижу" → "Я пишу сейчас.И вижу"   ← wrong

This module decides whether a single leading space must be prepended to the
new text so it reads naturally relative to what already sits before the
cursor::

    after a word / digit       → add space   "мир"  + "как дела" → " как дела"
    after a comma / period / …  → add space   "сейчас." + "И вижу" → " И вижу"
    after an opening bracket/quote → no space "«" + "Привет"      → "Привет"
    cursor already after a space   → no space (already separated)
    start of document / empty      → no space

It is intentionally separate from ``capitalization`` (which only fixes the
*case* of the first letter). Capitalization decides "big or small letter";
spacing decides "glued or separated". Both read the same cursor context but
answer different questions.
"""

from __future__ import annotations

# Opening delimiters: new text should hug them, so NO leading space.
#   «Привет   (примечание   "цитата
_OPENERS = frozenset("([{«„“\"'<")


def needs_leading_space(preceding_text: str) -> bool:
    """
    Decide whether a single space must separate the cursor's preceding
    text from the new dictation.

    Args:
        preceding_text: Text immediately before the insertion point
                        (current line up to the cursor). ``""`` means
                        start of document / empty field.

    Returns:
        True if a leading space should be prepended to the new text.
    """
    if not preceding_text:
        # Start of document / empty field — nothing to separate from.
        return False

    last_char = preceding_text[-1]

    # Cursor already sits after whitespace — already separated.
    if last_char.isspace():
        return False

    # After an opener the new text should touch it: «Привет, not « Привет.
    # After anything else (word, digit, comma, period, colon, …) → separate.
    return last_char not in _OPENERS


def apply_leading_space(text: str, preceding_text: str | None = None) -> str:
    """
    Prepend a single space to *text* when the cursor context requires a
    separator.

    Args:
        text: The (already case-adjusted) text about to be pasted.
        preceding_text: Text before the cursor. ``None`` means "no cursor
                        context available" — the text is returned unchanged.
                        ``""`` means start of document — no space added.

    Returns:
        *text*, optionally with one leading space.
    """
    if not text or preceding_text is None:
        return text

    # New text already starts with whitespace — don't double it.
    if text[0].isspace():
        return text

    if needs_leading_space(preceding_text):
        return " " + text

    return text


def apply_unconditional_leading_space(text: str) -> str:
    """
    Prepend a single space to *text* unconditionally — without reading any
    cursor context.

    This is the cursor-free separator strategy (config ``LEADING_SPACE_ALWAYS``):
    each dictation gets exactly one leading space so consecutive dictations
    never glue together. Because Vox cannot reliably read the text before the
    cursor (the clipboard-selection trick disturbs the caret in browsers, see
    D020), we stop trying to and simply always separate. The only cost is a
    leading space on the very first dictation in an empty field, which does not
    matter for dictation.

    Args:
        text: The (already processed) text about to be emitted.

    Returns:
        *text* with one leading space, unless it is empty or already starts
        with a space (in which case it is returned unchanged — we never double
        the separator or emit a lone space for empty output). A leading tab is
        not the separator, so it still gets a space, mirroring
        :func:`split_leading_separator`.
    """
    if not text or text.startswith(" "):
        return text
    return " " + text


def split_leading_separator(text: str) -> tuple[str, str]:
    """
    Split a single leading separator space off *text*.

    Vox pastes each dictation through the clipboard, but some targets strip
    leading whitespace from pasted content. The Chrome/Edge address bar
    (omnibox) is the worst offender: it runs ``base::CollapseWhitespace`` on
    pasted text, dropping the leading separator that :func:`apply_leading_space`
    added. Consecutive dictations then glue together::

        "Привет." + " Как дела"  → pasted → "Привет.Как дела"   ← wrong

    To survive that, the worker emits the separator as a real keystroke (which
    the omnibox does not trim) and pastes only the body. This helper performs
    the split.

    Only ONE leading space is treated as the separator, because
    :func:`apply_leading_space` ever prepends at most a single space. Any
    further leading whitespace (deliberate indentation, tabs) stays in the
    body untouched.

    Args:
        text: The fully processed text about to be emitted.

    Returns:
        ``(separator, body)`` where *separator* is ``" "`` when *text* began
        with a space and ``""`` otherwise, and ``separator + body == text``.
    """
    if text.startswith(" "):
        return " ", text[1:]
    return "", text
