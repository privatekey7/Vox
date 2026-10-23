"""Офлайн-замер Whisper (faster-whisper, CTranslate2) по записям (только у координатора, WAV в репозиторий не класть).
python tools/eval/whisper-pass.py <папка с WAV и recordings.tsv> <папка модели CT2> [--beam N] [--threads N] [--prompt "текст"] > out.tsv
Вывод: Raw<TAB>Raw на каждую запись в порядке имён файлов для текстового Vox.Replay; скорость в stderr.
Проверено на coriollon/whisper-large-v3-turbo-russian-codeswitch (ct2_int8_float16), CPU int8.
"""
import argparse, os, sys, time, wave

import numpy as np
from faster_whisper import WhisperModel


def load(path):
    # WAV без PyAV (новый av несовместим с faster-whisper): PCM16 → float32, моно, 16 кГц
    with wave.open(path) as w:
        rate, channels = w.getframerate(), w.getnchannels()
        pcm = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float32) / 32768
    pcm = pcm.reshape(-1, channels).mean(axis=1)
    if rate != 16000:
        pcm = np.interp(np.arange(0, len(pcm), rate / 16000), np.arange(len(pcm)), pcm).astype(np.float32)
    return pcm

p = argparse.ArgumentParser()
p.add_argument("folder")
p.add_argument("model")
p.add_argument("--beam", type=int, default=5)
p.add_argument("--threads", type=int, default=6)
p.add_argument("--prompt", default=None)
a = p.parse_args()

files = sorted(l.split("\t")[0] for l in open(os.path.join(a.folder, "recordings.tsv"), encoding="utf-8-sig").read().splitlines()[1:] if l.strip())
model = WhisperModel(a.model, device="cpu", compute_type="int8", cpu_threads=a.threads)
sys.stdout.reconfigure(encoding="utf-8")
times = []
for f in files:
    path = os.path.join(a.folder, f if f.endswith(".wav") else f + ".wav")
    audio = load(path)
    start = time.perf_counter()
    segments, _ = model.transcribe(audio, language="ru", beam_size=a.beam, initial_prompt=a.prompt, vad_filter=False, condition_on_previous_text=False)
    text = " ".join(s.text.strip() for s in segments).replace("\t", " ")
    times.append(time.perf_counter() - start)
    print(f"{text}\t{text}")
times.sort()
print(f"{len(files)} записей: среднее {sum(times) / len(times):.2f} с, медиана {times[len(times) // 2]:.2f} с, максимум {times[-1]:.2f} с", file=sys.stderr)
