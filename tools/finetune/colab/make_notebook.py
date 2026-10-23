"""Собирает tools/finetune/colab/pilot.ipynb (Colab, бесплатная T4). python make_notebook.py"""
import json
from pathlib import Path

cells = [
    ("markdown", """# Vox: пилот дообучения GigaAM v3 на англицизмах

План: `docs/plan-gigaam-finetune.md`. Среда выполнения → Сменить среду выполнения → **Графический процессор T4**.
Запускать ячейки по порядку. Всё складывается на Google Диск в `vox-finetune/pilot`: если сессия оборвалась,
подключитесь заново и запустите ячейки ещё раз, каждый шаг продолжит с места.
Сюда не загружаются записи и тексты пользователя: только синтетические фразы и открытый корпус Russian LibriSpeech.
Место на Диске: ~3 ГБ данных + чекпойнты по ~2,7 ГБ (хранятся два лучших), всего ~8–9 ГБ."""),
    ("code", """from google.colab import drive
drive.mount('/content/drive')
WORK = '/content/drive/MyDrive/vox-finetune/pilot'
LOCAL = '/content/work'
import os; os.makedirs(WORK, exist_ok=True)
!nvidia-smi --query-gpu=name,memory.total --format=csv"""),
    ("markdown", "## 1. Код и зависимости (~3–5 мин)"),
    ("code", """!rm -rf /content/Vox && git clone -q -b csharp --depth 1 https://github.com/privatekey7/Vox /content/Vox
![ -d /content/GigaAM ] || git clone -q --depth 1 https://github.com/salute-developers/GigaAM /content/GigaAM
!pip install -q -e "/content/GigaAM[train]" f5-tts ruaccent soundfile
FT = '/content/Vox/tools/finetune'"""),
    ("code", """# проверка: установка не должна была заменить CUDA-версию torch на CPU-версию
import torch, torchaudio
print(torch.__version__, torchaudio.__version__, 'CUDA:', torch.cuda.is_available(), torch.version.cuda)
assert torch.cuda.is_available(), 'нет GPU: Среда выполнения → Сменить среду выполнения → T4'"""),
    ("markdown", "## 2. Голоса: 150 дикторов Russian LibriSpeech (~5 мин)"),
    ("code", "!python {FT}/colab/refs.py {WORK} --speakers 150"),
    ("markdown", "## 3. Синтез 2870 фраз (ESpeech, ~1–2 ч на T4; после обрыва продолжает с места)"),
    ("code", "!python {FT}/colab/synth.py {FT}/data/pilot/phrases.jsonl {WORK}"),
    ("markdown", "## 4. Обычная русская речь против забывания, 6 ч (синтеза ~2,4 ч, то есть ~30%; ~10 мин)"),
    ("code", "!python {FT}/colab/replay.py {WORK} --hours 6"),
    ("markdown", "## 5. Копия на локальный диск и разметка исходной v3 (~15 мин)"),
    ("code", """!mkdir -p {LOCAL} && rsync -a --exclude ckpt {WORK}/ {LOCAL}/
!python {FT}/colab/label.py {LOCAL}
!cp {LOCAL}/v3-hyp.tsv {WORK}/"""),
    ("markdown", """## 6а. Проба: 20 шагов в fp16 (~3 мин)
`val/wer` на первом шаге должен быть маленьким (метки синтеза почти совпадают с выводом v3), loss конечным.
Если loss `nan` или `inf`, в обеих ячейках обучения заменить `--precision 16-mixed` на `--precision 32` (медленнее в ~2 раза)."""),
    ("code", """!python {FT}/colab/train.py /content/GigaAM --model_name v3_e2e_rnnt \\
  --train_manifest {LOCAL}/train.tsv --val_manifest {LOCAL}/val.tsv --output_dir /content/probe --exp_name probe \\
  --max_steps 20 --val_check_steps 20 --val_first_batches 20 --lr 2e-5 \\
  --batch_size 8 --rnnt_subbatch_size 2 --eval_batch_size 16 --precision 16-mixed --activation_checkpointing --num_workers 2"""),
    ("markdown", "## 6. Обучение: 1 эпоха с замороженным энкодером, потом 2 полные (~1 ч)"),
    ("code", """# после обрыва: добавить --resume_from_checkpoint <последний .ckpt из {WORK}/ckpt>
!python {FT}/colab/train.py /content/GigaAM --model_name v3_e2e_rnnt \\
  --train_manifest {LOCAL}/train.tsv --val_manifest {LOCAL}/val.tsv \\
  --output_dir {WORK}/ckpt --exp_name pilot \\
  --max_epochs 3 --freeze_encoder_epochs 1 --lr 2e-5 \\
  --batch_size 8 --accumulate_grad_batches 2 --rnnt_subbatch_size 2 --eval_batch_size 16 \\
  --precision 16-mixed --activation_checkpointing --val_check_interval 0.5 --num_workers 2"""),
    ("markdown", """## 7. Результат
Чекпойнты лежат на Google Диске в `vox-finetune/pilot/ckpt`. Лучший (наименьший val_wer) скачать на компьютер
в `D:\\Soft\\vox2\\.holdout\\ckpt\\`: дальше экспорт и проверка на живых записях делаются локально."""),
    ("code", "!ls -la {WORK}/ckpt/*"),
]

nb = {
    "nbformat": 4, "nbformat_minor": 0,
    "metadata": {"accelerator": "GPU", "colab": {"provenance": [], "gpuType": "T4"},
                 "kernelspec": {"name": "python3", "display_name": "Python 3"}},
    "cells": [
        {"cell_type": kind, "metadata": {}, "source": src.splitlines(keepends=True),
         **({"execution_count": None, "outputs": []} if kind == "code" else {})}
        for kind, src in cells
    ],
}
out = Path(__file__).with_name("pilot.ipynb")
out.write_text(json.dumps(nb, ensure_ascii=False, indent=1), encoding="utf-8")
print(out)
