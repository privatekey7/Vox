"""Ruble sign → word with Russian plural agreement (GigaAM writes '500 ₽')."""

import pytest

from voxapp.currency import spell_out_currency


@pytest.mark.parametrize(
    ("text", "expected"),
    [
        ("Переведи 500 ₽ на карту.", "Переведи 500 рублей на карту."),
        ("1 ₽", "1 рубль"),
        ("21 ₽", "21 рубль"),
        ("2 ₽", "2 рубля"),
        ("34 ₽", "34 рубля"),
        ("11 ₽", "11 рублей"),
        ("12 ₽", "12 рублей"),
        ("111 ₽", "111 рублей"),
        ("150 000 ₽ до 25-го числа.", "150 000 рублей до 25-го числа."),
        ("1 001 ₽", "1 001 рубль"),
        ("1,5 ₽", "1,5 рубля"),
        ("500₽", "500 рублей"),
        ("Стоит 99 ₽, а не 100 ₽.", "Стоит 99 рублей, а не 100 рублей."),
        ("цена в ₽", "цена в рублях"),
    ],
)
def test_spell_out_currency_rubles(text, expected):
    assert spell_out_currency(text) == expected


@pytest.mark.parametrize("text", ["", "без валюты", "500 рублей", "$5"])
def test_spell_out_currency_leaves_other_text_unchanged(text):
    assert spell_out_currency(text) == text
