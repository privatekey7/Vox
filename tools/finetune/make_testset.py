"""Проверка русского для порога пилота: 300 клипов тестовой части Russian LibriSpeech (CC-BY-4.0, дикторы не из train).
python tools/finetune/make_testset.py <папка> [--clips 300]
Пишет <папка>/*.wav и recordings.tsv в формате Vox.Replay --audio: в колонке raw — эталонная расшифровка
(человеческая, без пунктуации), чтобы tools/finetune/score_pilot.py сравнил с ней новый Raw.
"""
import argparse, io, os, sys

import soundfile as sf
from datasets import Audio, load_dataset

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "colab"))
from audio import to16k  # noqa: E402

p = argparse.ArgumentParser()
p.add_argument("out")
p.add_argument("--clips", type=int, default=300)
a = p.parse_args()

os.makedirs(a.out, exist_ok=True)
ds = load_dataset("istupakov/russian_librispeech", split="test", streaming=True).cast_column("audio", Audio(decode=False))
rows = []
for item in ds.shuffle(seed=11, buffer_size=1500):
    if not 2 <= item["duration"] <= 19.5:
        continue
    name = f"test-{len(rows):04d}.wav"
    sf.write(os.path.join(a.out, name), to16k(*sf.read(io.BytesIO(item["audio"]["bytes"]), dtype="float32")), 16000, subtype="PCM_16")
    rows.append(f"{name}\t\tgigaam\t{item['text'].strip()}\t\t")
    if len(rows) >= a.clips:
        break
with open(os.path.join(a.out, "recordings.tsv"), "w", encoding="utf-8") as f:
    f.write("file\ttime\tmodel\traw\tfinal\tnote\n" + "\n".join(rows) + "\n")
print(f"клипов {len(rows)}")
