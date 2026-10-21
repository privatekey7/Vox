"""Pipeline wiring per model: GigaAM already normalises numbers, Parakeet does not."""

from voxapp.post_processor import PostProcessor
from voxapp.registry import MODELS


def _pp(model_id: str) -> PostProcessor:
    return PostProcessor.from_model_config(MODELS[model_id])


def test_gigaam_keeps_words_that_itn_would_break():
    assert _pp("gigaam-v3-rnnt-punct").process_text("Один из вариантов.") == " Один из вариантов."


def test_gigaam_spells_out_ruble_sign():
    assert _pp("gigaam-v3-rnnt-punct").process_text("Переведи 500 ₽ на карту.") == (
        " Переведи 500 рублей на карту."
    )


def test_gigaam_maps_cyrillic_tech_terms_to_latin():
    assert "GitHub" in _pp("gigaam-v3-rnnt-punct").process_text("Запушь в гитхаб.")


def test_parakeet_converts_mixed_numbers():
    assert (
        _pp("parakeet-tdt-0.6b-v3").process_text("У меня 30 шесть задач.") == " У меня 36 задач."
    )
