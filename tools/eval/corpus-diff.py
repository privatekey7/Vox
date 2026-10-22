#!/usr/bin/env python3
"""Прогон корпусов (make-corpora.py) через Vox.Replay на базовой и новой сборке, отличия построчно.

Использование: python tools/eval/corpus-diff.py <Replay.dll базы> <Replay.dll новой> <папка> [--show N] [--pairs]
Для каждого корпуса и обеих моделей: сколько строк меняет база, сколько новая, и строки, где результат
новой отличается от базы, в трёх классах:
  added       база строку не меняла, новая меняет (новые замены: должно быть 0 или каждая разобрана);
  removed     база меняла, новая оставляет (убранные ложные замены или потерянные верные);
  retargeted  обе меняют, но по-разному.
--pairs: вместо целых строк только пары «слово базы → слово новой» (для личных текстов: история владельца).
Нужен .NET 10.
"""
import difflib, os, subprocess, sys

base, new, folder = sys.argv[1:4]
show = int(sys.argv[sys.argv.index("--show") + 1]) if "--show" in sys.argv else 40
pairs = "--pairs" in sys.argv
models = ["gigaam", "parakeet"]


def run(dll, model, path):
    p = subprocess.run(["dotnet", dll, "--model", model, path], capture_output=True, encoding="utf8")
    return [l.split("\t") for l in p.stdout.split("\n") if l]


def tokens(a, b):
    x, y = a.split(), b.split()
    out = []
    for tag, i1, i2, j1, j2 in difflib.SequenceMatcher(None, x, y, autojunk=False).get_opcodes():
        if tag != "equal":
            out.append(f"{' '.join(x[i1:i2])} → {' '.join(y[j1:j2])}")
    return "; ".join(out)


counts = {"added": 0, "removed": 0, "retargeted": 0}
for name in sorted(os.listdir(folder)):
    path = os.path.join(folder, name)
    for model in models if name in ("docs.txt", "history.txt") else models[:1]:
        a, b = run(base, model, path), run(new, model, path)
        assert len(a) == len(b), (name, len(a), len(b))
        changed = lambda r: r[0].strip() != r[1].strip()
        found = {"added": [], "removed": [], "retargeted": []}
        for x, y in zip(a, b):
            if x[1] == y[1]:
                continue
            kind = "added" if not changed(x) else "removed" if not changed(y) else "retargeted"
            found[kind].append((x, y))
        for k, v in found.items():
            counts[k] += len(v)
        print(f"{name} [{model}]: lines {len(a)}, changed base {sum(map(changed, a))}, new {sum(map(changed, b))}; "
              + ", ".join(f"{k} {len(v)}" for k, v in found.items()))
        for kind, items in found.items():
            for x, y in items[:show]:
                what = tokens(x[1], y[1]) if pairs else f"{x[0][:110]!r} base→ {x[1][:60]!r} new→ {y[1][:60]!r}"
                print(f"   {kind}: {what}")
print("TOTAL", counts)
