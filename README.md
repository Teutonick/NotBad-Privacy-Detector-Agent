# NotBad Privacy Detector Agent

PrivacyAudit can prepare an anonymized incorrect-detection report from any finding. It builds and previews the report locally, strips identifying path components and exact technical values, and only then opens a pre-filled public GitHub Issue when explicitly requested. Nothing is uploaded automatically.

Only one application instance is allowed per computer. When a previous audit is restored, the window opens first and shows staged progress while the snapshot is loaded asynchronously.

[Русская версия](README.ru.md)

> **Find what you forgot — before someone else does.**

NotBad Privacy Detector Agent is a portable, local-first Windows tool for digital archaeology. It maps forgotten layers of a PC — personal data, secrets, credentials, old application traces, archives, photos, metadata and copies — and explains why each finding deserves attention.

![NotBad Privacy Detector Agent demo](docs/assets/notbad-privacy-demo.gif)

## Why it is different

Most cleaners ask what can be deleted. NotBad Privacy Detector Agent asks a more useful question: **what is here, how exposed is it, and where did it come from?**

- **Understand, do not guess.** Every finding keeps its path, category, scanner, score and evidence.
- **Investigate on demand.** File Provenance Analyzer runs only when you request it for one object; it is never added to a mass scan.
- **Adapt locally.** Optional personal AI recommendations learn from your 👍/👎 ratings using only features PrivacyAudit already calculated.
- **Stay private.** Audits, ratings, provenance and model files remain on this PC. There is no account, telemetry, cloud inference or background service.
- **Portable by design.** The EXE does not install itself or add startup entries. Delete the EXE manually when you are finished.

### Application file history

Windows Jump List containers appear in an aggregated **Application history** workspace, not as hundreds of dangerous files. The workspace becomes available after an audit. **Analyze application history** parses Automatic/Custom Destinations on demand, merges known AppIDs into applications, hides empty groups, preserves Cyrillic ANSI/Unicode paths, and matches remembered objects with current findings. The default filter focuses on audit findings; availability, menu pinning, risk, size, age and search filters expose more history, with each option explained in the workspace legend. Each remembered object can be rated with 👍/👎 and scored by the same local personal-recommendation model using history-specific metadata; AI sorting ranks both objects and applications, while an application score is derived from its strongest objects rather than a broad container label. Files support Details/open/folder/recycle-bin/copy-path actions; a temporarily busy Windows clipboard is retried on a background STA thread and reported in the status line instead of blocking or crashing the UI. Folders open directly, and missing targets remain read-only historical paths. The parser and recommendations use no online AppID directory and send nothing outside the computer.

Incorrect-detection reports require a plain-language explanation of what is wrong. The preview puts that request at the top and pre-fills `privacy-audit`, `incorrect-detection`, and correction-type labels in the GitHub issue URL for later sorting; publication remains an explicit user action.

## What it can reveal

- personal data: names, emails, strict-format phones, addresses, explicit Telegram links and validated identifiers;
- secrets and credentials: known API-token formats, explicit key assignments, JWTs, private keys, connection strings and strict mixed-case ASCII entropy candidates;
- application leftovers: profiles, sessions, caches, history, logs, configuration and possible orphaned data;
- media exposure: GPS/EXIF, camera serials, document photos requiring combined paper/text/geometry evidence, faces and people indicators;
- archives and duplicates: sensitive files inside ZIP/JAR/APK-style containers, similar documents and images;
- Windows traces: Recent items, Jump Lists, bounded filesystem areas and other local exposure surfaces;
- provenance evidence: application mappings, formats, neighboring files, Registry correlations and available forensic traces.

## A simple workflow

1. Choose **Quick**, **Full** or **Custom** scan.
2. Review findings by risk, category, age, size or **✨ AI priority**.
3. Open Details to see scores, evidence and existing scanner results — without re-reading the file just to train a model. Use the back-arrow button or your mouse's browser-style Back button to return to the same list and scroll position.
4. For a file that needs context, choose **Investigate provenance**. The result is cached locally and reused until the file changes.
5. Rate findings with 👍 or 👎. After enough balanced ratings, the local recommendation model learns what you personally tend to care about.

When you stop a large audit, cancellation is requested asynchronously so the interface stays responsive. A dedicated indeterminate progress bar shows that the scanner is still unwinding; the partial report is kept when cancellation completes.

Archive contents are inspected only after choosing **Inspect archives**. This keeps the normal audit lightweight; inspected archives remain in the findings list with their sensitive-entry severity.

## Local AI recommendations

This is one small CPU-only ML.NET binary classifier, not a pretrained neural network. It reads only already-calculated numeric and categorical audit features (for example exposure, age, category, PII/secret indicators, EXIF/GPS and similarity counts). It never uploads ratings, vectors or model weights, and it never replaces Exposure, Personal Data, Secret or Archive Privacy scores. A model is trained only after the minimum balanced feedback is available and is invalidated automatically when the feature schema changes.

## Privacy and safety boundaries

- normal scans are offline; network access happens only after an explicit action such as downloading the optional YuNet model or opening a browser link;
- Windows Recent may contain real `.lnk` artifacts whose original target was deleted. The audit labels these stale shortcuts explicitly; it does not claim that the deleted target still exists.
- no Windows service, scheduled task, startup entry, tray worker or persistent analysis process;
- junctions/reparse points are not followed by default;
- SQLite stores metadata and findings, not arbitrary file contents;
- no automatic deletion of user files or Registry changes; any Recycle Bin action is explicit and separately confirmed;
- cleanup controls remove only application-owned data and clearly explain the scope before countdown confirmation.

## Download and run

Download the latest portable `NotBadPrivacyDetectorAgent.exe` from Releases, place it in a folder you control, and run it. No installer is required. Standard mode is enough for user-accessible locations; **Restart with administrator rights** can be used when you explicitly want to inspect protected system areas.

## Build and package locally

Product code lives under `src/PrivacyAudit`, tests under `tests/PrivacyAudit.Tests`, and release automation under `scripts`. Generated `bin`, `obj`, `artifacts`, and `dist` content is intentionally excluded from Git.

```powershell
.\scripts\verify.ps1
.\scripts\package-release.ps1
```

The first command runs tests, publishes the single-file EXE and performs a startup/exit smoke test. The second creates a compact versioned ZIP containing only the portable EXE, runtime/legal documents, third-party license files and a SHA-256 manifest. Project documentation and demo media remain in the source repository.

## Documentation and legal

[Project map](docs/PROJECT_MAP.md) · [Architecture](docs/ARCHITECTURE.md) · [Testing](docs/TESTING.md) · [Building](docs/BUILDING.md) · [Privacy policy](PRIVACY.md) · [Security](SECURITY.md) · [Disclaimer](DISCLAIMER.md) · [Third-party notices](THIRD_PARTY_NOTICES.md)

NotBad Privacy Detector Agent is distributed under the [MIT License](LICENSE). Third-party components and their licenses are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
