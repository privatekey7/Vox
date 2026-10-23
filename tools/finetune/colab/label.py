"""Разметка исходной GigaAM v3 и манифесты для train_utils/train.py.
python label.py <папка работы> [--val 0.05]
- replay: метка = вывод исходной v3 (самодистилляция, стиль не уезжает);
- synth: вывод v3 + целевое написание терминов (tools/finetune/labels.py), плохой синтез отбрасывается.
Пишет <папка>/train.tsv и <папка>/val.tsv (path, duration, transcription; пути абсолютные).
"""
import argparse, json, os, sys, zlib

import gigaam
import torch

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from labels import merge  # noqa: E402

p = argparse.ArgumentParser()
p.add_argument("work")
p.add_argument("--val", type=float, default=0.05)
a = p.parse_args()

model = gigaam.load_model("v3_e2e_rnnt", device="cuda" if torch.cuda.is_available() else "cpu")
cache_path = os.path.join(a.work, "v3-hyp.tsv")  # вывод v3 по клипам, чтобы не считать заново после обрыва
cache = dict(l.rstrip("\n").split("\t", 1) for l in open(cache_path, encoding="utf-8")) if os.path.exists(cache_path) else {}


def hyp(path):
    if path not in cache:
        cache[path] = model.transcribe(os.path.join(a.work, path)).text.strip().replace("\t", " ")
        with open(cache_path, "a", encoding="utf-8") as f:
            f.write(f"{path}\t{cache[path]}\n")
    return cache[path]


def rows(name):
    lines = open(os.path.join(a.work, name), encoding="utf-8").read().splitlines()
    head = lines[0].split("\t")
    return [dict(zip(head, l.split("\t"))) for l in lines[1:] if l]


out = []  # (path, duration, label)
for r in rows("replay.tsv"):
    text = hyp(r["path"])
    if text:
        out.append((r["path"], r["duration"], text))
replay_count, dropped = len(out), 0
for r in rows("synth.tsv"):
    label = merge(hyp(r["path"]), r["text"], json.loads(r["terms"]))
    if label is None:
        dropped += 1
    else:
        out.append((r["path"], r["duration"], label))

train, val = [], []
for path, duration, label in out:
    (val if zlib.crc32(path.encode()) % 1000 < a.val * 1000 else train).append(f"{os.path.join(a.work, path)}\t{duration}\t{label}")
if not val:  # train.py не запускается с пустой проверочной частью
    val.append(train.pop())
for name, items in (("train.tsv", train), ("val.tsv", val)):
    with open(os.path.join(a.work, name), "w", encoding="utf-8") as f:
        f.write("path\tduration\ttranscription\n" + "\n".join(items) + "\n")
print(f"русских {replay_count}, синтеза {len(out) - replay_count} (отброшено {dropped}); train {len(train)}, val {len(val)}")
