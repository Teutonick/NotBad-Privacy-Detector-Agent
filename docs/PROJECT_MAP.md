# Project map — NotBad Privacy Detector Agent

## Documentation boundaries

`README.md` and `README.ru.md` are synchronized product-facing entry points. They prioritize the product promise, major differentiators, a short end-user workflow, privacy/safety boundaries, download guidance and links to deeper material. README mentions only major user-visible capabilities in concise product language; it does not catalog individual controls, event handlers, threading details, parser rules, schemas, thresholds or implementation history.

Engineering and AI/vibecoding notes belong under `docs/` and must link to their related documents:

- `PROJECT_MAP.md` owns repository layout, component ownership, navigation/workspace inventory and file-level routing;
- `ARCHITECTURE.md` owns runtime behavior, data flow, algorithms, lifecycle, UI mechanics and component contracts;
- `TESTING.md` owns verification scope and release gates;
- `BUILDING.md` owns build and packaging instructions;
- `PERSONAL_MODEL_USER_DESCRIPTION.md` owns the canonical bilingual explanation of personal recommendations;
- root privacy, security, disclaimer and license documents own their respective user/legal guarantees.

When a feature changes, update its low-level canonical document first and add or revise README copy only when the change is a major product-level differentiator. Cross-link related low-level documents instead of duplicating long explanations.

## Anonymous incorrect-detection reports

The Details page exposes an **Anonymized report for GitHub** action for every `Finding`, including files, directories, text findings, and media. `IncorrectDetectionDialog` explains the local-only flow, validates a required 10–1000 character author explanation, and shows that exact text at the top of the mandatory preview before opening a pre-filled public GitHub Issue. `src/PrivacyAudit/Core/DiagnosticReportBuilder.cs` replaces paths with shapes, buckets size and score, allows only categorical identifiers, and never consumes raw scanner `MetadataJson`.

The sidebar footer also contains a purple rotating Recommendations card, driven by the existing inactive-window-aware rotation timer.

Application startup owns a named local mutex (`Local\NotBadPrivacyDetectorAgent`). A second launch exits with a localized message. Snapshot restoration starts only after the main window is shown; the restore prompt and asynchronous staged progress are handled by `MainWindow.TryRestoreSnapshotAsync`.

```text
PrivacyAudit.sln
├── src/PrivacyAudit/               product source; WPF app and single-file publish target
│   ├── PrivacyAudit.csproj
│   ├── App.xaml(.cs)               application resources, global crash handling, startup lifecycle
│   ├── Views/                      main window, dialogs, document viewer, sidebar control
│   ├── Services/                   localization, thumbnails, offline author-project definitions
│   ├── Converters/                 UI converters, glyphs, badges, rating presentation
│   ├── Assets/                     application icon and local onboarding images
│   ├── Core/                       models, scoring, filtering, detectors, diagnostics, pagination, metadata, similarity
│   ├── Models.cs                   Finding, RiskLevel, ScanContext, ScanProgress, application-history correlation fields
│   ├── ApplicationHistory.cs       bounded offline Jump List parser, ANSI/Unicode/UTF-8-repaired paths, merged AppIDs, path correlation, historical scoring and object-level personal-priority state
│   ├── CrashLogger.cs             persistent offline diagnostic and unhandled crash logger
│   ├── FindingFilter.cs            discrete size and age filtering logic
│   ├── FindingPagination.cs        virtualization and sorting
│   ├── ObservableRangeCollection.cs batch UI notifications
│   ├── ScanCoordinator.cs         isolated scanner execution
│   ├── TextExtractor.cs           plain text, code, and DOCX/XLSX/PPTX text extractor
│   ├── PiiDetector.cs             Luhn and issuer-aware card checks, INN, SNILS, Email, strict single-line Phone, Passport, Address, explicit Telegram links, FIO; standalone dates ignored
│   ├── SecretDetector.cs          known token formats, explicit key assignments, private keys, DB URIs, strict mixed-case ASCII entropy candidates
│   ├── CredentialConfigDetector.cs .env, npmrc, pip, NuGet, gradle, maven, docker-compose, kubeconfig, ssh, db configs
│   ├── IdentityTraceDetector.cs   Windows account, hostname, Git user/email detection in filenames and supported text/document contents; binary embedded paths ignored
│   ├── ArchiveInspector.cs        in-memory ZIP/JAR/NUPKG archive structure inspection & privacy scoring
│   ├── ExifMetadataExtractor.cs   GPS decimal coordinates, camera make/model/serial, software, author, last saved by
│   └── DocumentSimilarity.cs      TF-IDF vectorizer + Cosine similarity matching for text & Office documents
│   ├── PeopleDetection/            local YuNet face detection, document photo analyzer, perceptual image similarity
│   ├── DocumentDetector.cs        strict paper/text-band/geometry evidence; YuNet face only corroborates an ID document
│   ├── ImageSimilarity.cs         perceptual dHash (9x8) & aHash (8x8) difference matching without neural networks
│   ├── YuNetDetector.cs           on-device ONNX face detector
│   └── PeopleScanner.cs           local media scanner coordinator
│   ├── Scanners/                   independent scanner modules (Filesystem, Windows)
│   └── Storage/                    local SQLite persistence & snapshots
│   └── AppDataCleanupService.cs   scoped cleanup that rejects foreign roots and reparse traversal
├── tests/PrivacyAudit.Tests/       automated tests (120 unit tests)
├── scripts/verify.ps1              mandatory release verification
├── scripts/package-release.ps1     verified versioned ZIP + legal/privacy/license documents
├── Directory.Build.props           isolated build outputs, no lock against running release
├── docs/                           engineering documentation
│   └── PERSONAL_MODEL_USER_DESCRIPTION.md canonical RU/EN explanation; mandatory synchronization contract
└── dist/                           generated verified executable (ignored by Git)
```

