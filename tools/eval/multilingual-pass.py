"""Шаг 2, офлайн-замер: GigaAM Multilingual по записям (только у координатора, WAV в репозиторий не класть).
python tools/eval/multilingual-pass.py <папка с WAV и recordings.tsv> > ml.tsv
Вывод: Raw<TAB>Raw на каждую запись в порядке имён файлов (как в recordings.tsv) для текстового Vox.Replay.
Нужны пакет gigaam (github.com/salute-developers/GigaAM), torch CPU и ffmpeg в PATH. Модель multilingual_ctc.
"""
import os, sys, time

import gigaam
import torch

folder = sys.argv[1]
model_name = sys.argv[2] if len(sys.argv) > 2 else "multilingual_ctc"
torch.set_num_threads(3)
files = sorted(l.split("\t")[0] for l in open(os.path.join(folder, "recordings.tsv"), encoding="utf-8-sig").read().splitlines()[1:] if l.strip())
model = gigaam.load_model(model_name, device="cpu")
sys.stdout.reconfigure(encoding="utf-8")
start = time.perf_counter()
for f in files:
    path = os.path.join(folder, f if f.endswith(".wav") else f + ".wav")
    text = model.transcribe(path).text.strip().replace("\t", " ")
    print(f"{text}\t{text}")
print(f"{len(files)} записей, {(time.perf_counter() - start) / len(files):.2f} с на запись", file=sys.stderr)
