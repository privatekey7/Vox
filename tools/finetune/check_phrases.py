"""Проверка и сборка сгенерированных фраз (docs/plan-gigaam-finetune.md).
python tools/finetune/check_phrases.py tools/finetune/data/pilot/phrases-*.jsonl > tools/finetune/data/pilot/phrases.jsonl

Отбрасывает фразу, если: битый JSON; термина нет в text; в spoken есть латиница; в text или spoken есть
отложенный (heldout) термин или его кириллический вариант; прижившееся кириллическое слово записано латиницей.
Повторы spoken убираются. Статистика в stderr.
"""
import csv, glob, json, re, sys
from collections import Counter
from pathlib import Path

HERE = Path(__file__).resolve().parent
rows = list(csv.DictReader(open(HERE / "data" / "terms.tsv", encoding="utf-8"), delimiter="\t"))


def word_re(s):
    return re.compile(r"(?<![\w-])" + re.escape(s.lower()) + r"(?![\w-])")


held_text = [word_re(r["term"]) for r in rows if r["split"] == "heldout"]
held_spoken = [word_re(h) for r in rows if r["split"] == "heldout" for h in ([r["term"]] if r["script"] == "cyrillic" else []) + [h for h in r["hints"].split("|") if len(h) >= 4]]

files = [f for a in sys.argv[1:] for f in glob.glob(a)]
seen, kept, drops, per_term = set(), [], Counter(), Counter()
for path in files:
    for line in open(path, encoding="utf-8"):
        if not line.strip():
            continue
        try:
            p = json.loads(line)
            terms, spoken, text = p["terms"], p["spoken"].strip(), p["text"].strip()
        except (ValueError, KeyError, AttributeError):
            drops["битый JSON"] += 1
            continue
        low_text, low_spoken = text.lower(), spoken.lower()
        if not terms or any(t not in text for t in terms):
            drops["термина нет в text"] += 1
        elif re.search(r"[A-Za-z]", spoken):
            drops["латиница в spoken"] += 1
        elif any(r.search(low_text) for r in held_text) or any(r.search(low_spoken) for r in held_spoken):
            drops["отложенный термин"] += 1
        elif low_spoken in seen:
            drops["повтор"] += 1
        else:
            seen.add(low_spoken)
            kept.append({"terms": terms, "spoken": spoken, "text": text})
            per_term.update(terms)

sys.stdout.reconfigure(encoding="utf-8")
for p in kept:
    print(json.dumps(p, ensure_ascii=False))
pilot = {r["term"] for r in rows if r["pilot"] == "1"}
thin = sorted(t for t in pilot if per_term[t] < 10)
print(f"фраз {len(kept)}, отброшено {sum(drops.values())}: {dict(drops)}", file=sys.stderr)
print(f"терминов с < 10 фразами: {len(thin)} {thin[:20]}", file=sys.stderr)
