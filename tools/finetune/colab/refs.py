"""Образцы голосов для синтеза: клипы 4–11 с из SOVA RuDevices (CC-BY-4.0, часть validation).
python refs.py <папка вывода> [--speakers 150]
SOVA — записи тысяч людей на разные устройства, поэтому разные клипы почти всегда разные голоса
(в Russian LibriSpeech всего несколько чтецов). Пишет <папка>/refs/*.wav (16 кГц) и <папка>/refs.tsv (path, text).
"""
import argparse, io, os, sys

import soundfile as sf
from datasets import Audio, load_dataset

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from audio import to16k  # noqa: E402

p = argparse.ArgumentParser()
p.add_argument("out")
p.add_argument("--speakers", type=int, default=150)
a = p.parse_args()

os.makedirs(os.path.join(a.out, "refs"), exist_ok=True)
tsv = os.path.join(a.out, "refs.tsv")
if os.path.exists(tsv) and sum(1 for _ in open(tsv, encoding="utf-8")) > a.speakers:
    print("refs.tsv уже готов")
    raise SystemExit

ds = load_dataset("bond005/sova_rudevices", split="validation", streaming=True).cast_column("audio", Audio(decode=False))
rows = []
for item in ds.shuffle(seed=3, buffer_size=1000):
    text = item["transcription"].strip()
    pcm = to16k(*sf.read(io.BytesIO(item["audio"]["bytes"]), dtype="float32"))
    if not text or not 4 <= len(pcm) / 16000 <= 11:
        continue
    path = f"refs/{len(rows):03d}.wav"
    sf.write(os.path.join(a.out, path), pcm, 16000, subtype="PCM_16")
    rows.append((path, text))
    if len(rows) >= a.speakers:
        break

with open(tsv, "w", encoding="utf-8") as f:
    f.write("path\ttext\n" + "".join(f"{path}\t{text}\n" for path, text in rows))
print(f"голосов: {len(rows)}")
