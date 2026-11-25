# 0019 — Installer, updates and crash reports

**Date:** 2026-11-25
**Status:** Accepted (Milestone 07, phase E)

## Context
- FrameForge was only ever run from a build of the source: it needed .NET installed, and had no Start menu entry,
  uninstaller or upgrade path.
- When it crashed, the details went into the log and nothing told the user afterwards.
- The milestone asks for an installer and update strategy, and for an opt-in crash-reporting design.
- The app promises that nothing leaves the computer (ADR 0018), and its only user is the developer (PRODUCT.md).

## Decision

### Release build (`build/publish.ps1`)
- **What it builds:** a self-contained `win-x64` publish with ReadyToRun (code compiled ahead of time), in
  `artifacts\` (ignored by git).
- **What it does:**
  1. Runs the tests, unless `-SkipTests` is given.
  2. Stamps the version, from `-Version` or `Directory.Build.props`.
  3. Copies the .NET runtime's licence and notices from the runtime packs into `licenses\`, because a self-contained
     app ships the runtime.
  4. Makes a portable ZIP.
  5. Builds the installer, when Inno Setup 6 is installed.
- **Measured** on the 1,010-photo test library, from start to a filled grid:
  - **release: 1.55–1.69 s**
  - debug build: 2.27–3.30 s (it needs .NET installed and compiles on start)
  - memory: the same
- **Sizes:** the app folder is 180 MB, the ZIP 79 MB and the installer 78 MB (56 MB without the Visual C++ runtime, see below).

### Installer (Inno Setup 6, `build/FrameForge.iss`)
- **Per user, no admin prompt:** installs into `%LOCALAPPDATA%\Programs\FrameForge`, with a Start menu shortcut, an
  optional desktop shortcut and an entry in Windows' installed apps.
- **Upgrade:** run a newer installer.
  - It closes FrameForge if it is running, empties the app folder (so files from an older version don't linger) and
    installs the new files.
  - The library (`%LOCALAPPDATA%\FrameForge`) is a separate folder, and the installer never touches it.
- **Uninstall:** asks whether to remove the library too.
  - The default answer is No.
  - A silent uninstall never asks, and always keeps it.
  - Photos are never touched.
- **The app id** in the script must never change: it is how Windows recognises the same app across versions.

### The Microsoft Visual C++ runtime (added in M07 A0)
**The problem:** the native ONNX Runtime (`onnxruntime.dll`), and LibRaw from phase A, need the Visual C++ 2015–2022
runtime (`MSVCP140`, `VCRUNTIME140`, `VCRUNTIME140_1`). A self-contained .NET app doesn't include it, and a clean
Windows install may not have it.

**The installer:**
- **Bundling:** the installer bundles Microsoft's `vc_redist.x64.exe`. `build/publish.ps1` takes it from
  `build\cache\`, which git ignores; it was downloaded by hand from Microsoft's permanent link. The script refuses a
  file that isn't validly signed by Microsoft Corporation.
- **When it runs:** only if Microsoft's documented registry key
  (`HKLM\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X64`) shows no runtime, or an older one than the bundled
  version. It then asks for administrator permission once; FrameForge's own install still needs none.
- **The bundled version:** 14.44.35211, which adds 22 MB to the installer.

**The app** treats a runtime that can't load as "AI is off", not as a crash (`OnnxNative.IsLoadFailure`):
- The telemetry switch (`OnnxPrivacy`) and model loading catch it.
- The AI settings show "the AI runtime could not start on this computer…".
- Checked by running the evaluation tool with `onnxruntime.dll` removed. That found a further case: Windows 11's own,
  older `onnxruntime.dll` (1.17, for Windows ML) in System32 can load instead, and fail with "does not support API
  version 31". Any failure to initialise ONNX Runtime's native layer now counts.

### Updates: manual, no online check
- **How to update:** install the newer installer over the old one.
- **No update check, and no update service:** that would be a network connection the user didn't start, which goes
  against ADR 0018.
- **Where the version shows:** About, and Windows' installed apps.

### Safe database upgrades (`DatabaseMigrator`)
- **Before an upgrade:** when an existing database has migrations to apply, it is copied first to
  `%LOCALAPPDATA%\FrameForge\backups\`.
  - The copy uses SQLite's online backup, which is consistent even with the WAL journal.
  - The file is named after the time and the last migration applied before the upgrade.
  - The newest 3 copies are kept.
- **No copy is made** of a new database, or of one that is up to date.
- **If the upgrade fails,** start-up says where the copy is (`DatabaseUpgradeException`) instead of just "could not
  start".

### Crash reports: local only (`FileCrashReports`, `CrashNoticeViewModel`)
- **When a report is written** (to `%LOCALAPPDATA%\FrameForge\crash-reports\`):
  - an unhandled error that closes the app (any thread)
  - an error on the UI thread that the app survives
  - a failure to start
- **What a report contains:**
  - the version, Windows and .NET versions, and how long the app had been running
  - the error with its stack trace
  - the last 200 log lines
- **Redaction:**
  - The user-profile folder becomes `%USERPROFILE%`.
  - The computer name and user name become `<computer>` and `<user>`, computer name first, since it often contains
    the user name.
  - Logs already avoid image content (CLAUDE.md).
- **Storage:** the newest 10 reports are kept. Writing a report never throws.
- **On the next start,** a note at the bottom right says a report was saved and that nothing was sent.
  - It offers **Show report** (File Explorer), **Copy report** and **Dismiss**.
  - Dismiss renames the reports to `*.seen.txt`, so they don't come back.
- **Sharing** a report is entirely the user's choice: they paste or attach it themselves.
- **Testing the crash path:** `FRAMEFORGE_CRASH_TEST=1`, which works only with `FRAMEFORGE_DATA_DIR` set (test data),
  crashes the app on a worker thread 3 s after start-up.

### Design for any future upload (not built)
If reports are ever sent automatically, these are the conditions:
- **Opt-in:** off by default, asked once with a plain explanation, and changeable in Settings.
- **Transparent:** each report is shown in full before it goes, with Send and Don't send buttons.
- **Same content:** what the local report holds (redacted), and no photos, file lists or settings beyond the report.
- **Disclosed:** the destination, owner, retention and deletion are stated in About and in `docs/licence-checklist.md`.
- **Promise updated:** the "nothing is sent" sentences in About, the user guide and ADR 0018 change to match.

## How it was checked
- **Tests:**
  - an upgrade copies the database (with its data) first
  - a new database isn't copied
  - only 3 copies are kept
  - report contents, redaction, unseen and seen, and keeping 10
  - the notice: show, copy and dismiss
- **On this PC, in a scratch folder, with test data** (`e-install-test.ps1`), against the real library's size taken
  before and after:
  1. Installed 0.1.0: registered, with the Start menu shortcut and 13 licence files.
  2. Started on test data.
  3. Upgraded to 0.1.1: the registered version became 0.1.1.
  4. Crash test: the app closed, one report was written (contents as above), and on the next start the note showed.
     Dismiss cleared it.
  5. Silent uninstall: the app folder, shortcut and registration were removed, and the test data was kept.
  - **The real library was unchanged:** 195,896,880 bytes before and after.
  - The uninstaller's "remove the library" answer wasn't run, because it would delete the real library.

## Consequences and limits
- **The "runtime missing" path wasn't tested:** this PC has a newer runtime (14.51), and removing it would affect other
  software. Tested instead:
  - Installing here doesn't unpack or run Microsoft's installer (the install log has no mention of it).
  - The script compiles with the check.
  - The app copes with a runtime that can't load.
- **Not code-signed:** Windows SmartScreen will warn "unknown publisher" when the installer first runs on another PC.
  Signing needs a certificate, a cost decided if the app is ever shared.
- **No delta updates:** each update is a full installer of about 78 MB.
- **64-bit only:** `win-x64`, Windows 10 1809 or later.
- **A failed upgrade** may leave the database partly migrated. The user restores it by copying the backup back. FrameForge
  doesn't do that automatically, because a wrong automatic restore would lose newer work.
- **Inno Setup's terms:** free to use. Its author asks commercial users (for-profit, above US$5,000 a year) to buy a
  licence, which isn't strictly required. Installers built with it may be distributed. See the checklist.
