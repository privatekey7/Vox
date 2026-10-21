"""ITN regression tests — cases taken from real Parakeet output in vox.log."""

import pytest

from voxapp.itn import apply_itn


@pytest.mark.parametrize(
    ("text", "expected"),
    [
        # Parakeet sometimes emits the tens as digits and the unit as a word.
        ("30 шесть", "36"),
        ("у меня 30 шесть задач", "у меня 36 задач"),
        ("тридцать 6", "36"),
        ("100 двадцать три", "123"),
        ("2 тысячи двадцать пять", "2025"),
        # A digit followed by a multiplier word must not split into two numbers.
        ("150 тысяч рублей", "150 тысяч рублей"),
        ("5 миллионов", "5 миллионов"),
        # Two complete numbers in a row stay two numbers (time, scores).
        ("встреча в одиннадцать тридцать", "встреча в 11 30"),
        ("двадцать двадцать", "20 20"),
        # "один" alone is usually a pronoun, not a count.
        ("один из вариантов", "один из вариантов"),
        ("Одна из задач", "Одна из задач"),
        # Phone-like digit runs are never merged.
        ("8 900 555 35 35", "8 900 555 35 35"),
        ("900 55 55", "900 55 55"),
        # Ordinals are left alone rather than half-converted.
        ("две тысячи двадцать шестой год", "две тысячи двадцать шестой год"),
        # Trailing punctuation does not block conversion and is preserved.
        ("у меня тридцать шесть.", "у меня 36."),
        ("пять, шесть, семь", "5, 6, 7"),
        # Real dictations from vox.log.
        ("Семнадцать пятьдесят четыре.", "17 54."),
        ("Зона сорок четыре.", "Зона 44."),
        ("Сто семьдесят пять, двадцать один.", "175, 21."),
        ("номер одиннадцать девятьсот два.", "номер 11 902."),
        ("при проверке там миллион.", "при проверке там миллион."),
        ("около миллиарда", "около миллиарда"),
        (
            "на секундах сорок четвертый по сорок шестую",
            "на секундах сорок четвертый по сорок шестую",
        ),
    ],
)
def test_apply_itn_ru_regressions(text, expected):
    assert apply_itn(text, "ru") == expected


@pytest.mark.parametrize(
    ("text", "expected"),
    [
        ("сто двадцать три рубля", "123 рубля"),
        ("двадцать пять серверов", "25 серверов"),
        ("пять серверов", "5 серверов"),
        ("сто пятьдесят тысяч рублей", "150000 рублей"),
        ("две тысячи триста сорок пять", "2345"),
        ("тысяча рублей", "1000 рублей"),
        ("минус пять градусов", "минус 5 градусов"),
        ("пятнадцать процентов", "15 процентов"),
        ("Тридцать шесть", "36"),
    ],
)
def test_apply_itn_ru_existing_behaviour_kept(text, expected):
    assert apply_itn(text, "ru") == expected


@pytest.mark.parametrize("text", ["", "   ", "привет мир", "Version 2026 released"])
def test_apply_itn_ru_leaves_non_numeric_text_unchanged(text):
    assert apply_itn(text, "ru") == text


def test_apply_itn_en_still_works():
    assert apply_itn("twenty one servers", "en") == "21 servers"


def test_apply_itn_unknown_language_is_noop():
    assert apply_itn("тридцать шесть", "de") == "тридцать шесть"
