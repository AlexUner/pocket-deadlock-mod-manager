# Antivirus verification and reports

[Project overview](../README.md) | [Original record in Russian](../Проверка.md)

## Published package: 0.6.6

Checked on **3 October 2026** with Kaspersky 21.26 on one Windows x64 computer.

| Check | Recorded result |
| --- | --- |
| Scan of extracted release files | 413 objects, 413 OK; 0 detections, suspicions, skipped objects, corruption or errors |
| Active scan exclusions / trusted-app rules | 0 / 0; the owner disabled the earlier trust rule |
| Ordinary startup, catalog and library | No new Kaspersky detection or application crash observed in the checked interval |
| Public-channel update from 0.6.2 to 0.6.6 | Installer success, restart completed; all 412 installed files matched the published package |
| Owner's local state and game files | Mod selection and content, configuration and existing addons preserved; no set applied or game launched by that check |

File scan: 04:28:36-04:28:40 Moscow time. Ordinary run: 04:28:40-04:30:04. Public update started at 04:32:27. Protection and System Watcher remained running. The scan was report-only, without treatment/deletion; scan acceleration was disabled for this scan. Global protection was not disabled.

Package: [PocketDeadlock-0.6.6-win-x64.zip](https://github.com/AlexUner/pocket-deadlock-mod-manager/releases/tag/v0.6.6), 67,002,520 bytes, 412 files.

```text
SHA256  5D4D213BF24A7C88A0B57D954B6BD7E665CD25472D2AB554DBACA63722C540BA
```

GitHub Actions also ran 158 isolated checks and package validation: [CI](https://github.com/AlexUner/pocket-deadlock-mod-manager/actions/runs/37085976982), [release build](https://github.com/AlexUner/pocket-deadlock-mod-manager/actions/runs/37086100907). Automated checks do not certify antivirus acceptance.

## Earlier detection and limitations

An earlier developer diagnostic process triggered `PDM:Trojan.Win32.Generic`. It was not classified as a false positive by Kaspersky, and the files were not submitted to its laboratory. Ordinary releases exclude the developer test and image-capture code involved in those development workflows.

The checks above cover a particular package, computer and finite set of actions. They do not guarantee acceptance on other computers or in every workflow, and do not establish live-game compatibility for every mod. ECDSA authenticates update metadata; it is not a Windows Authenticode certificate or an antivirus verdict.

## If you see an alert

Stop using the flagged build while the cause is investigated. Record the app version, exact detection name, whether the flagged file belongs to the manager or a downloaded mod, and the action that triggered it. Obtain the release only from this repository and compare the ZIP hash with that release's `SHA256SUMS.txt`; a matching hash confirms package identity, not safety.

Use the [antivirus report form](https://github.com/AlexUner/pocket-deadlock-mod-manager/issues/new?template=antivirus.yml) for a redacted report. Do not attach account cookies, browser profiles, private keys, full local state or unredacted personal paths. A suspected exploitable vulnerability belongs in [private security reporting](../SECURITY.md).

The installation instructions do not require disabling protection or adding the application to a trusted list. Any further release verification should record whether exclusions or trust rules were active.
