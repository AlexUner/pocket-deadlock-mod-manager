# PocketDeadlock player guide

[Project overview](../README.md) | [Русская инструкция](guide.ru.md)

## Quick start

1. Download and extract the whole Windows release archive into a writable folder. Keep the EXE and neighboring files together.
2. Run `PocketDeadlock.exe`. A self-contained release does not require a separate .NET runtime.
3. Check the detected folder. Use **Game folder** to select the Deadlock root if needed.
4. Find a mod in **Catalog**, choose its download file, and click **Download to library**. For multiple VPK choices, select the intended files. You can also use **Import VPK / archive** in **Library**.
5. In **Library**, enable the desired mods and arrange their order. Higher entries have higher priority. For an intentional overlap, allow the higher-priority mod to override others; otherwise disable one conflicting mod.
6. Close Deadlock and click **Apply selected mods**. New imports are disabled by default. Selection, priority, and profile changes do not immediately change the applied files.
7. Click **Launch Deadlock**. By default, the selected set is applied before launching; change this in **Settings** if needed.

**Ctrl+F** focuses search. **Escape** cancels a foreground operation; use **Downloads** to cancel individual transfers.

## Features

- **Infinite cover feed:** Catalog, search and Favorites use an adaptive image grid without page buttons. The saved index exposes every matching result while the panel creates only visible tiles and two upcoming rows. Covers are prefetched with four simultaneous requests and memory/disk caching. A new search resets scrolling; background indexing retains the visible anchor. Details and file variants load on selection.
- **Popularity at a glance:** tiles and mod details show download counts and a star with the source's likes count. The star is not a five-star review score. A dash means the source has not supplied the count; selecting a card loads available statistics and saves them in the index. Sparse background refreshes retain cached counts. Tooltips and accessible labels identify both values.
- **GameBanana account:** log in or sign up on the official website inside an embedded WebView2 window, then connect the account. The manager verifies the session with GameBanana, encrypts it for the current Windows user, and sends cookies only to the HTTPS GameBanana website. Passwords are entered on the website. Log out to remove the saved session. Filter All available, Regular content or Sensitive / NSFW independently of hero and mod type. Hidden entries require a connected account; access still depends on the source and account settings.
- **One combined catalog:** GameBanana, Deadlocker, and DeadlockMods work behind the scenes. Origin IDs remove duplicate listings while retaining community-only mods. Hero/type/content filtering and sorting cover the entire combined index.
- **Hero and mod-type filters:** choose a hero and independently choose skins/models, interface, effects, weapons, music, voices, or another type. Both pickers include search, counts, Down/Enter selection and Escape. Type counts follow the selected or searched hero; Reset clears both filters. Sorting is in the small **Sort** menu.
- **Hero-aware local search:** `page`, `Paige` and `пейдж` recognize Paige, including mods whose title omits the hero. Russian names and common aliases are supported for the recognized hero roster. A saved disk index, background startup refresh and debounced search keep repeated queries local. Fresh GameBanana rows are merged, then filtered by indexed title, author, hero and type metadata; unrelated full-text hits cannot bypass relevance. Paste a GameBanana URL to open it directly.
- **Local library:** import VPK, ZIP, RAR, or 7Z; drag files into the window; choose VPK files inside archives; copy selected old VPK files from the game's `addons` folder.
- **Downloads and favorites:** two simultaneous transfers, cancellation, saved operation history, and local favorites.
- **Priority and overlaps:** enable mods, reorder by dragging or Move up/Move down, and explicitly permit a higher-priority mod to override overlapping game resources. Root README, LICENSE and similar documentation files do not cause conflicts; nested text files and configurations still do.
- **Profiles:** save and switch sets; import/export JSON; copy a `PD1-...` key to restore a catalog-backed set on another computer, preserving order, variants, selected VPK paths, and override choices.
- **Mod updates:** preserve the chosen variant, retain previous revisions, and roll back. Ambiguous replacements require manual selection; rollback disables that mod's automatic update option.
- **Game integration:** detect Steam libraries, choose a folder, apply or disable the manager's set, and launch through Steam. Apply, Disable, and Launch are blocked while Deadlock is detected as running.
- **App updates:** the default GitHub feed uses ECDSA release signatures and SHA256 package checks. Verified updates are staged and installed when the manager closes.
- **Language and theme:** Russian and English, light and dark. Defaults follow Windows; non-Russian system languages use English. Preferences can be changed in Settings.

Community entries with a GameBanana origin obtain current details and direct download files there. Community-only entries require downloading from their source page and importing the file. Catalog coverage depends on the providers; the saved index is not a complete mirror of every mod.

## Transfer a profile

Save the current set in **Profiles**, then use **Copy loadout key**. On another computer, paste the `PD1-...` key and choose **Download loadout by key**. The manager downloads available matching GameBanana variants and selects the restored set. Apply while the game is closed.

A key contains references and settings, not mod files or cloud storage. It includes enabled catalog-backed entries. Enabled local-only VPK files block key creation: transfer those files separately and import them, or disable them before creating a key. JSON export can record local entries but also excludes file content. Removed or ambiguous remote variants need manual recovery; neither format guarantees continued file availability.

## Updates

Checks run at startup and every 30 minutes while the manager is open, according to Settings. Global and per-mod options control automatic downloads. A mod update or rollback becomes pending game application. You can also check manually.

The signed app-update feed uses [the latest GitHub release](https://github.com/AlexUner/pocket-deadlock-mod-manager/releases/latest). Open **Updates** beside the version to check the manager independently of mod updates. Its window shows the last check, progress, retry information and release notes. A newer verified package is prepared before installation. **Install and restart** closes and reopens the manager; closing normally also installs a staged update. The updater verifies the signed release and package again before replacing application files. Language changes re-render the update state from saved codes instead of retaining the old interface language.

## Game files and recovery

PocketDeadlock mounts copies under `Deadlock\game\citadel\pocket_mods` using a marked `PocketDeadlock` block in `gameinfo.gi`. Reapplying unchanged content reuses verified copies. **Disable managed mods** removes that block and preserves the rest of the configuration.

Files in `game/citadel/addons` are copied only when selected for import. They are not moved, renamed, deleted, or migrated from another manager's database. External mounts remain. Conflict checks cover the selected PocketDeadlock set; external mods can still interact with it.

Local data is under `%LOCALAPPDATA%\PocketDeadlock`:

| Location | Contents |
| --- | --- |
| `state.json` | Library, profiles, favorites, history, and settings |
| `mods` | Imported content and retained revisions |
| `catalog-index` | Saved catalog metadata |
| `thumbnails` | Decoded cover PNGs, reused after restart; bounded to 128 MiB and 30 days |
| `gamebanana-session.dat` | Windows-encrypted GameBanana session, local to this Windows account |
| `gamebanana-browser` | Separate WebView2 website profile; unrelated browser profiles are not used |
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
