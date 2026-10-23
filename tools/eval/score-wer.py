"""WER по словам против эталонных фраз контрольного набора (только для координатора, см. docs/coordinator-holdout.md).
python tools/eval/score-wer.py docs/coordinator-holdout.md <вывод Replay> ...
Сравнение без регистра и пунктуации, ё = е. Это общий уровень ошибок (русские слова и термины вместе).
"""
import re, sys

phrases = []
for line in open(sys.argv[1], encoding="utf-8"):
    m = re.match(r"^(\d+)\. (.*) \[(.*)\]\s*$", line)
    if m:
        phrases.append(m.group(2))


def words(s):
    return re.findall(r"[\w+#]+", s.lower().replace("ё", "е"))


def distance(a, b):
    row = list(range(len(b) + 1))
    for i, x in enumerate(a, 1):
        prev, row[0] = row[0], i
        for j, y in enumerate(b, 1):
            prev, row[j] = row[j], min(row[j] + 1, row[j - 1] + 1, prev + (x != y))
    return row[-1]


for tsv in sys.argv[2:]:
    rows = [l.rstrip("\r\n").split("\t") for l in open(tsv, encoding="utf-8-sig") if l.strip()]
    if rows and rows[0][0] == "file":
        rows = rows[1:]
    assert len(rows) == len(phrases), f"{tsv}: {len(rows)} строк, фраз {len(phrases)}"
    errors = sum(distance(words(e), words(r[-1])) for e, r in zip(phrases, rows))
    total = sum(len(words(e)) for e in phrases)
    print(f"{tsv}: WER {100 * errors / total:.1f}% ({errors}/{total})")
