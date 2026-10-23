"""Запуск официального GigaAM train_utils/train.py с чтением WAV через soundfile.
python train.py <папка GigaAM> <аргументы train.py ...>
Новый torchaudio.load требует torchcodec и библиотеки FFmpeg; для 16-битных WAV хватает soundfile.
"""
import os, runpy, sys

import soundfile as sf
import torch
import torchaudio


def _load(path, *args, **kwargs):
    data, rate = sf.read(path, dtype="float32", always_2d=True)
    return torch.from_numpy(data.T.copy()), rate


torchaudio.load = _load

train_utils = os.path.join(os.path.abspath(sys.argv[1]), "train_utils")
sys.path.insert(0, train_utils)
os.chdir(train_utils)
sys.argv = [os.path.join(train_utils, "train.py")] + sys.argv[2:]
runpy.run_path(sys.argv[0], run_name="__main__")
