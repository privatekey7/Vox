"""Счёт пилота дообучения (docs/plan-gigaam-finetune.md, этап 2). Только для координатора.
python tools/finetune/score_pilot.py --holdout BASE NEW --a30 BASE NEW --rutest BASE NEW

Каждый файл — вывод `Vox.Replay --audio <папка> --models <корень моделей>` (file, recorded raw, new raw, new final).
BASE — экспорт исходной v3 (tools/finetune/export_sherpa.py v3_e2e_rnnt), NEW — экспорт дообученной.
- holdout: контрольный набор №1 (docs/coordinator-holdout.md), его термины в обучение не шли; делится на
  «совсем новые» и «делят слово с обучающим термином» (GitHub Actions при обучающем GitHub);
- a30: записи anglicisms-30, латинские термины которых были в пилоте (tools/eval/score-set.py);
- rutest: русский WER против человеческой расшифровки (tools/finetune/make_testset.py).
Термины по Raw (что пишет сама модель) и по Final (со словарём). Лишняя латиница: латинские слова, которых
нет в эталоне — в Raw (выдумала модель) и в Final, но не в Raw (подтянул словарь).
"""
import argparse, ast, csv, re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
p = argparse.ArgumentParser()
for name in ("holdout", "a30", "rutest"):
    p.add_argument(f"--{name}", nargs=2, metavar=("BASE", "NEW"))
a = p.parse_args()


def replay(path):
    rows = [l.rstrip("\r\n").split("\t") for l in open(path, encoding="utf-8-sig") if l.strip()]
    return [(r[-2], r[-1]) for r in rows[1:]] if rows and rows[0][0] == "file" else [(r[-2], r[-1]) for r in rows]


def hit(t, s):
    return t in s or (t[0].islower() and t.lower() in s.lower())


def latin(s):
    return {w.lower() for w in re.findall(r"[A-Za-z]+", s)}


def extra(expected, raw, final):
    known = latin(expected)
    return len(latin(raw) - known), len(latin(final) - known - latin(raw))


def words(s):
    return re.findall(r"[\w+#]+", s.lower().replace("ё", "е"))


def distance(x, y):
    row = list(range(len(y) + 1))
    for i, u in enumerate(x, 1):
        prev, row[0] = row[0], i
        for j, v in enumerate(y, 1):
            prev, row[j] = row[j], min(row[j] + 1, row[j - 1] + 1, prev + (u != v))
    return row[-1]


terms = list(csv.DictReader(open(ROOT / "tools/finetune/data/terms.tsv", encoding="utf-8"), delimiter="\t"))
train_words = {w for r in terms if r["split"] == "train" and r["script"] == "latin" for w in latin(r["term"])}
pilot = {r["term"] for r in terms if r["pilot"] == "1"}


def term_table(title, phrases, files, groups):
    print(f"\n{title}")
    print(f"{'':<34}{'база Raw':>10}{'база Final':>12}{'новая Raw':>11}{'новая Final':>13}")
    data = [replay(f) for f in files]
    for label, keep in groups:
        cells = []
        for rows in data:
            assert len(rows) == len(phrases), f"{len(rows)} строк, фраз {len(phrases)}"
            for col in (0, 1):
                ok = sum(hit(t, row[col]) for (_, ts), row in zip(phrases, rows) for t in ts if keep(t))
                total = sum(1 for _, ts in phrases for t in ts if keep(t))
                cells.append(f"{ok}/{total}")
        print(f"{label:<34}{cells[0]:>10}{cells[1]:>12}{cells[2]:>11}{cells[3]:>13}")
    for name, rows in zip(("база", "новая"), data):
        model_extra = sum(extra(e, r, f)[0] for (e, _), (r, f) in zip(phrases, rows))
        dict_extra = sum(extra(e, r, f)[1] for (e, _), (r, f) in zip(phrases, rows))
        print(f"лишняя латиница, {name}: модель {model_extra}, словарь {dict_extra}")


if a.holdout:
    phrases = [(m.group(1), [t.strip() for t in m.group(2).split(",")])
               for m in (re.match(r"^\d+\. (.*) \[(.*)\]\s*$", l) for l in open(ROOT / "docs/coordinator-holdout.md", encoding="utf-8")) if m]
    shared = lambda t: bool(latin(t) & train_words)
    term_table("Контрольный набор №1 (термины не обучались)", phrases, a.holdout, [
        ("все", lambda t: True),
        ("латиница, совсем новые", lambda t: bool(latin(t)) and not shared(t)),
        ("латиница, общее слово с обучающим", lambda t: bool(latin(t)) and shared(t)),
        ("кириллица (прижившиеся)", lambda t: not latin(t)),
    ])

if a.a30:
    src = (ROOT / "tools/eval/score-set.py").read_text(encoding="utf-8")
    k = ast.literal_eval(re.search(r"^K = (\[.*?\])\s*$", src, re.M | re.S).group(1))
    expected = [l.rstrip("\n").split("\t")[0] for l in open(ROOT / "tools/eval/anglicisms-30.tsv", encoding="utf-8")][1:]
    term_table("anglicisms-30 (живой голос)", list(zip(expected, k)), a.a30, [
        ("все", lambda t: True),
        ("латиница, были в пилоте", lambda t: t in pilot),
        ("остальные", lambda t: t not in pilot),
    ])

if a.rutest:
    print("\nРусский WER (Raw против человеческой расшифровки, без регистра и пунктуации)")
    for name, path in zip(("база", "новая"), a.rutest):
        rows = [l.rstrip("\r\n").split("\t") for l in open(path, encoding="utf-8-sig") if l.strip()][1:]
        errors = sum(distance(words(r[1]), words(r[2])) for r in rows)
        total = sum(len(words(r[1])) for r in rows)
        print(f"{name}: {100 * errors / total:.2f}% ({errors}/{total}, клипов {len(rows)})")
