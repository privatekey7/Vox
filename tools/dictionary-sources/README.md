# Источники для словаря терминов

Сырые списки названий, выгруженные 22.10.2026 для расширения `src/Vox.Core/Text/Data/terms.json`.
В приложение не попадают: это вход для отбора терминов, а не словарь.

| Файл | Что внутри | Откуда |
|---|---|---|
| `stackoverflow-tags-2026-10.txt` | 2500 самых популярных тегов, по одному в строке | Stack Exchange API `/2.3/tags?sort=popular&site=stackoverflow` (страницы 1–25; дальше нужен ключ API). Данные Stack Overflow — CC BY-SA 4.0 |
| `huggingface-models-2026-10.txt` | 1199 идентификаторов моделей `org/name` | Hugging Face API `/api/models`, топ-500 по `downloads`, `likes` и `trendingScore`, без повторов |
