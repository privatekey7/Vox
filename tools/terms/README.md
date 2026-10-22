# Инструменты словаря терминов

Словарь — `src/Vox.Core/Text/Data/terms.json`, политика — `docs/terms.md`.

1. Записать новые термины в текстовый файл:
   ```
   ## ai-tools
   Claude Code = клод код, клод-код
   ```
2. Проверить: `VOX_FREQ=ru_full.txt python3 tools/terms/check.py новые.txt` — дубли со словарём,
   форма источников и совпадения с обычными русскими словами (частотный словарь
   [hermitdave/FrequencyWords](https://github.com/hermitdave/FrequencyWords) `content/2018/ru/ru_full.txt`,
   CC BY-SA, в репозиторий не кладётся). Совпало — только в связке или убрать.
3. Добавить: `python3 tools/terms/add.py новые.txt`, затем `dotnet test Vox.sln --filter "Category!=Models"`.
4. Регрессия на корпусе владельца — `tools/Vox.Replay` (`--diff vox.log`).
