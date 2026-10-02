# PocketDeadlock - Deadlock Mod Manager for Windows

A native Windows x64 tool for finding Deadlock mods, managing a local VPK library, and switching mod sets. Built with C# and WPF. Independent of Valve, GameBanana, Deadlocker, and DeadlockMods.

[Download the latest release](https://github.com/AlexUner/pocket-deadlock-mod-manager/releases/latest) · [Русская инструкция](#русская-инструкция)

![PocketDeadlock catalog, Russian dark interface](assets/catalog-ru.png)

<details><summary>English light theme, library and loadout keys</summary>

![English light theme](assets/catalog-en-light.png)
![Searchable categories](assets/category-search-ru.png)
![Local mod library](assets/library-en.png)
![Loadout profiles and sharing keys](assets/profiles-en.png)

</details>

## Features

- **One combined catalog:** GameBanana, Deadlocker, and DeadlockMods work behind the scenes. Origin IDs remove duplicate listings while retaining community-only mods. Mod and sound browsing share one search; no provider switching is needed. Category filtering and sorting cover the combined index before pagination.
- **Searchable categories:** open **All categories** and type to narrow the list. Down/Enter selects a category; Escape closes it. Secondary sorting choices are in the small **Sort** menu.
- **Local search:** a saved disk index, background refresh at startup, debounced search, and fresh GameBanana results added to indexed results. Paste a GameBanana mod or sound URL to open it directly.
- **Local library:** import VPK, ZIP, RAR, or 7Z; drag files into the window; choose VPK files inside archives; copy selected old VPK files from the game's `addons` folder.
- **Downloads and favorites:** two simultaneous transfers, cancellation, saved operation history, and local favorites.
- **Priority and overlaps:** enable mods, reorder by dragging or Move up/Move down, and explicitly permit a higher-priority mod to override overlapping resources.
- **Profiles:** save and switch sets; import/export JSON; copy a `PD1-...` key to restore a catalog-backed set on another computer, preserving order, variants, selected VPK paths, and override choices.
- **Mod updates:** preserve the chosen variant, retain previous revisions, and roll back. Ambiguous replacements require manual selection; rollback disables that mod's automatic update option.
- **Game integration:** detect Steam libraries, choose a folder, apply or disable the manager's set, and launch through Steam. Apply, Disable, and Launch are blocked while Deadlock is detected as running.
- **App updates:** the default GitHub feed uses ECDSA release signatures and SHA256 package checks. Verified updates are staged and installed when the manager closes.
- **Language and theme:** Russian and English, light and dark. Defaults follow Windows; non-Russian system languages use English. Preferences can be changed in Settings.

Community entries with a GameBanana origin obtain current details and direct download files there. Community-only entries require downloading from their source page and importing the file. Catalog coverage depends on the providers; the saved index is not a complete mirror of every mod.

## Quick start

1. Download and extract the whole Windows release archive into a writable folder. Keep the EXE and neighboring files together.
2. Run `PocketDeadlock.exe`. A self-contained release does not require a separate .NET runtime.
3. Check the detected folder. Use **Game folder** to select the Deadlock root if needed.
4. Find a mod in **Catalog**, choose its download file, and click **Download to library**. For multiple VPK choices, select the intended files. You can also use **Import VPK / archive** in **Library**.
5. In **Library**, enable the desired mods and arrange their order. Higher entries have higher priority. For an intentional overlap, allow the higher-priority mod to override others; otherwise disable one conflicting mod.
6. Close Deadlock and click **Apply selected mods**. New imports are disabled by default. Selection, priority, and profile changes do not immediately change the applied files.
7. Click **Launch Deadlock**. By default, the selected set is applied before launching; change this in **Settings** if needed.

**Ctrl+F** focuses search. **Escape** cancels a foreground operation; use **Downloads** to cancel individual transfers.

## Transfer a profile

Save the current set in **Profiles**, then use **Copy loadout key**. On another computer, paste the `PD1-...` key and choose **Download loadout by key**. The manager downloads available matching GameBanana variants and selects the restored set. Apply while the game is closed.

A key contains references and settings, not mod files or cloud storage. It includes enabled catalog-backed entries. Enabled local-only VPK files block key creation: transfer those files separately and import them, or disable them before creating a key. JSON export can record local entries but also excludes file content. Removed or ambiguous remote variants need manual recovery; neither format guarantees continued file availability.

## Updates

Checks run at startup and every 30 minutes while the manager is open, according to Settings. Global and per-mod options control automatic downloads. A mod update or rollback becomes pending game application. You can also check manually.

The signed app-update feed uses [the latest GitHub release](https://github.com/AlexUner/pocket-deadlock-mod-manager/releases/latest). A newer verified package is prepared before installation. **Install manager update** closes the manager and requests a restart; closing normally also installs a staged update. The updater verifies the signed release and package again before replacing application files.

## Game files and recovery

PocketDeadlock mounts copies under `Deadlock\game\citadel\pocket_mods` using a marked `PocketDeadlock` block in `gameinfo.gi`. Reapplying unchanged content reuses verified copies. **Disable managed mods** removes that block and preserves the rest of the configuration.

Files in `game/citadel/addons` are copied only when selected for import. They are not moved, renamed, deleted, or migrated from another manager's database. External mounts remain. Conflict checks cover the selected PocketDeadlock set; external mods can still interact with it.

Local data is under `%LOCALAPPDATA%\PocketDeadlock`:

| Location | Contents |
| --- | --- |
| `state.json` | Library, profiles, favorites, history, and settings |
| `mods` | Imported content and retained revisions |
| `catalog-index` | Saved catalog metadata |
| `backups` | Original `gameinfo.gi` bytes, named by SHA256 |
| `staging`, `updates` | Temporary work and prepared app-update packages |

Settings can open the data and backup folders. Removing a library entry retains stored content for recovery and takes effect in the game on the next Apply. It does not clean disk space.

Prefer **Disable managed mods** to restoring an old configuration manually: Steam may have changed it since the backup. For complete removal, close Deadlock, disable managed mods, then close the manager before deleting its application folder, local data folder, and only the game's `pocket_mods` directory. Preserve `addons` and unrelated game files.

## Supported formats and limits

- Self-contained VPK files only; split VPK files with external segments are rejected.
- VPK, ZIP, RAR, and 7Z inputs up to 2 GiB; extracted VPK content up to 4 GiB; up to 99 VPK files per imported set.
- Only VPK content is installed. Scripts, DLLs, executables, loose configs, and dependencies require the author's separate instructions.
- Downloads are checked against the advertised size and GameBanana MD5 when provided. Library files use SHA256 and VPK structure/resource checks.
- Overlap permission controls priority; it does not merge or build VPK files.
- Steam or game updates can reset mounting. Reapply after closing the game if needed. Check each mod in the current game for compatibility.
- No crosshair editor, VPK authoring, plugins, or cloud profiles. There is no guarantee every mod works or an antivirus accepts every build.

## Build and test

Build on Windows with the **.NET 10 SDK**. The project targets `net10.0-windows`, uses WPF, and pins SharpCompress in `packages.lock.json`. Run from this source directory:

```powershell
dotnet restore -r win-x64 --locked-mode
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o publish
.\publish\PocketDeadlock.exe
```

Use a dedicated scratch directory for self-tests:

```powershell
.\publish\PocketDeadlock.exe --self-test .\test-output --offline
.\publish\PocketDeadlock.exe --self-test .\test-output
```

Offline tests use synthetic game and catalog fixtures. The default run also exercises live services and needs network access. Each run creates its own subdirectory under the supplied target; `test-results.txt` is written at the target, and a failure writes `FAILURE.txt`. The runner does not apply to the detected real game. A passing runner does not establish live-game mod compatibility. See [Проверка.md](Проверка.md) for recorded results.

## Architecture

| Source | Responsibility |
| --- | --- |
| `App.xaml`, `MainWindow.xaml`, `MainWindow*.cs` | WPF styles, layout, catalog/library workflows, downloads, profiles, settings, update UI |
| `Localization.cs`, `Translations.cs` | System-language defaults, Russian/English text, theme resources |
| `Models.cs` | Catalog, library, profile, activity, revision records |
| `GameBanana.cs`, `Catalogs.cs` | `IModCatalog`, origin API access, community adapters |
| `HybridCatalogs.cs` | Disk index, local queries, background refresh, remote-search enrichment |
| `UnifiedCatalog.cs`, `MainWindow.Catalog.cs` | Combined provider identities, searchable category picker, secondary sorting menu |
| `ModStorage.cs` | Atomic state writes, archives, hashes, VPK validation, revisions, rollback |
| `GameInstall.cs` | Steam detection, running-game checks, scoped copies and configuration patching |
| `Profiles.cs` | Profile selection, JSON transfer, PD1 encoding and validation |
| `ModUpdates.cs` | Chosen-variant matching and library updates |
| `AppUpdates.cs`, `UpdateKey.cs` | Signed releases, staging, application replacement; public verification key only |
| `SelfTests.cs` | Synthetic and optional live-service checks |

[PRODUCT.md](PRODUCT.md) records scope. [DESIGN.md](DESIGN.md) and [.impeccable/design.json](.impeccable/design.json) record the native visual system. Archive handling uses [SharpCompress](https://github.com/adamhathcock/sharpcompress) 0.50.4 under MIT; [its license notice](SharpCompress-LICENSE.txt) is included.

## Русская инструкция

PocketDeadlock - менеджер модов Deadlock для Windows x64. Он хранит библиотеку VPK, помогает выбрать варианты и переключать наборы. По умолчанию язык и тема берутся из Windows: для русского языка - русский, для остальных - английский. В настройках можно явно выбрать русский или English, светлую или темную тему.

[Скачать последний выпуск](https://github.com/AlexUner/pocket-deadlock-mod-manager/releases/latest)

### Быстрый запуск

1. Распакуйте весь архив в папку с доступом на запись. Сохраните `PocketDeadlock.exe` вместе с соседними файлами. Для готовой самостоятельной сборки отдельная установка .NET не нужна.
2. Запустите программу и проверьте папку Deadlock. При необходимости нажмите **Папка игры** и выберите корень игры.
3. В **Каталоге** найдите мод или вставьте ссылку GameBanana. Выберите файл и нажмите **Скачать в библиотеку**. Если в архиве несколько вариантов VPK, выберите нужные.
4. В **Библиотеке** отметьте моды и задайте порядок. Верхний мод имеет больший приоритет. Для намеренного перекрытия ресурсов разрешите верхнему моду перекрывать другие; иначе отключите один из конфликтующих.
5. Закройте Deadlock и нажмите **Применить выбранные**. Новые импортированные моды выключены. Изменение галочек, порядка или профиля само по себе не меняет подключенный набор.
6. Нажмите **Запустить Deadlock**. По умолчанию менеджер применит набор перед запуском через Steam; эту настройку можно отключить.

**Ctrl+F** переводит фокус в поиск. **Escape** отменяет текущую операцию; отдельные загрузки можно отменить на странице **Загрузки**.

### Основные возможности

- Один общий каталог: GameBanana, Deadlocker и DeadlockMods работают внутри программы. Совпадающие записи объединяются по идентификатору исходного мода; переключать базы не нужно. Сохраняются моды, звуки и уникальные записи сообществ.
- В **Все категории** есть поиск. Стрелка вниз и Enter выбирают категорию, Escape закрывает список. Сортировка доступна в небольшом меню **Порядок**. Фильтры и сортировка работают по всему объединенному индексу до деления на страницы; фоновые запросы дополняют результаты.
- Импорт VPK, ZIP, RAR и 7Z, перетаскивание файлов и выбор VPK внутри архива. **Найти старые VPK** копирует выбранные файлы из `addons` в библиотеку, сохраняя оригиналы.
- Две одновременные загрузки, отмена, история операций и избранное.
- Порядок модов, разрешение перекрытий, сохранение наборов в профили, обмен JSON и ключами PD1.
- Обновление выбранного варианта, сохранение предыдущих версий и откат. После отката автообновление мода отключается. Неоднозначные варианты требуют ручного выбора.
- Применение и отключение собственного набора, резервные копии `gameinfo.gi` и запуск через Steam. Пока Deadlock запущен, применение, отключение и запуск заблокированы.
- Обновления менеджера из подписанного канала GitHub с проверкой ECDSA и SHA256. Подготовленное обновление устанавливается при закрытии менеджера.

Записи сообществ с источником GameBanana получают оттуда актуальные файлы. Для модов без прямой загрузки откройте страницу источника, скачайте файл и импортируйте его. Индекс не является полной копией всех модов.

### Передача набора

Сохраните набор в **Профилях** и нажмите **Скопировать ключ набора**. На другом компьютере вставьте ключ `PD1-...` и нажмите **Загрузить набор по ключу**. Менеджер загрузит доступные варианты GameBanana и выберет набор в библиотеке. Примените его после выхода из игры.

Ключ сохраняет ссылки, варианты, выбранные VPK, порядок и разрешения перекрытий, но не содержит сами файлы. В него входят включенные моды из каталога. Если включен локальный VPK без ссылки на каталог, создание ключа недоступно: передайте файл отдельно и импортируйте либо отключите перед созданием ключа. JSON тоже не содержит файлы. Удаленные автором или неоднозначные варианты придется восстановить вручную. Облачного хранения профилей нет.

### Обновления и восстановление

Проверка запускается при открытии менеджера и каждые 30 минут, пока он открыт, согласно настройкам. Обновленные или восстановленные VPK нужно применить к игре. Автоматическую загрузку можно отключить глобально и для отдельного мода.

Программа подключает копии из `Deadlock\game\citadel\pocket_mods` через собственный отмеченный блок в `gameinfo.gi`. **Отключить наши моды** удаляет его, сохраняя остальные настройки. Перед изменением исходные байты сохраняются в `%LOCALAPPDATA%\PocketDeadlock\backups`. В настройках можно открыть папки данных и резервных копий.

Файлы в `addons` и подключения других менеджеров сохраняются. Проверка конфликтов охватывает выбранный набор PocketDeadlock. Удаление записи сохраняет ее содержимое для восстановления; отключение в игре произойдет при следующем применении. Повторное применение неизмененных файлов использует уже проверенные копии.

Для полного удаления закройте игру, отключите наши моды, закройте менеджер и удалите его папку, `%LOCALAPPDATA%\PocketDeadlock` и только `pocket_mods` внутри игры. Сохраните `addons` и остальные файлы игры. Для обычного отключения предпочтительнее штатная кнопка: старая резервная копия может не учитывать обновления Steam.

### Ограничения и сборка

Поддерживаются единые VPK; составные VPK с внешними частями отклоняются. Максимальный входной файл - 2 GiB, распакованные VPK - 4 GiB, один импортированный набор - 99 VPK. Скрипты, EXE, DLL, отдельные конфигурации и зависимости автоматически не устанавливаются. Разрешение перекрытий не объединяет VPK. Совместимость конкретного мода нужно проверять в текущей игре; проверка структуры и контрольной суммы ее не гарантирует.

Редактора прицела, сборки VPK, плагинов и облачных профилей нет. Программа не связана с Valve и владельцами каталогов. Гарантии приема любой сборки антивирусом нет.

Для сборки нужен **.NET SDK 10 на Windows**. Команды приведены в [Build and test](#build-and-test). `--self-test` использует отдельную искусственную папку игры; `--offline` отключает живые сетевые проверки. Результаты конкретной проверки находятся в [Проверка.md](Проверка.md).

