"""Список терминов для дообучения GigaAM (docs/plan-gigaam-finetune.md).
python tools/finetune/make_terms.py > tools/finetune/data/terms.tsv

Колонки: term, script (latin | cyrillic), split (train | heldout), pilot (1 = в пилоте), source, hints.
- latin: цели terms.json с латиницей и brands.txt; в метках пишутся латиницей.
- cyrillic: прижившиеся слова (цели terms.json кириллицей, «коммит», «фронтенд»); в метках остаются кириллицей.
- heldout: термины контрольного набора №1 (docs/coordinator-holdout.md) и 15% остальных по хешу; в обучение не идут.
- pilot: латинские термины anglicisms-30 (tools/eval/score-set.py) и PILOT_EXTRA обучающих по хешу.
- hints: кириллические варианты из terms.json (подсказки произношения, не готовые формы для синтеза).
"""
import ast, hashlib, json, re, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DATA = ROOT / "src" / "Vox.Core" / "Text" / "Data"
HELDOUT_SHARE = 0.15
PILOT_EXTRA = 100


def latin(s):
    return re.search(r"[A-Za-z]", s) is not None


def pure_cyrillic(s):
    return re.fullmatch(r"[а-яёА-ЯЁ \-]+", s) is not None


def bucket(term):
    return int(hashlib.sha1(term.lower().encode("utf-8")).hexdigest()[:8], 16) / 0xFFFFFFFF


def anglicisms30():
    src = (ROOT / "tools" / "eval" / "score-set.py").read_text(encoding="utf-8")
    k = re.search(r"^K = (\[.*?\])\s*$", src, re.M | re.S).group(1)
    return {t for phrase in ast.literal_eval(k) for t in phrase}


def holdout():
    terms = set()
    for line in (ROOT / "docs" / "coordinator-holdout.md").read_text(encoding="utf-8").splitlines():
        m = re.match(r"^\d+\. .* \[(.*)\]\s*$", line)
        if m:
            terms.update(t.strip() for t in m.group(1).split(","))
    return terms


terms = {}  # term -> [script, source, hints]
for section, entries in json.loads((DATA / "terms.json").read_text(encoding="utf-8")).items():
    if not isinstance(entries, dict):
        continue
    for target, variants in entries.items():
        hints = [v for v in variants if pure_cyrillic(v) and v.lower() != target.lower()]
        row = terms.setdefault(target, ["latin" if latin(target) else "cyrillic", section, []])
        row[2].extend(h for h in hints if h not in row[2])
for brand in (DATA / "brands.txt").read_text(encoding="utf-8").split():
    terms.setdefault(brand, ["latin", "brands", []])

a30, hold = anglicisms30(), holdout()
both = a30 & hold
if both:
    print(f"в обоих наборах (идут в heldout): {sorted(both)}", file=sys.stderr)
missing = sorted((a30 | hold) - terms.keys())
if missing:
    print(f"нет в словаре (добавлены как есть): {missing}", file=sys.stderr)
    for t in missing:
        terms[t] = ["latin" if latin(t) else "cyrillic", "eval", []]

held = {t for t in terms if t in hold or (t not in a30 and bucket(t) < HELDOUT_SHARE)}
train_latin = sorted((t for t, r in terms.items() if r[0] == "latin" and t not in held and t not in a30), key=bucket)
pilot = {t for t in a30 if t not in held and terms[t][0] == "latin"} | set(train_latin[:PILOT_EXTRA])

sys.stdout.reconfigure(encoding="utf-8")
print("term\tscript\tsplit\tpilot\tsource\thints")
for t in sorted(terms, key=str.lower):
    script, source, hints = terms[t]
    print(f"{t}\t{script}\t{'heldout' if t in held else 'train'}\t{int(t in pilot)}\t{source}\t{'|'.join(hints)}")
counts = {(s, h): sum(1 for t, r in terms.items() if r[0] == s and (t in held) == h) for s in ("latin", "cyrillic") for h in (False, True)}
print(f"терминов {len(terms)}: latin train {counts['latin', False]}, latin heldout {counts['latin', True]}, "
      f"cyrillic train {counts['cyrillic', False]}, cyrillic heldout {counts['cyrillic', True]}; пилот {len(pilot)}", file=sys.stderr)
