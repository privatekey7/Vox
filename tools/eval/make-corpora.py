#!/usr/bin/env python3
"""Корпуса для проверки ложных замен: одна строка = один Raw для Vox.Replay.

Использование: python tools/eval/make-corpora.py <выходная папка> [--ru ru_full.txt] [--scowl <scowl>/final]
Создаёт:
  freq.txt     частые английские слова SCOWL (уровень <= 20), строчные и с заглавной
  rare.txt     5000 случайных слов SCOWL-95 (из --scowl), которых нет в словаре Vox (seed 7)
  docs.txt     строки доков и кода репозитория (без terms.json и закрытого контрольного набора)
  ru_rare.txt  (если дан --ru, файл hermitdave/FrequencyWords content/2018/ru/ru_full.txt) 20000 русских слов, не из первых 5000
               по частоте, >= 5 букв (seed 7)
Сравнение: Replay на базовом коммите и на новом, затем `diff` двух результатов (или corpus-diff.py).
"""
import gzip, os, random, re, subprocess, sys

root = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
out = sys.argv[1]
opt = lambda name: sys.argv[sys.argv.index(name) + 1] if name in sys.argv else None
os.makedirs(out, exist_ok=True)

words = []  # (word, level)
level = 0
with gzip.open(os.path.join(root, "src/Vox.Core/Text/Data/english.txt.gz"), "rt", encoding="utf8") as f:
    for line in f:
        line = line.rstrip("\n")
        if line.startswith("#"):
            level = int(line[1:])
        elif line:
            words.append((line, level))

freq = [w for w, l in words if l <= 20]
with open(os.path.join(out, "freq.txt"), "w", encoding="utf8") as f:
    for w in freq:
        f.write(w + "\n")
        f.write(w[0].upper() + w[1:] + "\n")

known = {w for w, _ in words}
rare = set()
if opt("--scowl"):
    for name in os.listdir(opt("--scowl")):
        if name.endswith(".95") and ("words" in name or "proper-names" in name):
            for w in open(os.path.join(opt("--scowl"), name), encoding="latin-1").read().splitlines():
                w = w.strip().lower()
                if w.isalpha() and w.isascii() and w not in known:
                    rare.add(w)
rare = sorted(rare)
random.Random(7).shuffle(rare)
with open(os.path.join(out, "rare.txt"), "w", encoding="utf8") as f:
    f.write("\n".join(rare[:5000]) + "\n")

skip = {"docs/coordinator-holdout.md", "src/Vox.Core/Text/Data/terms.json", "src/Vox.Core/Text/Data/no-declension.json"}
tracked = subprocess.run(["git", "ls-files"], cwd=root, capture_output=True, text=True, encoding="utf8").stdout.split("\n")
exts = (".md", ".cs", ".py", ".sh", ".yml", ".yaml", ".iss", ".csproj", ".xaml", ".txt", ".json")
lines = []
for path in tracked:
    if not path.endswith(exts) or path in skip or "/bin/" in path or "/obj/" in path:
        continue
    try:
        text = open(os.path.join(root, path), encoding="utf8").read()
    except (OSError, UnicodeDecodeError):
        continue
    for line in text.split("\n"):
        line = line.strip()
        if 15 <= len(line) <= 300 and "\t" not in line and "[STT]" not in line:  # иначе Replay примет файл за vox.log
            lines.append(line)
with open(os.path.join(out, "docs.txt"), "w", encoding="utf8") as f:
    f.write("\n".join(lines) + "\n")

if opt("--ru"):
    ru = []
    for i, line in enumerate(open(opt("--ru"), encoding="utf8")):
        w = line.split(" ")[0]
        if i >= 5000 and len(w) >= 5 and re.fullmatch(r"[а-яё]+", w):
            ru.append(w)
    random.Random(7).shuffle(ru)
    with open(os.path.join(out, "ru_rare.txt"), "w", encoding="utf8") as f:
        f.write("\n".join(ru[:20000]) + "\n")
print("ok", {n: sum(1 for _ in open(os.path.join(out, n), encoding="utf8")) for n in os.listdir(out)})
