# Эксперимент: hotwords для GigaAM (22.10.2026)

Проверка идеи из `docs/adr/033-rag-rejected-hotwords-deferred.md`. Python + `sherpa-onnx==1.13.8`
(то же ядро, что NuGet `org.k2fsa.sherpa.onnx` 1.13.8 в Vox).

```bash
pip install sherpa-onnx==1.13.8 soundfile numpy
# модели: asr-models/sherpa-onnx-nemo-transducer-punct-giga-am-v3-russian-2025-12-16.tar.bz2,
#         tts-models/vits-piper-ru_RU-irina-medium.tar.bz2, vits-piper-ru_RU-dmitri-medium.tar.bz2
awk '$1!="<unk>"{print $1" "(-$2)}' $GIGAAM/tokens.txt > bpe.vocab   # ssentencepiece: piece score
python3 synth.py <папка с моделями>          # wav/ из phrases.txt двумя голосами Piper
python3 rec.py $GIGAAM greedy                 # базовая линия
python3 rec.py $GIGAAM beam                   # modified_beam_search без подсказок
PATHS=2 python3 rec.py $GIGAAM hot 1.0        # с hotwords.txt, score 1.0
```

## Результат

- **Поддержка есть.** В 1.13.8 `OfflineRecognizerTransducerNeMoImpl` умеет `modified_beam_search` и
  hotwords (`hotwords_file`, `hotwords_score`, `modeling_unit="bpe"`, `bpe_vocab`). В C# это
  `OfflineRecognizerConfig.HotwordsFile/HotwordsScore`, `ModelConfig.ModelingUnit/BpeVocab`.
- **Токены GigaAM e2e:** 1025 BPE-пьес SentencePiece (`▁` в начале слова), 85 с латиницей
  (`▁S`, `▁C`, `an`, `er`…), поэтому подсказки латиницей кодируются. `bpe.vocab` в архиве модели нет —
  собирается из `tokens.txt` (порядок пьес = приоритет слияния, score = −id).
- **Скорость** (4 потока, CPU облака; GigaAM int8):

  | Аудио | greedy | beam (4 пути) | beam (2 пути) | beam 2 + hotwords 1.0 |
  |---|---|---|---|---|
  | 5 с | 372 мс | 578 мс (+55%) | 529 мс (+42%) | 567 мс (+52%) |
  | 15 с | 934 мс | 1678 мс (+80%) | 1547 мс (+66%) | 1572 мс (+68%) |
  | 45 с | 2775 мс | 5019 мс (+81%) | 4261 мс (+54%) | 3794 мс (+37%) |

  Порог владельца +30% не проходит ни один вариант.
- **Качество** (28 фраз, 2 синтетических голоса, 8 с терминами, 6 обычных):
  - score 1.5–3.0: появляются термины (DeepSeek, GitHub, MCP LLM, GigaAM), но **ломается текст** —
    обрыв фразы на подсказке («Открой Remotio», «Сделай ролик в Remotio», «Проект работает на Giga»)
    и вставки («Qweba Ope сыра» вместо «Купи хлеба, молока и немного сыра»). Неприемлемо.
  - score 0.75–1.0: обрывов нет, обычные фразы не испорчены, но выигрыш мал: «гетab» → GitHub,
    «Chat GPT» → ChatGPT, «MCLM» → «MCPLM»; остальное — то, что и так чинит `terms.json`
    («Дипсик» → DeepSeek, «клодкод» → Claude Code).
- Ограничение: TTS-голоса не равны живой речи; по живым записям владельца выводы могут сдвинуться.

## Вывод

Не включать сейчас: +40–70% времени распознавания ради единичных исправлений, которые словарь в
основном уже делает, а при заметном эффекте (score ≥ 1.5) hotwords портят текст. Харнесс готов —
если владелец даст 10–20 живых WAV с терминами, прогнать `rec.py` на них (положить в `wav/` и
`wav/index.json`).
