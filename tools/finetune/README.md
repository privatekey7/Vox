# Дообучение GigaAM v3 под англицизмы

План и пороги: `docs/plan-gigaam-finetune.md`. Здесь только как запускать.

## Локально (подготовка и проверка)

| Шаг | Команда |
|---|---|
| Список терминов (train/heldout/пилот) | `python tools/finetune/make_terms.py > tools/finetune/data/terms.tsv` |
| Сборка фраз пилота (агенты пишут `phrases-N.jsonl`) | `python tools/finetune/check_phrases.py "tools/finetune/data/pilot/phrases-*.jsonl" > tools/finetune/data/pilot/phrases.jsonl` |
| Проверка сборки меток | `python tools/finetune/labels.py` |
| Экспорт чекпойнта в формат приложения | `python tools/finetune/export_sherpa.py <ckpt> <models-root>/sherpa-onnx-nemo-transducer-punct-giga-am-v3-russian-2025-12-16` |
| Проверка на живых записях | `Vox.Replay --audio <папка WAV> --models <models-root>` и `tools/eval/score-holdout.py` |

На Windows запускать Python с `PYTHONUTF8=1`: `train_utils` GigaAM читает манифест без указания кодировки.
Экспорт работает на CPU (torch, gigaam, onnx, onnxruntime); проверен на исходной модели: те же 29/66, что у
модели приложения (сырой вывод отличается в 3 фразах из 30 из-за версии torch). Сравнивать дообученную
модель нужно с этим экспортом исходной, а не с моделью приложения.

## Colab (бесплатная T4)

Открыть: <https://colab.research.google.com/github/privatekey7/Vox/blob/csharp/tools/finetune/colab/pilot.ipynb>
(блокнот собирается `colab/make_notebook.py`). Шаги: голоса (`refs.py`) → синтез (`synth.py`, ESpeech-TTS-1)
→ русская речь (`replay.py`) → разметка исходной v3 (`label.py`) → обучение (`train.py`, обёртка над
официальным `train_utils/train.py`). Всё лежит на Google Диске, каждый шаг продолжает с места после обрыва.

Источники: голоса для синтеза — SOVA RuDevices validation (тысячи людей; в Russian LibriSpeech всего несколько
чтецов), русская речь против забывания — поровну Russian LibriSpeech train и SOVA train, проверка русского —
Russian LibriSpeech test (`make_testset.py`). Все CC-BY-4.0.

## Оценка пилота (локально)

1. Скачать лучший `.ckpt` из Google Диска в `.holdout\ckpt\`, экспортировать в `<root>\sherpa-onnx-nemo-transducer-punct-giga-am-v3-russian-2025-12-16`.
2. `Vox.Replay --audio` по трём папкам (контрольный набор №1, anglicisms-30, `rutest`) для базы и новой модели.
3. `python tools/finetune/score_pilot.py --holdout BASE NEW --a30 BASE NEW --rutest BASE NEW`.

База 23.10 (экспорт исходной v3): контрольный №1 Raw 18/66, Final 29/66; anglicisms-30 Raw 14/64, Final 61/64
(словарь подгоняли под этот набор, смотреть Raw); русский WER 6,38% на 300 клипах.

Подводные камни (проверено на CPU 23.10):
- новый `torchaudio.load` требует torchcodec и библиотеки FFmpeg: `synth.py` и `colab/train.py` читают WAV через soundfile;
- `--raw_text` для `v3_e2e_rnnt` нельзя: стирает регистр и латиницу;
- `gigaam.load_model` в новых версиях даёт пример входа на 8 строк: `export_sherpa.py` задаёт размер 1 явно,
  иначе sherpa-onnx падает на загрузке декодера.
