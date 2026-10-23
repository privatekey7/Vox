"""Синтез фраз ESpeech-TTS-1 (F5-TTS, Apache-2.0) голосами из refs.tsv. Продолжает с места после обрыва.
python synth.py <phrases.jsonl> <папка работы> [--nfe 32] [--limit N]
В синтез идёт spoken (кириллица, термины как их произносят), в synth.tsv — text для разметки.
Пишет <папка>/synth/NNNNN.wav (16 кГц) и <папка>/synth.tsv (path, duration, spoken, text, terms).
Половине клипов добавляется слабый шум (SNR 25–40 дБ), скорость речи 0,9–1,1.
"""
import argparse, json, os, random, time

import numpy as np
import soundfile as sf
import torch
import torchaudio


def _load(path, *args, **kwargs):
    # новый torchaudio читает через torchcodec, которому нужны библиотеки FFmpeg; WAV хватает soundfile
    data, rate = sf.read(path, dtype="float32", always_2d=True)
    return torch.from_numpy(data.T.copy()), rate


torchaudio.load = _load

import sys  # noqa: E402

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from audio import to16k  # noqa: E402
from huggingface_hub import hf_hub_download  # noqa: E402
from ruaccent import RUAccent
from f5_tts.infer.utils_infer import infer_process, load_model, load_vocoder, preprocess_ref_audio_text
from f5_tts.model import DiT

p = argparse.ArgumentParser()
p.add_argument("phrases")
p.add_argument("work")
p.add_argument("--nfe", type=int, default=32)
p.add_argument("--limit", type=int, default=None)
a = p.parse_args()

phrases = [json.loads(l) for l in open(a.phrases, encoding="utf-8") if l.strip()][: a.limit]
refs = [l.rstrip("\n").split("\t") for l in open(os.path.join(a.work, "refs.tsv"), encoding="utf-8")][1:]
os.makedirs(os.path.join(a.work, "synth"), exist_ok=True)
tsv_path = os.path.join(a.work, "synth.tsv")
done = set()
if os.path.exists(tsv_path):
    done = {l.split("\t")[0] for l in open(tsv_path, encoding="utf-8")}
else:
    open(tsv_path, "w", encoding="utf-8").write("path\tduration\tspoken\ttext\tterms\n")

repo = "ESpeech/ESpeech-TTS-1_RL-V2"
model = load_model(DiT, dict(dim=1024, depth=22, heads=16, ff_mult=2, text_dim=512, conv_layers=4),
                   hf_hub_download(repo, "espeech_tts_rlv2.pt"), vocab_file=hf_hub_download(repo, "vocab.txt"))
vocoder = load_vocoder()
accent = RUAccent()
accent.load(omograph_model_size="turbo3.1", use_dictionary=True, tiny_mode=False)
prepared = {}  # образец голоса → (аудио, текст) после preprocess_ref_audio_text


def stress(text):
    # RUAccent иногда падает на отдельном слове (onnx-модель ждёт token_type_ids); без ударений F5 тоже читает
    try:
        return accent.process_all(text)
    except Exception as e:  # noqa: BLE001
        print(f"без ударений ({type(e).__name__}): {text[:60]}", flush=True)
        return text

start, made = time.perf_counter(), 0
with open(tsv_path, "a", encoding="utf-8") as tsv:
    for i, ph in enumerate(phrases):
        path = os.path.join("synth", f"{i:05d}.wav")
        if path in done:
            continue
        rnd = random.Random(i)
        ref_path, ref_text = refs[rnd.randrange(len(refs))][:2]
        if ref_path not in prepared:
            prepared[ref_path] = preprocess_ref_audio_text(os.path.join(a.work, ref_path), stress(ref_text))
        ref_audio, ref_txt = prepared[ref_path]
        wav, sr, _ = infer_process(ref_audio, ref_txt, stress(ph["spoken"]), model, vocoder,
                                   nfe_step=a.nfe, speed=rnd.uniform(0.9, 1.1), show_info=lambda *_: None)
        wav = to16k(wav, sr)
        if rnd.random() < 0.5:
            snr = rnd.uniform(25, 40)
            noise = np.random.default_rng(i).standard_normal(len(wav)).astype(np.float32)
            wav = wav + noise * np.sqrt(np.mean(wav ** 2) / 10 ** (snr / 10))
        wav = np.clip(wav, -1, 1)
        sf.write(os.path.join(a.work, path), wav, 16000, subtype="PCM_16")
        tsv.write(f"{path}\t{len(wav) / 16000:.2f}\t{ph['spoken']}\t{ph['text']}\t{json.dumps(ph['terms'], ensure_ascii=False)}\n")
        tsv.flush()
        made += 1
        if made % 50 == 0:
            print(f"{i + 1}/{len(phrases)}, {(time.perf_counter() - start) / made:.2f} с на фразу", flush=True)
print(f"готово: синтезировано {made}, всего {len(phrases)}")
