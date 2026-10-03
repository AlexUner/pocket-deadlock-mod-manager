# Security policy

## Supported versions

Security fixes target the latest published stable release. Older releases and developer diagnostic builds are not maintained as separate security branches. Download releases from [this repository](https://github.com/AlexUner/pocket-deadlock-mod-manager/releases/latest).

## Report a vulnerability privately

Use GitHub's **[Report a vulnerability](https://github.com/AlexUner/pocket-deadlock-mod-manager/security/advisories/new)** form for exploitable issues such as archive path traversal, unsafe game-file writes, account-session leakage or update verification bypass.

Include the affected release, expected boundary, reproduction steps and a minimal synthetic example. Do not include real account credentials, cookies, signing keys or unrelated user files. Avoid posting exploitable details in public issues until they have been assessed. No response-time or bounty commitment is offered.

Antivirus detections without an identified exploit can use the [redacted antivirus issue form](https://github.com/AlexUner/pocket-deadlock-mod-manager/issues/new?template=antivirus.yml). See [the current verification record](docs/antivirus.md).

## Boundaries

GameBanana authentication uses its official website and a separate embedded-browser profile. Stored sessions are encrypted for the current Windows user. Profiles and sharing keys contain references and choices, not account cookies or mod content.

App updates verify ECDSA-signed metadata and the package SHA256 before staging and again before installation. This signature is not Authenticode. Archive, checksum and VPK structure checks do not establish that a downloaded mod is benign or compatible with the current game.
