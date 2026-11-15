# Security policy

[По-русски ниже](#безопасность)

Vox is an offline dictation app for Windows. It records the microphone while you hold a key, recognises
speech on your computer and pastes the text into the active window. It has no server and no accounts.

## Supported versions

| Version | Supported |
|---|---|
| Latest 2.x release | Yes |
| Older 2.x releases | No, please update to the latest one |
| 1.x (Python) | No |

## Reporting a vulnerability

Please do not open a public issue for a security problem.

1. Preferred: [report it privately on GitHub](https://github.com/privatekey7/Vox/security/advisories/new)
   (Security → Advisories → Report a vulnerability).
2. If that does not work for you: write to Telegram [@privatekey_ai](https://t.me/privatekey_ai) and ask
   for a private contact. Do not post the details in public comments.

What helps:

- the Vox version (shown at the bottom of Settings → Help) and the Windows version;
- steps to reproduce, or a proof of concept;
- what an attacker gains and what they need to start with (another Windows account on the same PC, a
  file you open, a network position).

Do not attach `vox.log`, `vox_data.db` or dictation recordings as they are: they can contain the texts
of your dictations. Cut out the lines that matter.

Vox is maintained by one person. You will get an answer as soon as the report is read, and credit in
the release notes if you want it.

## What counts as a vulnerability

Vox works offline, so the risks are local. Examples of what we want to hear about:

- **Models.** Vox loads a speech model whose files do not match the pinned SHA-256, or a model another
  Windows user placed in the shared `C:\ProgramData\Vox\models` folder
  ([ADR 036](docs/adr/036-model-integrity.md)).
- **Installer and updates.** A release file that differs from what GitHub Actions built, a way to make
  the installer or the uninstaller run or delete something with administrator rights for another user
  ([code signing policy](docs/code-signing-policy.md), [ADR 034](docs/adr/034-installer-user-areas.md)).
- **Privacy.** Audio, dictated text, clipboard contents or window titles leaving the computer, or stored
  where the [privacy policy](docs/privacy.md) says they are not: dictation texts in `vox.log` with the
  developer setting off, passwords or keys in the remembered screen names, a dictation in the Win+V
  clipboard history ([ADR 035](docs/adr/035-learning-and-screen-names-hardening.md)).
- **Network.** Any connection other than the ones the privacy policy lists.
- **Text injection.** A file, a window title or a transfer file that makes Vox type or paste text the
  user did not dictate.
- **Dependencies.** A known vulnerability in a bundled component (sherpa-onnx, onnxruntime, SQLite,
  NAudio, the .NET runtime) that can be reached through Vox.

Not a vulnerability:

- The SmartScreen warning: the installer is not signed yet ([ADR 032](docs/adr/032-smartscreen.md)).
  You can verify the file with its SHA-256 and with `gh attestation verify`, see the
  [README](README.en.md#questions-and-problems).
- Anything that needs administrator rights or code already running under your own Windows account: such
  a program can read your files and keystrokes without Vox.
- Dictation texts in the log or recordings on disk when you turned these developer settings on yourself.
- Recognition mistakes. Please report them as ordinary [issues](https://github.com/privatekey7/Vox/issues).

---

# Безопасность

Vox работает офлайн: записывает микрофон, пока вы держите клавишу, распознаёт речь на вашем компьютере
и вставляет текст в активное окно. Сервера и аккаунтов нет.

- **Поддерживается** только последний выпуск 2.x. Версии 1.x (Python) не поддерживаются.
- **Как сообщить.** Не создавайте публичный issue. Напишите
  [приватно через GitHub](https://github.com/privatekey7/Vox/security/advisories/new)
  (Security → Advisories → Report a vulnerability). Запасной путь: Telegram
  [@privatekey_ai](https://t.me/privatekey_ai), попросите личный контакт и не пишите подробности
  в открытых комментариях.
- **Что приложить:** версию Vox и Windows, шаги или пример, что получает атакующий и что ему нужно
  для начала.
- **Чего не прикладывать:** `vox.log`, `vox_data.db` и записи диктовок целиком. В них могут быть тексты
  ваших диктовок. Вырежьте только нужные строки.
- **Что считается уязвимостью:** загрузка подменённой модели, подмена установщика или действия
  установщика с правами администратора в чужой учётной записи, утечка звука, текста диктовок, буфера
  обмена или заголовков окон, любое обращение в сеть сверх описанного в
  [политике конфиденциальности](docs/privacy.md), вставка текста, который пользователь не диктовал,
  известная уязвимость во встроенном компоненте, до которой можно добраться через Vox.
- **Что не считается:** предупреждение SmartScreen (установщик пока не подписан), всё, для чего уже
  нужны права администратора или программа под вашей учётной записью, тексты в логе при включённой
  настройке для разработчика, ошибки распознавания.
