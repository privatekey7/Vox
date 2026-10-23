"""Обычная русская речь против забывания: поровну Russian LibriSpeech (чтение книг) и SOVA RuDevices
(разговорные фразы на разных устройствах, часть train; голоса для синтеза взяты из validation). Обе CC-BY-4.0.
python replay.py <папка работы> [--hours 6]
Пишет <папка>/replay/*.wav (16 кГц) и <папка>/replay.tsv (path, duration). Метки ставит label.py
исходной моделью v3, чтобы стиль вывода (регистр, пунктуация, числа) не уехал.
"""
import argparse, io, os, sys

import soundfile as sf
from datasets import Audio, load_dataset

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from audio import to16k  # noqa: E402

p = argparse.ArgumentParser()
p.add_argument("work")
p.add_argument("--hours", type=float, default=6)
a = p.parse_args()

tsv_path = os.path.join(a.work, "replay.tsv")
if os.path.exists(tsv_path):
    print("replay.tsv уже готов")
    raise SystemExit
os.makedirs(os.path.join(a.work, "replay"), exist_ok=True)

rows = []
for name in ("istupakov/russian_librispeech", "bond005/sova_rudevices"):
    ds = load_dataset(name, split="train", streaming=True).cast_column("audio", Audio(decode=False))
    total = 0.0
    for item in ds.shuffle(seed=7, buffer_size=2000):
        pcm = to16k(*sf.read(io.BytesIO(item["audio"]["bytes"]), dtype="float32"))
        duration = len(pcm) / 16000
        if not 1.5 <= duration <= 19.5:
            continue
        path = f"replay/{len(rows):05d}.wav"
        sf.write(os.path.join(a.work, path), pcm, 16000, subtype="PCM_16")
        rows.append((path, duration))
        total += duration
        if total >= a.hours / 2 * 3600:
            break
    print(f"{name}: {total / 3600:.1f} ч")

with open(tsv_path, "w", encoding="utf-8") as f:
    f.write("path\tduration\n" + "".join(f"{path}\t{d:.2f}\n" for path, d in rows))
print(f"клипов {len(rows)}")
