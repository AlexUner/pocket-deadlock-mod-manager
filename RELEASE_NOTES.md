PocketDeadlock 0.6.4 - native Windows Deadlock Mod Manager.

- Unified infinite cover catalog with cached startup indexing, hero/type/content filters, and official GameBanana account connection for restricted entries.
- Download counts and star-marked likes on catalog tiles and mod details. The star shows the source's number of likes, not a five-star review score. Missing counts show a dash; opening a card loads and caches available counts without slowing every search or fetching every profile.
- Fixed a library-opening crash caused by localization in a deferred WPF template. Accessibility labels and tooltips update when switching between Russian and English.
- Root README and license files no longer create false VPK conflicts. Game resources retain overlap checks.
- Visible manager update action, installed version, last check, progress, retry and verified install-on-close or install-and-restart controls.
- Native application/window icon, both themes, Windows language defaults and loadout sharing keys.
- GitHub Actions build and test ordinary portable packages, publish checksums and sign the update feed. Release notes are included in the manager's update window.

Antivirus limitation: earlier developer UI-check processes triggered Kaspersky System Watcher (PDM:Trojan.Win32.Generic). Developer checks and image capture are excluded from ordinary builds. The earlier detection has not been classified by Kaspersky as a false positive. Local scan and ordinary-run results are documented in [the verification report](https://github.com/AlexUner/pocket-deadlock-mod-manager/blob/main/Проверка.md); they do not guarantee acceptance on every machine or for every workflow. No files were submitted to Kaspersky. The ECDSA feed signature authenticates the release package; it is not a Windows Authenticode certificate.

Extract the whole ZIP into a writable folder and keep the EXE beside the included runtime files. GameBanana sign-in requires WebView2 Runtime. Apply a selected mod set only after closing Deadlock.

Русский: на плитках показаны скачивания и ★ с числом отметок «Нравится». Если источник не передал число, показан прочерк; доступные числа загружаются при открытии карточки и сохраняются в индексе. Исправлено падение при открытии библиотеки; доступны оба языка, темы, вход GameBanana и отдельная проверка обновлений менеджера. Предыдущее срабатывание Kaspersky на диагностическую сборку не признано лабораторией ложным. Результаты локальной проверки указаны в отчете выше; исключение для антивируса не является требованием установки.
