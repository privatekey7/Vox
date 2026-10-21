# D032. Установка без предупреждения SmartScreen

**Статус:** предложено 22.10.2026, ждёт решения владельца (нужны его аккаунты и страна проживания).

## Контекст

- `vox-setup.exe` не подписан. Windows показывает «Система Windows защитила ваш компьютер»
  («Неизвестный издатель»), пока у файла нет репутации в SmartScreen. Так было и в 1.x.
- У неподписанного файла репутация привязана к хешу, поэтому каждая новая сборка начинает с нуля.
- Факты на октябрь 2026 (источники ниже):
  - EV-сертификат больше не даёт мгновенной репутации (Microsoft убрала это в 2024).
    OV, EV и Azure Artifact Signing лишь накапливают репутацию издателя со временем, так что первые
    установки всё равно могут предупреждать.
  - Без предупреждения с первой установки ставятся только пакеты из Microsoft Store (MSIX):
    Store сам подписывает пакет. Аккаунт разработчика-частника бесплатный.
  - Azure Artifact Signing (~$10 в месяц) доступен частным лицам только в США и Канаде.
  - SignPath Foundation бесплатно подписывает OSS-проекты (Vox подходит: MIT, публичный
    репозиторий, сборка в GitHub Actions). Издателем будет «SignPath Foundation». Каждую подпись
    одобряют вручную. Проектам без заметной аудитории часто отказывают.

## Варианты

| Вариант | Предупреждение | Цена | Что нужно |
|---|---|---|---|
| A. Microsoft Store (MSIX) | нет с первой установки | бесплатно | аккаунт Partner Center (страна должна быть в списке), упаковка MSIX, доработки (ниже) |
| B. SignPath Foundation | уходит по мере накопления репутации | бесплатно | заявка, страница «Code signing policy», шаг подписи в CI; могут отказать |
| C. OV-сертификат (Certum, Sectigo…) | уходит по мере накопления репутации | €50–300 в год | проверка личности; продают не во все страны |
| D. Ничего не подписывать | всегда для новой версии | бесплатно | инструкция «Подробнее → Выполнить в любом случае» в README |

Доработки для варианта A:
- автозапуск через `StartupTask` в манифесте вместо ключа `Run`;
- проверить, что WH_KEYBOARD_LL, SendInput и UI Automation работают в full-trust пакете
  (PowerToys работает так же);
- запись в `%APPDATA%\vox` в пакете виртуализируется: данные 1.x читаются, но пишутся в папку
  пакета. Нужен перенос данных или явный путь;
- политика конфиденциальности (Store требует её из-за микрофона и чтения полей ввода);
- `.exe` из GitHub остаётся для тех, кто не пользуется Store.

## Предложение

1. Сейчас, бесплатно (D+):
   - выпускать установщик только релизами, а не каждой тестовой сборкой: так меньше разных
     хешей;
   - добавить в README инструкцию с картинкой;
   - каждый релиз отправлять в Microsoft как «ложное срабатывание» на
     https://www.microsoft.com/wdsi/filesubmission. Это иногда снимает предупреждение для
     конкретного файла, но гарантии нет.
2. Главный путь — A (Store), если Partner Center принимает страну владельца. Это единственный
   вариант без предупреждения с первой установки.
3. Параллельно подать заявку в B (SignPath). Если одобрят, подписанный `.exe` с GitHub со
   временем перестанет вызывать предупреждение.

## Источники

- Microsoft Learn: Code signing options for Windows app developers —
  https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options
- Microsoft Learn: Artifact Signing quickstart —
  https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart
- EV certs do not grant immediate reputation anymore —
  https://www.todesktop.com/blog/posts/windows-apps-psa-ev-certs-do-not-grant-immediate-reputation-anymore
- SignPath Foundation conditions — https://signpath.org/terms.html
- Отказы SignPath из-за малой аудитории (примеры): https://github.com/wslkit/skrog/issues/358,
  https://github.com/gankedtv/rewynd/issues/137
- Бесплатный аккаунт Store для частных лиц —
  https://alternativeto.net/news/2025/9/microsoft-finally-drops-microsoft-store-publishing-fee-for-individual-developers-globally
