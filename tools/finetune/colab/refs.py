"""Образцы голосов для синтеза: по одному клипу 4–11 с от разных дикторов Russian LibriSpeech (CC-BY-4.0).
python refs.py <папка вывода> [--speakers 150]
Пишет <папка>/refs/*.wav (16 кГц) и <папка>/refs.tsv (path, text, speaker). Дикторы берутся из пути
audio/<диктор>/<книга>/…; список дикторов нужен replay.py, чтобы не брать их же голоса для русских данных.
"""
import argparse, io, os

import numpy as np
import soundfile as sf
from datasets import Audio, load_dataset

p = argparse.ArgumentParser()
p.add_argument("out")
p.add_argument("--speakers", type=int, default=150)
a = p.parse_args()

os.makedirs(os.path.join(a.out, "refs"), exist_ok=True)
tsv = os.path.join(a.out, "refs.tsv")
if os.path.exists(tsv) and sum(1 for _ in open(tsv, encoding="utf-8")) > a.speakers:
    print("refs.tsv уже готов")
    raise SystemExit

ds = load_dataset("istupakov/russian_librispeech", split="train", streaming=True).cast_column("audio", Audio(decode=False))
seen, rows = set(), []
for item in ds:
    speaker = item["audio_filepath"].split("/")[1]
    if speaker in seen or not 4 <= item["duration"] <= 11:
        continue
    pcm, rate = sf.read(io.BytesIO(item["audio"]["bytes"]), dtype="float32")
    if pcm.ndim > 1:
        pcm = pcm.mean(axis=1)
    if rate != 16000:
        pcm = np.interp(np.arange(0, len(pcm), rate / 16000), np.arange(len(pcm)), pcm).astype(np.float32)
    path = os.path.join("refs", f"{speaker}.wav")
    sf.write(os.path.join(a.out, path), pcm, 16000)
    rows.append((path, item["text_no_preprocessing"].strip(), speaker))
    seen.add(speaker)
    if len(rows) >= a.speakers:
        break

with open(tsv, "w", encoding="utf-8") as f:
    f.write("path\ttext\tspeaker\n")
    for r in rows:
        f.write("\t".join(r) + "\n")
print(f"голосов: {len(rows)}")
