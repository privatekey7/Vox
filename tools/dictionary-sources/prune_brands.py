#!/usr/bin/env python3
"""Убирает из brands.txt бренды, которые заменяют слова контрольных корпусов (tools/eval/make-corpora.py).

Использование: python tools/dictionary-sources/prune_brands.py <папка корпусов> [файл ...]
По умолчанию корпуса rare.txt (редкие английские слова), ru_rare.txt (редкие русские слова) и freq.txt (частые
английские). Каждая замена на бренд из brands.txt в строках `[FUZZY] "слово" → "Бренд"` (stderr Vox.Replay)
выбрасывает этот бренд; пересборка и повтор, пока замен на бренды не останется. Выброшенные имена
записываются в pruned-brands.tsv (имя, слово корпуса, которое оно заменяло). Правило общее: бренд, который
хоть раз принял обычное слово за себя, в словарь не идёт.
"""
import os, re, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
BRANDS = os.path.join(ROOT, "src/Vox.Core/Text/Data/brands.txt")
folder = sys.argv[1]
files = sys.argv[2:] or ["rare.txt", "ru_rare.txt", "freq.txt"]
dll = os.path.join(ROOT, "tools/Vox.Replay/bin/Release/net10.0/Vox.Replay.dll")
pruned = []
while True:
    subprocess.run(["dotnet", "build", os.path.join(ROOT, "tools/Vox.Replay"), "-c", "Release", "-v", "q", "--nologo"],
                   capture_output=True, check=True)
    brands = [l.strip() for l in open(BRANDS, encoding="utf8") if l.strip()]
    names = set(brands)
    hit = {}
    for f in files:
        p = subprocess.run(["dotnet", dll, os.path.join(folder, f)], capture_output=True, encoding="utf8")
        for m in re.finditer(r'\[(?:FUZZY|EN)\] "(.*?)" → "(.*?)" [\d.]+', p.stderr):
            if m.group(2) in names:
                hit.setdefault(m.group(2), m.group(1))
    if not hit:
        break
    pruned += sorted(hit.items())
    with open(BRANDS, "w", encoding="utf8", newline="\n") as out:
        out.write("".join(b + "\n" for b in brands if b not in hit))
    print("pruned", sorted(hit))
with open(os.path.join(HERE, "pruned-brands.tsv"), "w", encoding="utf8", newline="\n") as out:
    out.write("".join(f"{n}\t{w}\n" for n, w in pruned))
selected = os.path.join(HERE, "selected-brands.tsv")
if os.path.exists(selected):
    gone = {n for n, _ in pruned}
    rows = [l for l in open(selected, encoding="utf8") if l.split("	")[0] not in gone]
    open(selected, "w", encoding="utf8", newline="
").write("".join(rows))
print("brands left", len([l for l in open(BRANDS, encoding="utf8") if l.strip()]))
