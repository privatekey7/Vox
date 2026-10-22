# Источники для словаря терминов

Сырые списки названий, выгруженные 22.10.2026 для расширения `src/Vox.Core/Text/Data/terms.json`.
В приложение не попадают: это вход для отбора терминов, а не словарь.

| Файл | Что внутри | Откуда |
|---|---|---|
| `stackoverflow-tags-2026-10.txt` | 2500 самых популярных тегов, по одному в строке | Stack Exchange API `/2.3/tags?sort=popular&site=stackoverflow` (страницы 1–25; дальше нужен ключ API). Данные Stack Overflow — CC BY-SA 4.0 |
| `huggingface-models-2026-10.txt` | 1199 идентификаторов моделей `org/name` | Hugging Face API `/api/models`, топ-500 по `downloads`, `likes` и `trendingScore`, без повторов |

## Бренды для поиска по звучанию (23.10.2026, шаг 1)

`brands.txt` (`src/Vox.Core/Text/Data/`): имена, по одному в строке. Это только цели поиска по звучанию
(`FuzzyTerms`): «Poloniks» → Poloniex, «полониекс» → Poloniex. Своё написание бренда не меняется (в
`terms.json` бренд не лежит: точная подмена исправляла бы регистр кодовых слов вроде «config»).

Сборка воспроизводима, ручного отбора нет:

1. `python tools/dictionary-sources/fetch_sources.py` выгружает рейтинги (файлы `*-2026-10.tsv`: имя, место).
2. `python tools/dictionary-sources/select_brands.py` отбирает по одному правилу (описано в начале файла) и
   пишет `brands.txt` и `selected-brands.tsv` (имя и источник).
3. `python tools/dictionary-sources/prune_brands.py <папка корпусов>` выбрасывает бренды, которые заменили
   слово контрольных корпусов (`tools/eval/make-corpora.py`: редкие английские SCOWL-95, редкие русские,
   частые английские); список выброшенных в `pruned-brands.tsv`. Корпуса для отсева: seed 7 и 8, проверка на
   seed 9.

| Источник | Что берётся | Лицензия или условия |
|---|---|---|
| Stack Overflow, теги (`stackoverflow-names`) | написание тега из описания тега (`/tags/{t}/wikis`); только с заглавной внутри | CC BY-SA 4.0 |
| Hugging Face (`huggingface-orgs`, `huggingface-families`) | `fullname` организаций из топа моделей; слова с заглавной внутри в идентификаторах (MiniLM), от 2 моделей | открытый API |
| Homebrew Casks (`homebrew-casks`) | показываемое имя приложения, топ-1500 по установкам за год, без шрифтов | открытый API |
| CoinGecko (`coingecko-coins`, `coingecko-exchanges`) | топ-300 монет по капитализации, топ-100 бирж | открытый API без ключа |
| DefiLlama (`defillama-protocols`) | топ-300 протоколов по TVL | открытый API |

Правило отбора (все пороги в `select_brands.py`): одно слово из латинских букв, 5+ букв, с заглавной, но не
из одних заглавных; не больше одного перехода «строчная → заглавная»; не из двух английских слов подряд; не
английское слово SCOWL и не форма такого слова; ещё не термин и не написание в `terms.json`. Из тегов
Stack Overflow только с заглавной внутри слова.
