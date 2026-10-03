# PocketDeadlock and Deadlock Mod Manager

[Project overview](../README.md) | [Русский](comparison.ru.md)

**Checked on 3 October 2026.** PocketDeadlock refers to the published [0.6.6 release](https://github.com/AlexUner/pocket-deadlock-mod-manager/releases/tag/v0.6.6). The other project is [deadlock-mod-manager/deadlock-mod-manager](https://github.com/deadlock-mod-manager/deadlock-mod-manager), with its source reviewed at [52872bb](https://github.com/deadlock-mod-manager/deadlock-mod-manager/tree/52872bb6db04a3b02c0f6d6fe173fd7d5dd303d3). Source presence does not prove that a feature is enabled in every published build. We did not run the competitor or benchmark either application.

## Different focus

PocketDeadlock is a native Windows utility focused on ready-made VPK mods. Deadlock Mod Manager is a broader project with desktop, web, API and additional modding tools. Browsing, downloads, load order, localization, profiles and updates overlap; these are not exclusive PocketDeadlock features.

| Area | PocketDeadlock 0.6.6 | Deadlock Mod Manager: documentation / reviewed source |
| --- | --- | --- |
| Desktop | Windows x64; C# / WPF; self-contained ZIP | Windows and Linux packages; Tauri / React / TypeScript. Linux support is documented as best effort. |
| Catalog architecture | Direct GameBanana access plus Deadlocker and DeadlockMods adapters; one combined local index | GameBanana-backed catalog through the project's API and database. |
| Search | Saved disk index, startup refresh and remote enrichment; local filtering by hero/type | API search, category filters and frontend query caching. No speed comparison was performed. |
| Profiles and sharing | Local profiles, JSON export and `PD1` keys containing catalog references and settings; no profile upload | Local profiles plus a share dialog that uploads a profile through `shareProfile` and returns an ID. The API checks a feature flag. |
| Language | Russian and English; defaults follow Windows | More languages and a community translation workflow. |
| Extra tools | Focused on ready-made mods; no crosshair editor, VPK authoring or plugins | Source includes crosshair tools, a plugin system and VPK tooling. |
| Updates | Mod revisions/rollback; signed app feed and checked ZIP installation | Mod-update checks and the Tauri app updater. Both projects support updates. |

## What actually distinguishes PocketDeadlock

- **Native WPF UI and Windows-only distribution.** This is a platform choice, not evidence that it uses less memory or starts faster.
- **Combined provider adapters with a local index.** Searches use saved records while sources refresh. Coverage still depends on the providers; some community entries need manual import.
- **A self-contained sharing key.** The key carries the loadout references and choices instead of looking up a stored profile ID. It can be decoded without our service; fetching the mod files needs the source websites. Local-only VPKs cannot be embedded in it.
- **Explicit selection, application and recovery.** Library changes stay pending, configuration is backed up, and retained mod revisions can be restored. These are documented PocketDeadlock behaviors, not claims that the other project lacks safeguards.

Choose PocketDeadlock if this focused Windows workflow suits you. The other project has broader platform coverage and extra tools. Full feature parity is not a goal of the current scope.

## Antivirus is a verification result

PocketDeadlock started after an antivirus warning disrupted the existing setup. That origin does not establish that either application is malicious, nor that a different implementation will always avoid a detection.

The 0.6.6 scan and checked startup/update workflows had no new Kaspersky detection on one computer without active exclusions. An earlier PocketDeadlock diagnostic process did trigger a detection. See the [exact package and limitations](antivirus.md). We make no current antivirus claim about the competitor.

## Sources

- Desktop stack and Linux caveat: [project README](https://github.com/deadlock-mod-manager/deadlock-mod-manager/blob/52872bb6db04a3b02c0f6d6fe173fd7d5dd303d3/README.md); [installation guide](https://github.com/deadlock-mod-manager/deadlock-mod-manager/blob/52872bb6db04a3b02c0f6d6fe173fd7d5dd303d3/apps/docs/content/docs/using-mod-manager/installation.mdx).
- Discovery and languages: [features guide](https://github.com/deadlock-mod-manager/deadlock-mod-manager/blob/52872bb6db04a3b02c0f6d6fe173fd7d5dd303d3/apps/docs/content/docs/using-mod-manager/features.mdx).
- Profiles: [share dialog](https://github.com/deadlock-mod-manager/deadlock-mod-manager/blob/52872bb6db04a3b02c0f6d6fe173fd7d5dd303d3/apps/desktop/src/components/profiles/profile-share-dialog.tsx); [profile API](https://github.com/deadlock-mod-manager/deadlock-mod-manager/blob/52872bb6db04a3b02c0f6d6fe173fd7d5dd303d3/apps/api/src/routers/v2/profiles.ts). The features guide still calls profiles upcoming; the reviewed source already implements them. Availability can depend on the service feature flag.
- Updates: [app-update hook](https://github.com/deadlock-mod-manager/deadlock-mod-manager/blob/52872bb6db04a3b02c0f6d6fe173fd7d5dd303d3/apps/desktop/src/hooks/use-check-for-updates.ts); [mod-update hook](https://github.com/deadlock-mod-manager/deadlock-mod-manager/blob/52872bb6db04a3b02c0f6d6fe173fd7d5dd303d3/apps/desktop/src/hooks/use-check-updates.ts).
- Additional tools and architecture: [desktop source](https://github.com/deadlock-mod-manager/deadlock-mod-manager/tree/52872bb6db04a3b02c0f6d6fe173fd7d5dd303d3/apps/desktop/src); [packages](https://github.com/deadlock-mod-manager/deadlock-mod-manager/tree/52872bb6db04a3b02c0f6d6fe173fd7d5dd303d3/packages).
- PocketDeadlock: [product scope](../PRODUCT.md), [player guide](guide.md), [implementation map](development.md), [verification record](../Проверка.md).
