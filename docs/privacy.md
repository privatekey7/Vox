# Конфиденциальность Vox

[English below](#vox-privacy-policy)

Обновлено 25 октября 2026 года.

Vox распознаёт речь на вашем компьютере. Голос, тексты диктовок и настройки никуда не отправляются.
В Vox нет аккаунтов, рекламы и телеметрии.

## Что Vox делает с данными

- **Микрофон.** Vox слушает, только пока вы держите клавишу записи. Звук распознаётся на компьютере и
  не сохраняется. Исключение: настройка для разработчика «Сохранять записи диктовок» (по умолчанию
  выключена). С ней Vox хранит до 500 последних записей, не больше 300 МБ, в папке `%APPDATA%\vox`.
- **Текст диктовки** вставляется в поле у курсора через буфер обмена. Эта копия не попадает в журнал
  буфера Win+V и в облачную синхронизацию буфера. Если включено «Сохранять мой буфер обмена», после
  вставки Vox возвращает то, что вы копировали до этого.
- **История диктовок** хранится в `%APPDATA%\vox\vox_data.db`, чтобы диктовку можно было найти и
  скопировать. Кнопка «Очистить» в разделе «История» удаляет её.
- **Словарь и исправления.** После вставки Vox смотрит на поле, куда вставил текст, чтобы заметить,
  как вы исправили слово, и запомнить замену. Выключается настройкой «Учиться на моих исправлениях».
- **Подсказки с экрана.** Vox читает заголовки открытых окон, чтобы правильно писать названия программ
  и сайтов. Запомненные названия хранятся в `%APPDATA%\vox\screen_names.json` (не больше 5000) и
  забываются через 7–30 дней, а те, что помогли в диктовке, через 180 дней. В настройках можно
  посмотреть, что Vox запомнил, очистить память или выключить подсказки. Чтение текста активного окна
  по умолчанию выключено.
- **Лог** `%APPDATA%\vox\logs\vox.log` содержит служебные события: длину текста, время, ошибки. Тексты
  диктовок попадают туда, только если включить настройку для разработчика «Писать тексты диктовок в лог».
- **Отчёты о сбоях.** Если Vox падает, он сохраняет отчёт на этом компьютере. Отчёт никуда не
  отправляется и удаляется вместе с историей.

## Сеть

Vox выходит в интернет только чтобы скачать модель распознавания: при первом запуске и при смене языка
или модели. Модели скачиваются с GitHub (проект [k2-fsa/sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx/releases)),
каждый файл сверяется с контрольной суммой. Больше Vox ничего не скачивает и не отправляет.

## Перенос и удаление

- «Сохранить в файл…» в настройках создаёт файл переноса на другой компьютер. Файл появляется только по
  вашей команде и остаётся у вас.
- «Удалить мои данные…» удаляет всё, что Vox хранит для вас на этом компьютере.
- При удалении программы Vox спрашивает, удалить ли историю, словарь и настройки.

## Связь

Вопросы о конфиденциальности: [GitHub Issues](https://github.com/privatekey7/vox/issues) или Telegram
[@privatekey_ai](https://t.me/privatekey_ai).

---

# Vox privacy policy

Last updated October 25, 2026.

Vox recognises speech on your computer. Your voice, dictated texts and settings are never sent anywhere.
Vox has no accounts, no ads and no telemetry.

## What Vox does with data

- **Microphone.** Vox listens only while you hold the recording key. The audio is recognised on your
  computer and is not saved, except with the developer setting "Save dictation recordings" (off by
  default): then Vox keeps up to the last 500 recordings, at most 300 MB, in `%APPDATA%\vox`.
- **Dictated text** is pasted at the cursor through the clipboard. This copy is kept out of the Win+V
  clipboard history and clipboard cloud sync. With "Keep my clipboard" on, Vox puts back what you had
  copied before.
- **Dictation history** is stored in `%APPDATA%\vox\vox_data.db`, so you can find and copy a dictation.
  The "Clear" button in "History" deletes it.
- **Dictionary and corrections.** After pasting, Vox looks at the field it pasted into to notice how you
  fixed a word and remember the replacement. Turned off with "Learn from my corrections".
- **Hints from the screen.** Vox reads the titles of open windows to spell program and site names
  correctly. Remembered names are stored in `%APPDATA%\vox\screen_names.json` (at most 5000) and are
  forgotten after 7–30 days, or 180 days for names that helped in a dictation. In settings you can see
  what Vox remembered, clear it or turn the hints off. Reading the text of the active window is off by
  default.
- **Log** `%APPDATA%\vox\logs\vox.log` holds service events: text length, timing, errors. Dictated texts
  go there only with the developer setting "Write dictation texts to the log".
- **Crash reports.** If Vox crashes, it saves a report on this computer. The report is not sent anywhere
  and is deleted together with the history.

## Network

Vox connects to the internet only to download a speech model: on first start and when you change the
language or model. Models come from GitHub ([k2-fsa/sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx/releases)),
and every file is checked against its checksum. Vox downloads and sends nothing else.

## Transfer and deletion

- "Save to file…" in settings creates a transfer file for another computer. It is created only when you
  ask and stays with you.
- "Delete my data…" deletes everything Vox keeps for you on this computer.
- When you uninstall Vox, it asks whether to delete the history, dictionary and settings.

## Contact

Privacy questions: [GitHub Issues](https://github.com/privatekey7/vox/issues) or Telegram
[@privatekey_ai](https://t.me/privatekey_ai).
