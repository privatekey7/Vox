"""Шаг 2: что второй проход может добавить к основному (только для координатора, см. docs/coordinator-holdout.md).
python tools/eval/score-merge.py docs/coordinator-holdout.md <основной вывод Replay> <второй вывод Replay>
Потолок объединения: термин засчитан, если он есть в Final хотя бы одного прохода (идеальный выбор).
Ложные вставки словаря: латинские слова Final, которых нет ни в Raw, ни в эталонной фразе (без учёта регистра).
Правило совпадения как в score-holdout.py.
"""
import re, sys

md, base_tsv, other_tsv = sys.argv[1:4]
phrases = []
for line in open(md, encoding="utf-8"):
    m = re.match(r"^(\d+)\. (.*) \[(.*)\]\s*$", line)
    if m:
        phrases.append((m.group(2), [t.strip() for t in m.group(3).split(",")]))


def finals(tsv):
    rows = [l.rstrip("\r\n").split("\t") for l in open(tsv, encoding="utf-8-sig") if l.strip()]
    if rows and rows[0][0] == "file":
        rows = rows[1:]
    assert len(rows) == len(phrases), f"{tsv}: {len(rows)} строк, фраз {len(phrases)}"
    return [(r[-2], r[-1]) for r in rows]


def hit(t, s):
    return t in s or (t[0].islower() and t.lower() in s.lower())


def false_latin(exp, raw, fin):
    known = {w.lower() for w in re.findall(r"[A-Za-z]+", exp + " " + raw)}
    return [w for w in re.findall(r"[A-Za-z]+", fin) if w.lower() not in known]


base, other = finals(base_tsv), finals(other_tsv)
total = sum(len(t) for _, t in phrases)
b = o = only_other = only_base = 0
for (exp, terms), (_, fb), (_, fo) in zip(phrases, base, other):
    for t in terms:
        hb, ho = hit(t, fb), hit(t, fo)
        b += hb
        o += ho
        only_other += ho and not hb
        only_base += hb and not ho
fb_false = sum(len(false_latin(e, r, f)) for (e, _), (r, f) in zip(phrases, base))
fo_false = sum(len(false_latin(e, r, f)) for (e, _), (r, f) in zip(phrases, other))
print(f"основной {b}/{total}, второй {o}/{total}")
print(f"только во втором: {only_other}, только в основном: {only_base}, потолок объединения {b + only_other}/{total}")
print(f"ложные вставки словаря: основной {fb_false}, второй {fo_false}")
