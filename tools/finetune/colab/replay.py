"""Обычная русская речь против забывания: клипы Russian LibriSpeech (CC-BY-4.0) других дикторов, не из refs.
python replay.py <папка работы> [--hours 15]
Пишет <папка>/replay/*.wav (16 кГц) и <папка>/replay.tsv (path, duration). Метки ставит label.py
исходной моделью v3, чтобы стиль вывода (регистр, пунктуация, числа) не уехал.
"""
import argparse, io, os

import numpy as np
import soundfile as sf
from datasets import Audio, load_dataset

p = argparse.ArgumentParser()
p.add_argument("work")
p.add_argument("--hours", type=float, default=15)
a = p.parse_args()

tsv_path = os.path.join(a.work, "replay.tsv")
if os.path.exists(tsv_path):
    print("replay.tsv уже готов")
    raise SystemExit
refs = {l.split("\t")[2].strip() for l in open(os.path.join(a.work, "refs.tsv"), encoding="utf-8")}
os.makedirs(os.path.join(a.work, "replay"), exist_ok=True)

ds = load_dataset("istupakov/russian_librispeech", split="train", streaming=True).cast_column("audio", Audio(decode=False))
ds = ds.shuffle(seed=7, buffer_size=2000)
rows, total = [], 0.0
for n, item in enumerate(ds):
    if item["audio_filepath"].split("/")[1] in refs or not 2 <= item["duration"] <= 19.5:
        continue
    pcm, rate = sf.read(io.BytesIO(item["audio"]["bytes"]), dtype="float32")
    if pcm.ndim > 1:
        pcm = pcm.mean(axis=1)
    if rate != 16000:
        pcm = np.interp(np.arange(0, len(pcm), rate / 16000), np.arange(len(pcm)), pcm).astype(np.float32)
    path = os.path.join("replay", f"{len(rows):05d}.wav")
    sf.write(os.path.join(a.work, path), pcm, 16000, subtype="PCM_16")
    rows.append((path, len(pcm) / 16000))
    total += len(pcm) / 16000
    if total >= a.hours * 3600:
        break

with open(tsv_path, "w", encoding="utf-8") as f:
    f.write("path\tduration\n")
    for path, d in rows:
        f.write(f"{path}\t{d:.2f}\n")
print(f"клипов {len(rows)}, {total / 3600:.1f} ч")
