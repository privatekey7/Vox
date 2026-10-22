# Эксперимент: маленькая LLM правит только спорные слова

Не встроено в приложение. Конвейер: Raw → PostProcessor → спорные слова (`Vox.Replay --contested`,
`tools/Vox.Replay/Contested.cs`) → LLM (llama.cpp) выбирает кандидата или оставляет слово → ответ принимается,
только если все неспорные куски фразы совпали дословно, иначе фраза остаётся как была.

Спорное слово: латиница, которой нет среди терминов и английских слов (SCOWL); смесь алфавитов; кириллица вне
частотного списка, похожая по звучанию на термин (скелет FuzzyTerms от 0.9, от 5 букв). Соседние спорные слова
склеиваются («Spost Sque»). Кандидаты: 5 ближайших написаний из terms.json по звуковому скелету.

```bash
pip install llama-cpp-python==0.3.16          # CPU; собирается из исходников (нужны cmake, gcc)
tail -n +2 tools/eval/anglicisms-30.tsv | cut -f2 > giga.txt
python3 tools/llm-fix/make_corpus_b.py corpusB.txt
dotnet run --project tools/Vox.Replay -c Release -- --contested giga.txt > giga.jsonl
dotnet run --project tools/Vox.Replay -c Release -- --contested corpusB.txt > corpusB.jsonl
python3 tools/llm-fix/run.py --model модель.gguf --terms giga.jsonl --plain corpusB.jsonl --out правки.tsv
```

Без LLM (csharp на 22.10): термины 48/64; если всегда выбирать верного кандидата, 55/64 (потолок подхода).
Спорные слова есть в 11 из 30 фраз набора и в 28 из 300 обычных фраз (корпус B).