`UI → ScanCoordinator → IPrivacyScanner[] → Finding[] → Pii/Secret/Config/Identity/Archive/EXIF Detectors → optional local Application History post-processing → TF-IDF & dHash Similarity Engine → SQLite + views`

Scanner errors are isolated. Cancellation returns a partial report. System culture selects Russian only for `ru-*`; every other culture falls back to English. Russian localization strictly employs a friendly and respectful informal tone ("ты") without bureaucratese or formal "Вы".

# UI, Legend and Tooltip Standards

- **Universal ToolTips**: Every button, dropdown, slider, checkbox, search field, and rating control must have a concise, user-friendly `ToolTip` explaining its purpose in non-technical terms.
- **Collapsible Form Legends**: Key workspace forms (Findings and Media) include a collapsible **«Legend & Information»** drawer explaining all active controls, filters, scanners, and ratings in the same wording as their tooltips.
- **Details return navigation**: opening an object by double-click records whether it came from the findings grid, findings tiles, or media tiles and stores the scroll offsets. Details exposes a small back-arrow button and accepts XButton1/XButton2, native XBUTTON down/up, browser-back app commands, and the browser-back virtual key, restoring the original tab/list and position across common mouse-driver mappings.
- **Accurate Detection Risk**: Jump List containers are technical exposure artifacts, not dangerous user files, and never receive a High/Critical finding. Risk is assigned to referenced objects from existing audit evidence; an unmatched historical path is promoted only for explicit sensitive-name/extension or network-path evidence.
- **Reorganized Findings Toolbar**: Upper row houses category and risk dropdowns, deep scanning triggers (PII, Secrets, Configs, Identity, Archives), stop button, and legend toggle; lower row holds discrete size/age sliders, screen exposure toggle, personal ML recommendation toggle, vertically-centered search box, and the reset button.

# Selection, deletion and cancellation UX

- `FindingsGrid`, `FindingsTileList` and `MediaTileList` use WPF extended selection (Shift ranges and Ctrl point selection).
- The context menu acts on the current list selection. Directory findings are explicitly marked and expose only **«Show in Explorer»**; the delete path rejects directories defensively. For files, a separate confirmation is required before selected files are sent to the Windows Recycle Bin; successful entries are then removed from the in-memory audit lists and snapshot without a post-delete verification pass.
- The Recent scanner records the actual `.lnk` artifact in Windows Recent, checks its recorded target without opening it, and labels a shortcut with a missing target as a stale Recent trace rather than implying that the target file still exists.
- Archive sensitivity is an on-demand inspection result, not a pre-scan category filter. The Findings toolbar keeps **«Inspect archives»** as the entry point; after completion the view automatically selects the localized archive-inspection filter and shows only inspected archives with sensitive contents, while archive severity badges remain available in details.
- Cooperative cancellation uses `CancellationTokenSource.CancelAsync()` from the cancel action, keeping the WPF dispatcher available for rendering while the scanner unwinds. An indeterminate cancellation progress bar and localized “please wait” status remain visible until the active operation has actually finished unwinding.

# Personal attention model

- `src/PrivacyAudit/Core/PersonalAttentionModel.cs` extracts versioned, content-free feature rows from findings and already-parsed application-history metadata, trains the single ML.NET SDCA logistic-regression model, batch-scores both sources, and manages the local model artifacts.
- `src/PrivacyAudit/Storage/AuditDatabase.cs` owns `ml_feedback`; labels are keyed by normalized file path and retain the finding id, timestamps, schema version, and feature snapshot.
- `src/PrivacyAudit/Views/MainWindow.xaml(.cs)` supplies 👍/👎/clear controls, personal-score sorting and recommendation filtering, background retraining/cancellation, and model/history reset controls.
- The Details view opens a localized **How AI recommendations work** window. `docs/PERSONAL_MODEL_USER_DESCRIPTION.md` is its canonical copy; changes to features, learning policy, score semantics, privacy/storage, retraining, or audit influence must update both copies in the same change and are release-blocking otherwise.
- The first normal launch opens a localized three-slide introduction. Its opt-out marker lives under the owned app-data directory and is removed by full application-data cleanup; smoke tests can suppress the modal.
- `src/PrivacyAudit/Core/FileProvenanceAnalyzer.cs` is an explicit Details-only forensic investigation service. It is never registered as an `IPrivacyScanner`; `src/PrivacyAudit/Storage/AuditDatabase.cs` persists versioned results and evidence for cache reuse and invalidation.
# UI modal layer

`MainWindow` contains the shared `ModalOverlay` host. Intro, cleanup and personal-recommendations information dialogs are `UserControl` views shown in that host, keeping navigation and dismissal inside the main application window.
