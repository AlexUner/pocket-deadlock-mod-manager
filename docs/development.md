# Development and releases

[Project overview](../README.md) | [Contributing](../CONTRIBUTING.md)

## Build and test

Build on Windows with the **.NET 10 SDK**. The project targets `net10.0-windows`, uses WPF, and pins SharpCompress and WebView2 in `packages.lock.json`. Account login uses the installed Microsoft Edge WebView2 Runtime. Run from this source directory:

```powershell
dotnet restore -r win-x64 --locked-mode
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o publish
.\publish\PocketDeadlock.exe
```

Developer checks are excluded from the ordinary build. Prefer the isolated GitHub Actions run for diagnostics. An earlier local developer diagnostic process triggered a Kaspersky behavioral detection; do not bypass an alert to complete these checks. If you need an isolated developer run on your own test machine, build it explicitly into a dedicated directory:

```powershell
dotnet build -c Release -p:PocketDiagnostics=true -o artifacts/diagnostics
.\artifacts\diagnostics\PocketDeadlock.exe --self-test .\test-output --offline
.\artifacts\diagnostics\PocketDeadlock.exe --self-test .\test-output
```

Offline tests use synthetic game and catalog fixtures. The default run also exercises live services and needs network access. Each run creates its own subdirectory under the supplied target; `test-results.txt` is written at the target, and a failure writes `FAILURE.txt`. The runner does not apply to the detected real game. A passing runner does not establish live-game mod compatibility. See [Проверка.md](../Проверка.md) for recorded results.

## Architecture

| Source | Responsibility |
| --- | --- |
| `App.xaml`, `MainWindow.xaml`, `MainWindow*.cs` | WPF styles, layout, catalog/library workflows, downloads, profiles, settings, update UI |
| `Localization.cs`, `Translations.cs` | System-language defaults, Russian/English text, theme resources |
| `Models.cs` | Catalog, library, profile, activity, revision records |
| `GameBanana.cs`, `Catalogs.cs` | `IModCatalog`, origin API access, community adapters |
| `HybridCatalogs.cs` | Disk index, local queries, background refresh, remote-search enrichment |
| `UnifiedCatalog.cs`, `CatalogTaxonomy.cs`, `MainWindow.Catalog.cs` | Combined identities, hero aliases, independent searchable hero/type filters and sorting |
| `ModStorage.cs` | Atomic state writes, archives, hashes, VPK validation, revisions, rollback |
| `GameInstall.cs` | Steam detection, running-game checks, scoped copies and configuration patching |
| `Profiles.cs` | Profile selection, JSON transfer, PD1 encoding and validation |
| `ModUpdates.cs` | Chosen-variant matching and library updates |
| `AppUpdates.cs`, `UpdateKey.cs` | Signed releases, staging, application replacement; public verification key only |
| `SelfTests.cs` | Synthetic and optional live-service checks |
| `GameBananaAccount.cs`, `GameBananaLoginWindow.cs`, `MainWindow.Account.cs` | Official website login, encrypted session, cookie boundaries and content filter |
| `ThumbnailCache.cs`, `CatalogThumbnail.cs`, `CatalogTilePanel.cs`, `MainWindow.Tiles.cs` | Lazy cover loading, shared cache, adaptive tiles and keyboard navigation |

[PRODUCT.md](../PRODUCT.md) records scope. [DESIGN.md](../DESIGN.md) and [.impeccable/design.json](../.impeccable/design.json) record the native visual system. Archive handling uses [SharpCompress](https://github.com/adamhathcock/sharpcompress) 0.50.4 under MIT; [its license notice](../SharpCompress-LICENSE.txt) is included. Embedded login uses Microsoft WebView2 1.0.4258.31; its license and notices are included.

## GitHub release automation

Pull requests and pushes to main run Windows compilation, isolated offline checks and ordinary package verification. The artifact is a portable ZIP with runtime and dependency licenses; developer diagnostics and owner data are rejected.

The **Windows release** workflow accepts an existing stable `vMAJOR.MINOR.PATCH` tag matching the project version. A tag push creates a draft release. Manual dispatch can explicitly publish it. It runs the checks again, builds the ordinary runtime, packages it, signs `update-feed.json`, and uploads that feed, the ZIP and `SHA256SUMS.txt` together. Maintain `RELEASE_NOTES.md` before tagging: its text is included in both the release and the manager's update window. A published stable release becomes available to desktop clients through the existing latest-release feed; drafts do not.

Before the first automatic signed release, configure the `RELEASE_SIGNING_KEY` secret in the GitHub **releases** environment with the existing ECDSA private key. It must match `UpdateKey.cs`; the script rejects another key. Never commit the private key. GitHub receives this credential only when the maintainer configures that secret. Manual workflows become available once their file is on the default branch. See [GitHub workflow instructions](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-run-a-workflow).
