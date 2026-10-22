"""Счёт терминов контрольного набора (только для координатора, см. docs/coordinator-holdout.md).
python tools/eval/score-holdout.py docs/coordinator-holdout.md <вывод Vox.Replay> [--misses]
Строки вывода (заголовок пропускается) в порядке фраз набора, Final в последней колонке. Правило совпадения как в score-set.py.
"""
import re, sys

md, tsv = sys.argv[1], sys.argv[2]
show = "--misses" in sys.argv
phrases = []
for line in open(md, encoding="utf-8"):
    m = re.match(r"^(\d+)\. (.*) \[(.*)\]\s*$", line)
    if m:
        phrases.append((m.group(2), [t.strip() for t in m.group(3).split(",")]))
rows = [l.rstrip("\r\n").split("\t") for l in open(tsv, encoding="utf-8-sig") if l.strip()]
if rows and rows[0][0] == "file":
    rows = rows[1:]
assert len(rows) == len(phrases), f"{len(rows)} строк, фраз {len(phrases)}"


def hit(t, s):
    return t in s or (t[0].islower() and t.lower() in s.lower())


ok = total = 0
for (exp, terms), row in zip(phrases, rows):
    fin = row[-1]
    for t in terms:
        total += 1
        if hit(t, fin):
            ok += 1
        elif show:
            print(f"{t!r}\n   exp: {exp}\n   raw: {row[-2]}\n   fin: {fin}")
print(f"{tsv}: {ok}/{total}")
