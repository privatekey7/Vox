# Vox 2.0 — переход на C#: документ передачи

Этот файл — стартовая точка для новой сессии. Здесь всё, что решено и проверено
в предыдущей сессии (21.10.2026), чтобы не повторять исследование.

## 0. Как работать с владельцем проекта

- Общение — на русском, коротко, списками. Сначала план → согласование → код.
- Стандарт разработки — [privatekey7/BEST_PRACTICES](https://github.com/privatekey7/BEST_PRACTICES)
  (`BEST_PRACTICES.md`): спек до кода, TDD (тест падает → код), один баг — один
  регрессионный тест, Conventional Commits, минимальный diff, «лестница лени»,
  `debt:`-комментарии для сознательных упрощений, ADR в `docs/adr/`, CHANGELOG.
- **Не пушить в `main`.** Работа — в ветке `csharp`. Тестовые сборки — через
  GitHub Actions; владелец скачивает `.exe` из Artifacts сам
  (хранилище артефактов `*.blob.core.windows.net` закрыто в облачном окружении).
- Владелец тестирует на ПК и на ноутбуке (64 ГБ ОЗУ).

## 1. Решение

Переписать Vox с Python на **C# / .NET 10 LTS**, **WinForms**, в этом же репозитории
(ветка `csharp`). Python-версия остаётся в `main`, пока C# её не догонит, затем
замена и релиз **2.0**.

Причины: однопоточный UI вместо Tkinter-в-потоках (класс вылетов, см. §6),
нативный WH_KEYBOARD_LL-хук (можно скрыть хоткей от других приложений),
WASAPI-микрофон со сменой устройств, UI Automation без comtypes, без PyInstaller.

**Открытые вопросы к владельцу** (рекомендации предыдущей сессии в скобках —
подтвердить перед стартом):
1. WinForms или WPF? (WinForms)
2. Ветка `csharp` в этом репо? (да)
3. Чинить ли вылет при смене модели в Python 1.3 параллельно? (по желанию владельца)

## 2. Что проверено прототипом (всё работает)

| Проверка | Результат |
|---|---|
| sherpa-onnx C# (`org.k2fsa.sherpa.onnx` 1.13.8) на GigaAM v3 и Parakeet v3 | Текст **идентичен Python** во всех 42 случаях (21 запись × 2 модели), скорость та же |
| Сборка `net10.0-windows` на Linux (`EnableWindowsTargeting`): NotifyIcon, SetWindowsHookEx, NAudio WASAPI, Clipboard, UI Automation | Собирается |
| Чтение `vox_data.db` от Python-версии через Microsoft.Data.Sqlite (FTS5-поиск, словарь) | Работает тем же SQL |
| Распаковка `.tar.bz2` (SharpZipLib) | Побайтно верно |
| Размер self-contained single-file `.exe` | 142 МБ без WPF (≈40 МБ в установщике); с WPF 199 МБ — **WPF не брать** |

### Проверенный код распознавания

```csharp
using SherpaOnnx;

var config = new OfflineRecognizerConfig();
config.FeatConfig.SampleRate = 16000;
config.FeatConfig.FeatureDim = 80;               // GigaAM тоже работает с 80 (берёт из метаданных)
config.ModelConfig.Transducer.Encoder = ".../encoder.int8.onnx";
config.ModelConfig.Transducer.Decoder = ".../decoder.onnx";      // у Parakeet: decoder.int8.onnx
config.ModelConfig.Transducer.Joiner  = ".../joiner.onnx";       // у Parakeet: joiner.int8.onnx
config.ModelConfig.Tokens = ".../tokens.txt";
config.ModelConfig.ModelType = "nemo_transducer";
config.ModelConfig.NumThreads = Math.Max(1, Environment.ProcessorCount / 2);
config.ModelConfig.Provider = "cpu";
config.DecodingMethod = "greedy_search";
using var recognizer = new OfflineRecognizer(config);

using var stream = recognizer.CreateStream();
stream.AcceptWaveform(16000, samples);   // float[] в [-1, 1]
recognizer.Decode(stream);               // есть и Decode(IEnumerable<OfflineStream>) — пачкой
string text = stream.Result.Text;
```

### Пакеты (версии выбраны осознанно)

| Пакет | Версия | Зачем / почему эта версия |
|---|---|---|
| `org.k2fsa.sherpa.onnx` | 1.13.8 | Распознавание; та же версия, что в Python |
| `Microsoft.Data.Sqlite.Core` | 10.0.13 | История/словарь |
| `SQLitePCLRaw.bundle_e_sqlite3` | 3.0.5 | **Явно**: транзитивный 2.1.11 имеет уязвимость GHSA-2m69-gcr7-jv3q |
| `NAudio.Wasapi` | 3.1.0 | Микрофон. Ветка 3.x молодая и меняется каждые дни; 3.1.0 — от 07.09.2026 |
| `SharpZipLib` | 1.4.2 | `.tar.bz2` моделей (в .NET нет bzip2) |
| UI Automation | COM `UIAutomationCore` | Не через WPF (`System.Windows.Automation`) — WPF +57 МБ |

## 3. Архитектура

```
Vox.sln
├─ src/Vox.Core      (net10.0)          постобработка, сегментация, каталог моделей, конфиг, SQLite
├─ src/Vox.Engine    (net10.0)          обёртка sherpa-onnx (+ сегментация длинных записей)
├─ src/Vox.App       (net10.0-windows)  WinForms: хук, микрофон, вставка, буфер, трей, окна, загрузка моделей
└─ tests/Vox.Core.Tests, tests/Vox.Engine.Tests (xUnit) — гоняются в облаке на Linux
```

Все окна — в одном UI-потоке; фоновые потоки общаются с ним через `Control.BeginInvoke`.

## 4. Что переносить (эталон — Python в ветке `feat/gigaam-ru`)

Поведение 1.3.0 (см. `CHANGELOG.md`, `docs/adr/030-…`):

| Python | Суть | Тесты-эталоны |
|---|---|---|
| `voxapp/post_processor.py` | Конвейер: terms → fillers → ITN (если `needs_itn`) → currency → sentence_spacing → punctuation (если `needs_punctuation`) → leading space | `tests/test_post_processor.py` |
| `voxapp/terms.py`, `punctuation.py::replace_terms` | IT-словарь «гитхаб → GitHub» | — |
| `voxapp/fillers.py` | Удаление «э-э», «ммм» | — |
| `voxapp/itn.py` | Числа, слотовый парсер для смеси цифр и слов | `tests/test_itn.py` (38) |
| `voxapp/currency.py` | «500 ₽» → «500 рублей» с согласованием | `tests/test_currency.py` |
| `voxapp/sentence_spacing.py`, `spacing.py` | Пробелы между предложениями/диктовками; `split_leading_separator` (пробел печатается, тело вставляется — из-за омнибокса Chrome) | — |
| `voxapp/segmenter.py` | Нарезка > 12 с по самым тихим местам, без потери сэмплов | `tests/test_segmenter.py` |
| `voxapp/registry.py` | Каталог: `gigaam-v3-rnnt-punct` (по умолчанию), `parakeet-tdt-0.6b-v3` | `tests/test_registry.py` |
| `voxapp/worker.py` | Запись по хоткею, хвост 150 мс, поток обработки, диагностика, возврат буфера, отмена при сочетании клавиш | `tests/test_worker.py` |
| `voxapp/correction_monitor.py::apply_dictionary` | Применение пользовательского словаря | — |
| `voxapp/storage.py` | SQLite (схема ниже) | — |

**Сверка паритета:** прогнать все `[STT] Raw:` из `vox.log` владельца через Python- и
C#-конвейер — результаты должны совпасть до символа. Лог содержит личные тексты —
**в репозиторий не коммитить**; попросить владельца загрузить его в сессию заново.

## 5. Совместимость данных (обновление поверх Python без потерь)

Папка данных: `%APPDATA%\vox\` — `models\`, `logs\vox.log`, `vox_data.db`, `config.json`.

`config.json`: `hotkey` (имя клавиши в стиле библиотеки `keyboard`: `right ctrl`, `ctrl`,
`right shift`, `shift`, `right alt`, `alt`, `caps lock`, `scroll lock`, `pause`, `f1`…`f12`),
`active_model`, `language`.

Модели: `models\<extracted_dir>` из `registry.py` — те же каталоги, перекачивать не нужно.

Схема `vox_data.db` (создаётся Python-версией; C# должен открывать как есть):

```sql
CREATE TABLE transcriptions (id INTEGER PRIMARY KEY AUTOINCREMENT, raw_text TEXT NOT NULL,
  processed_text TEXT NOT NULL, model TEXT NOT NULL DEFAULT '', lang TEXT NOT NULL DEFAULT '',
  duration_ms INTEGER NOT NULL DEFAULT 0, app_context TEXT NOT NULL DEFAULT '', created_at REAL NOT NULL);
CREATE VIRTUAL TABLE transcriptions_fts USING fts5(processed_text, content='transcriptions',
  content_rowid='id', tokenize='unicode61');
-- триггеры transcriptions_ai / _ad / _au синхронизируют FTS
CREATE TABLE dictionary (id INTEGER PRIMARY KEY AUTOINCREMENT, wrong TEXT NOT NULL, correct TEXT NOT NULL,
  lang TEXT NOT NULL DEFAULT '', source TEXT NOT NULL DEFAULT 'manual', created_at REAL NOT NULL,
  UNIQUE(wrong, lang));
CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
-- поиск: SELECT t.* FROM transcriptions_fts fts JOIN transcriptions t ON t.id = fts.rowid
--        WHERE transcriptions_fts MATCH $q ORDER BY rank LIMIT $n
```

## 6. Известные проблемы Python-версии (не повторить в C#)

- **Вылет при смене модели из трея** (1.3.0, ноутбук владельца): процесс закрывается без
  сообщения. Не диагностирован (лог не получен). Главная гипотеза — Tkinter-окно
  создаётся в фоновом потоке и уничтожается GC в другом (`Tcl_AsyncDelete`). В C#
  исключается единым UI-потоком.
- Из лога (1725 диктовок): записи 15–25 с давали пустой текст в 21% (решено нарезкой);
  серии пустых подряд — микрофон переставал отдавать звук (решено переоткрытием).
- GigaAM пишет английские слова кириллицей — нужен IT-словарь.

## 7. Этапы

1. Каркас решения + CI (тесты Linux, `vox-setup.exe` на Windows, SHA-256 в артефактах).
2. Vox.Core: перенос + все Python-тесты + сверка по логу.
3. Минимальная рабочая версия: движок, WASAPI, хук, вставка, возврат буфера → **тестовая сборка №1**.
4. Трей, мастер первого запуска с выбором модели, загрузка/смена модели, история, логи → **сборка №2**.
5. Самообучающийся словарь через UI Automation (дизайн согласован с владельцем:
   читать поле после вставки, diff одного слова, тост «Запомнил: X → Y [Отменить]»,
   пароли не читать, отдельный поток с таймаутом).
6. Установщик (Inno Setup, `vox.iss`), обновление поверх 1.x, README, ADR → **2.0**.

## 8. Окружение облачной сессии

- .NET: `apt-get install -y dotnet-sdk-10.0` (загрузки с `builds.dotnet.microsoft.com` закрыты).
- NuGet (`api.nuget.org`), PyPI, GitHub Releases — доступны. `huggingface.co` и
  `*.blob.core.windows.net` (артефакты Actions) — закрыты.
- Модели для тестов: `https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/`
  `sherpa-onnx-nemo-transducer-punct-giga-am-v3-russian-2025-12-16.tar.bz2` (162 МБ),
  `sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8.tar.bz2` (465 МБ).
  Синтетическая русская речь для тестов: TTS `tts-models/vits-piper-ru_RU-irina-medium.tar.bz2`,
  `vits-piper-ru_RU-dmitri-medium.tar.bz2` через sherpa-onnx (Python или C#).
