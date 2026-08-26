# Public release audit

Audit date: 2026-08-24.

## Result

The project has the core legal/privacy package needed for an MIT public source release: project MIT license, bilingual README/privacy/disclaimer/third-party documents, dependency inventory, packaged package-license files, and reproducible verification/release scripts. This is an engineering audit, not legal advice.

The public source tree uses a conventional `src/PrivacyAudit`, `tests`, `scripts`, and `docs` layout. Generated executables, symbols, archives, test reports, SDK caches, IDE state, local databases, logs, dumps, environment files, and signing-key formats are excluded by `.gitignore`; previously tracked build outputs are removed from the releaseable source set.

## Controls verified

1. **Licenses and distribution documents** — the compact release ZIP contains the EXE, root `LICENSE`, `PRIVACY.md`, `SECURITY.md`, bilingual `DISCLAIMER.md`, `THIRD_PARTY_NOTICES.md`, package license files/NuGet manifests and .NET notices. README visibly repeats the AS IS warning and links to the full disclaimer; the disclaimer explicitly covers absence of warranty, data loss/corruption, system damage, financial loss and other direct or indirect consequences, consistently with the MIT limitation of liability. Project documentation and demo media are source-only and are not duplicated into the binary distribution. Optional YuNet and Image Safety Classifier XS are MIT and SHA-256 pinned; their unmodified source mirrors and attribution files remain under `third_party/` in the repository and are not bundled into the installer. ImageSharp 3.1.12 uses the Six Labors Split License; its Apache-2.0 grant applies while this project is distributed as MIT open source.
2. **Network boundary** — audits, detectors, SQLite, similarity, and personal ML are offline. Model downloads are explicit YuNet or Image Safety XS actions from pinned URLs, with repository mirrors used only after upstream failure or verification mismatch. Repository/author URLs open only after user clicks.
3. **Process lifecycle** — no service, tray icon, autostart, scheduler task, shell extension, or child analysis process exists. Close cancels all in-process work; packaged smoke-test asserts the process and children terminate.
4. **Writes** — application-owned persistent writes are limited to `%LOCALAPPDATA%\NotBadPrivacyDetectorAgent`. A self-contained .NET single-file runtime can additionally use its OS-managed temporary native-bundle extraction cache. Build/test artifacts stay in the source workspace and are not created by the distributed app.
5. **Input and deletion safety** — scan roots come from a folder picker; search input is capped at 1024 characters; content reads and image/archive work use quotas; SQL uses parameters. Cleanup requires countdown confirmation, accepts only the exact owned root, rejects foreign paths, and does not follow reparse points.
6. **Scale and resumability** — UI presentation is virtualized/paged and people-scan results are reusable checkpoints. However, the scan coordinator, global finding list, sorting/filtering, snapshots, and SQLite save still materialize whole result sets. Therefore the current version is **not certified for millions of findings**, and most deep detector passes can be cancelled with partial progress but cannot resume at an exact persisted cursor. This is a documented scalability limitation, not a passed requirement.

## Release gate

Run `scripts\package-release.ps1`. It must complete tests, uncompressed self-contained single-file publish, optional Authenticode signing when protected credentials are configured, startup/termination smoke-test of the exact release bytes, compact runtime/legal document collection, license collection, ZIP creation, SHA-256 generation, and a generated `publish-github-release-v<version>.txt` checklist. An unsigned release must be reported as `NotSigned`; its adjacent SHA-256 sidecar is mandatory. A self-signed public release is prohibited because it creates no trust on a clean user computer. Publish the ZIP and SHA-256 sidecar under the canonical `v<version>` tag as a normal (not Draft/Pre-release) GitHub Release so the in-app manual update check can discover it.
