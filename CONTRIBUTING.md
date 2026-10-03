# Contributing to PocketDeadlock

Fixes, documentation and Russian/English translations are welcome. Start with [the product scope](PRODUCT.md), then [development and architecture](docs/development.md). Larger changes should begin with an issue describing the user scenario.

## Report an issue

Use [the issue chooser](https://github.com/AlexUner/pocket-deadlock-mod-manager/issues/new/choose). Include the manager version, Windows version, reproducible steps, expected behavior and actual result. If a specific mod is involved, include its public source URL and file variant.

Do not post passwords, cookies, private signing keys, browser profiles, whole `state.json` files or unredacted personal paths. Report exploitable vulnerabilities using [SECURITY.md](SECURITY.md). Antivirus detections have a separate issue form and [verification record](docs/antivirus.md).

## Make a change

1. Fork the repository and create a branch from `main`.
2. Keep one pull request focused on one problem or user scenario.
3. Build on Windows with the .NET 10 SDK and locked dependencies; follow [the build commands](docs/development.md#build-and-test).
4. Explain the resulting behavior and relevant validation. GitHub Actions runs the isolated checks and verifies the ordinary package.
5. Open a pull request. For UI changes, provide redacted screenshots in the relevant language/theme and check keyboard navigation.

Developer diagnostic builds are separate from the shipped app. Prefer the isolated GitHub CI run for their checks; do not run diagnostics against an active personal game or bypass an antivirus alert to finish a test.

## Project conventions

- Preserve selection-versus-application behavior, the chosen file variant and existing game files.
- Add focused checks for meaningful changes to archives, paths, VPK parsing, game configuration, account sessions or updates. Documentation-only edits need link/content validation.
- Keep RU/EN text consistent in `Translations.cs` and `Localization.cs`. Authored Russian normally uses `е` rather than `ё`; preserve exact names and quotations.
- Use ordinary `-` in authored Markdown. Do not rewrite code, URLs or identifiers for typography.
- Preserve WPF focus, keyboard and native-dialog behavior. See [DESIGN.md](DESIGN.md) for the existing visual system.
- Never commit private keys, credentials, personal library data, account sessions, or generated release archives.
- Do not change immutable release assets, signing identity or the update channel as part of an unrelated fix.

Be respectful and describe concrete problems. Contributions are made under the repository's [MIT license](LICENSE); third-party mods and dependencies retain their own licenses. There is no guaranteed review or support response time.
