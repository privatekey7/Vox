"""
Unified post-processing pipeline for Vox transcriptions.

Replaces scattered inline processing in the worker with a single
configurable pipeline:

    raw STT text → terms → fillers → ITN → sentence-spacing → punctuation
    → capitalization → leading-space

Each step can be toggled on/off. Model-specific flags from the registry
control which steps apply.

Usage:
    from voxapp.post_processor import PostProcessor

    pp = PostProcessor(lang="ru")
    result = pp.process("эээ мне нужно пять серверов", preceding_text="Задача:")
    # "Мне нужно 5 серверов."
"""

from __future__ import annotations

from dataclasses import dataclass, field

from voxapp.capitalization import apply_capitalization
from voxapp.config import CURSOR_CONTEXT_ENABLED, LEADING_SPACE_ALWAYS
from voxapp.fillers import remove_fillers
from voxapp.itn import apply_itn
from voxapp.punctuation import add_punctuation, replace_terms
from voxapp.sentence_spacing import normalize_sentence_spacing
from voxapp.spacing import apply_leading_space, apply_unconditional_leading_space


@dataclass
class PipelineResult:
    """Result of the post-processing pipeline with intermediate steps."""

    raw: str
    processed: str
    steps: dict[str, str] = field(default_factory=dict)


class PostProcessor:
    """
    Configurable text post-processing pipeline.

    Args:
        lang: Language code ("ru" or "en"). Affects fillers and ITN.
        enable_terms: Replace phonetic tech terms (реакт → React).
        enable_fillers: Remove filler words (ну, типа, um, uh).
        enable_itn: Convert number-words to digits.
        enable_sentence_spacing: Insert missing spaces after glued sentence
            punctuation emitted by punctuation-aware STT models.
        enable_punctuation: Add sentence-end punctuation and capitalize.
        enable_capitalization: Smart first-letter case from cursor context.
        enable_spacing: Insert a leading separator space from cursor context.
        always_leading_space: Always prepend one space before the output,
            cursor-free (overrides ``enable_spacing``/``preceding_text``).
    """

    def __init__(
        self,
        *,
        lang: str = "ru",
        enable_terms: bool = True,
        enable_fillers: bool = True,
        enable_itn: bool = True,
        enable_sentence_spacing: bool = True,
        enable_punctuation: bool = True,
        enable_capitalization: bool = True,
        enable_spacing: bool = True,
        always_leading_space: bool = False,
    ) -> None:
        self.lang = lang
        self.enable_terms = enable_terms
        self.enable_fillers = enable_fillers
        self.enable_itn = enable_itn
        self.enable_sentence_spacing = enable_sentence_spacing
        self.enable_punctuation = enable_punctuation
        self.enable_capitalization = enable_capitalization
        self.enable_spacing = enable_spacing
        self.always_leading_space = always_leading_space

    @classmethod
    def from_model_config(
        cls,
        model_config: dict,
        *,
        lang: str | None = None,
    ) -> PostProcessor:
        """
        Create a PostProcessor using flags from a model's registry entry.

        Args:
            model_config: A dict from ``registry.MODELS[model_id]``.
            lang: Override language. If None, uses model_config["language"]
                  (falling back to "ru" for "multi" models).
        """
        model_lang = model_config.get("language", "ru")
        if lang is None:
            lang = model_lang if model_lang != "multi" else "ru"

        # Contextual capitalization and the leading separator space both rely on
        # reading the text before the cursor. That cursor read disturbs the
        # caret in browsers/editors (the "reversed text" bug), so it is gated
        # behind CURSOR_CONTEXT_ENABLED (off by default). See D020.
        cursor_steps = CURSOR_CONTEXT_ENABLED

        return cls(
            lang=lang,
            enable_terms=model_config.get("needs_term_replace", True),
            enable_fillers=True,
            enable_itn=True,
            # Runs for every model: punctuation-aware models (needs_punctuation
            # False) emit glued sentences like "Привет.Что" that still need
            # separator spaces.
            enable_sentence_spacing=True,
            enable_punctuation=model_config.get("needs_punctuation", True),
            enable_capitalization=cursor_steps,
            enable_spacing=cursor_steps,
            # Cursor-free separator: always prepend one space (D029). Replaces
            # the cursor-context leading-space step that is off by default.
            always_leading_space=LEADING_SPACE_ALWAYS,
        )

    def process(self, text: str, *, preceding_text: str | None = None) -> PipelineResult:
        """
        Run the full pipeline on raw STT output.

        Args:
            text: Raw text from the STT engine.
            preceding_text: Text before the cursor in the target field
                            (used for contextual capitalization).
                            ``None`` means "no cursor context available" —
                            the capitalization step is skipped.
                            ``""`` means "start of document" — capitalize.

        Returns:
            PipelineResult with the final processed text and each step's output.
        """
        result = PipelineResult(raw=text, processed=text)
        current = text

        if not current or not current.strip():
            result.processed = current
            return result

        # 1. Term replacement (phonetic → English tech terms)
        if self.enable_terms:
            current = replace_terms(current)
            result.steps["terms"] = current

        # 2. Filler word removal
        if self.enable_fillers:
            current = remove_fillers(current, lang=self.lang)
            result.steps["fillers"] = current

        # 3. Inverse text normalization (numbers)
        if self.enable_itn:
            current = apply_itn(current, lang=self.lang)
            result.steps["itn"] = current

        # 3b. Sentence spacing — split glued sentences from punctuation-aware
        #     models ("Привет.Что" → "Привет. Что"). Runs before add_punctuation
        #     so the trailing-punctuation heuristic sees clean, spaced text.
        if self.enable_sentence_spacing:
            spaced = normalize_sentence_spacing(current)
            if spaced != current:
                current = spaced
                result.steps["sentence_spacing"] = current

        # 4. Punctuation (sentence ending + initial capitalize)
        if self.enable_punctuation:
            current = add_punctuation(current)
            result.steps["punctuation"] = current

        # 5. Contextual capitalization (override initial cap based on context).
        #    Only runs when real cursor context is provided (preceding_text
        #    is not None).  When preceding_text is None the step is skipped
        #    to avoid false capitalisation after ITN digits (e.g.
        #    "23 Сервера." instead of "23 сервера.").
        if self.enable_capitalization and preceding_text is not None:
            current = apply_capitalization(current, preceding_text)
            result.steps["capitalization"] = current

        # 6. Leading separator space (MUST be last so trimming/normalisation
        #    can't strip it).
        #    - always_leading_space (D029): prepend one space unconditionally,
        #      cursor-free. This is the default — Vox can't reliably read the
        #      caret (D020), so it simply always separates dictations.
        #    - otherwise fall back to the cursor-context rule, which only runs
        #      when real cursor context is available (preceding_text not None).
        if self.always_leading_space:
            spaced = apply_unconditional_leading_space(current)
        elif self.enable_spacing and preceding_text is not None:
            spaced = apply_leading_space(current, preceding_text)
        else:
            spaced = current
        if spaced != current:
            current = spaced
            result.steps["spacing"] = current

        result.processed = current
        return result

    def process_text(self, text: str, *, preceding_text: str | None = None) -> str:
        """
        Convenience method — returns just the processed text string.

        Same as ``process(text).processed``.
        """
        return self.process(text, preceding_text=preceding_text).processed
