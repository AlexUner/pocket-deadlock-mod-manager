# PocketDeadlock

<!-- impeccable:product-schema 1 -->

## Platform

Native Windows desktop, x64. The application uses WPF controls and Windows dialogs. Preserve native window, keyboard, focus, scrolling, and file-selection behavior.

## Users

Deadlock players who want to find mods, choose variants, maintain a local library, and switch sets for different heroes or sessions. Russian and English are supported. The default follows the Windows UI language: Russian for `ru`, English for other languages.

## Product Purpose

Manage the supported VPK workflow in one desktop tool: discover, download or import, choose, order, update, apply, disable, and recover a set. Success means the player can see which mods are selected and understand when that selection reaches the game.

## Operating Context

- Windows with Steam and an installed copy of Deadlock. Standard Steam libraries are detected; the player can choose a game folder.
- Catalog metadata comes from GameBanana, Deadlocker, and DeadlockMods behind one combined catalog. There is no provider selector. Matching origin IDs and content kinds merge duplicates; exclusive community entries remain. Hero/type/content filtering and sorting cover the combined index before the continuous feed is rendered. Fresh GameBanana rows are merged and subjected to the same relevance filter; remote full-text membership alone cannot admit an unrelated mod. Selected details and files come from the origin.
- Community metadata can seed the GameBanana index. Community-only entries require opening the source page and importing a downloaded file.
- Downloads, profile selection, and library changes are separate from applying files to the game. Apply, Disable, and Launch are unavailable while Deadlock is detected as running.
- Update checks occur at startup and every 30 minutes while the manager is open, according to saved settings. There is no background service after it closes.

## Capabilities and Constraints

### Supported workflows

- Browse Catalog and Favorites as adaptive cover tiles. Images load near the viewport with four concurrent requests; shared memory and decoded PNG caching avoid duplicate tile/detail requests and support subsequent offline viewing. Missing images leave a selectable tile. Arrow keys follow tile rows and columns; compact-height windows reduce cover height to retain names. Full names and captions are available in tooltips.
- Use a continuous virtualized feed for Catalog, search and Favorites. The disk index exposes all matching records without page navigation; only visible cards and two upcoming rows are instantiated. Prefetch upcoming covers; retain a scroll anchor during indexing; reset scrolling on a new search. Fetch details and variants only after selection.
- Log in or register on the official GameBanana website inside an isolated WebView2 profile. Verify the session with GameBanana, encrypt cookies for the current Windows account, scope requests to the GameBanana website and remove the saved session on logout. Offer All available, Regular and Sensitive / NSFW content filters; hidden content needs a connected account. The user controls website confirmation and visibility preferences.
- Browse mods and sounds in one catalog; search names, authors, recognized heroes and mod types; open a GameBanana URL. Hero aliases recognize page/Paige/пейдж and common Russian names. Separate searchable hero/type pickers support keyboard selection and reset. Mod-type counts follow the selected or searched hero. Sorting by update time, name, downloads or likes is a secondary menu.
- Keep local favorites. Queue downloads with two active transfers, cancellation, and a saved operation history.
- Import VPK, ZIP, RAR, and 7Z using a file dialog or drag and drop. Choose VPK files inside archives. Scan existing `game/citadel/addons` and copy selected VPK files into the library.
- Enable or disable entries, search the library, reorder priority with buttons or drag and drop, and explicitly allow a higher-priority mod to override overlapping resources.
- Save, select, import, and export profiles. Preserve order, enabled state, override choices, file variant, and selected archive paths.
- Transfer an enabled catalog-backed set with a `PD1-...` key and download missing variants. Keys and JSON profiles contain references and settings, not VPK content. Enabled local-only files must be transferred separately and cannot be included in a PD1 key.
- Match updates to the chosen variant, retain previous revisions, and roll back. Ambiguous variants or changed archive choices require manual selection. Rollback disables automatic updating for that mod.
- Apply a set through `game/citadel/pocket_mods` and a marked block in `gameinfo.gi`. Back up original configuration bytes before a change. Disable removes the manager's marked block.
- Open Updates from the main header to see the installed version, last check, progress and retry details, and check the manager independently of mod updates. Check the configured GitHub app-update feed, verify its ECDSA signature and package SHA256, stage a complete update, and install it on normal close or with Install and restart.
- Build portable releases with GitHub Actions, isolated offline checks, package validation, a signed feed and checksums. Tag pushes create draft releases; stable publication is explicit. Private signing credentials and user data are excluded from source and artifacts.
- Use the PocketDeadlock icon in the Windows EXE and every application window, with native sizes from 16 to 256 pixels.
- Follow Windows language and theme by default, with explicit Russian/English and light/dark preferences.

### Boundaries

- No crosshair editor, VPK authoring or merging, plugin system, or cloud profiles.
- Only self-contained VPK files are installed. Split VPK files with external segments are rejected. Scripts, executables, DLLs, loose configuration files, and dependencies are not installed automatically.
- Input limit: 2 GiB. Extracted VPK content: 4 GiB. One imported set: 99 VPK files.
- Conflict checks cover the selected PocketDeadlock set. Existing external mounts and `addons` may still affect the game. Another manager's database is not migrated.
- Removing an entry removes it from the library selection. Stored content remains for recovery; the game's applied set changes on the next Apply.
- Catalog availability, checksums, and structural VPK checks do not establish current-game compatibility or guarantee antivirus acceptance.
- The application is independent of Valve and the catalog providers. Similar capabilities do not establish identical parity with another manager.
- Diagnostic checks and automatic image capture are developer-only and are excluded from normal builds after a Kaspersky behavioral detection in a developer UI-check process. The original detection has not been classified as a false positive; file scans and bounded ordinary runs cannot guarantee every antivirus or workflow accepts the app. Verification records identify whether a trust rule was active.

## Brand Commitments

The name is PocketDeadlock. This is a focused native utility with direct action labels, visible file variants and game state, and plain recovery guidance. Impeccable and Taste guide usability and hierarchy while preserving familiar Windows affordances. Visual tokens belong to [DESIGN.md](DESIGN.md).

## Evidence on Hand

The source includes the WPF interface, three catalog adapters, local persistence, profile transfer, VPK validation, mod revisions, a signed app updater, and a self-test runner. [README.md](README.md) contains user and build instructions. [Проверка.md](Проверка.md) is the separate verification record. This document does not assert a test count, antivirus result, or successful live-game validation.

## Product Principles

- Keep selection, downloading, and application distinct and explain pending changes.
- Preserve the chosen variant; request an explicit choice when identity is ambiguous.
- Scope game changes to the manager's directory and marked block, with recovery evidence.
- Keep useful local state available when a remote catalog is unavailable.
- Explain portable references and local-file ownership without promising unavailable content.

## Accessibility & Inclusion

Preserve readable text in both themes, visible keyboard focus, native tab navigation, automation names for important controls, and search with Ctrl+F. Controls and status messages must remain understandable in Russian and English. System defaults and saved preferences must remain predictable.
