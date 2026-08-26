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

Application startup owns a named local mutex (`Local\NotBadPrivacyDetectorAgent`). A second launch exits with a localized message. Snapshot restoration starts automatically only after the main window is shown; asynchronous staged progress is handled by `MainWindow.TryRestoreSnapshotAsync`.

The startup workspace follows the **Privacy Radar** lifecycle. `SnapshotStore` persists both enriched findings and the audit context (preset, roots, start/completion time and duration). `MainWindow.TryRestoreSnapshotAsync` automatically restores it after first render; JSON decoding, personal-score preparation, media existence checks and ranking run on worker threads while navigation remains rendered in read-only mode. Restore accepts cancellation: the cancel action releases the restore guard and opens the new-audit form immediately, while the old snapshot and audit rows remain until a replacement audit actually starts. The scan-settings card is then replaced with the current-radar summary. **Start over** reveals the standard preset/scope form; old audit rows and the resumable snapshot are cleared only when the replacement audit actually starts. `Core/PrivacyRadarRanking.cs` supplies evidence-aware ordering from accumulated deep-detector metadata.

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
│   ├── FindingFilter.cs            discrete size/age and centered bidirectional media-resolution filtering logic
│   ├── MediaImageInfo.cs           lazy header-only image dimension reader for media filtering
│   ├── TriageRouter.cs              cheap applicability/cost routing, ten-percent budget and diverse priority scope
│   ├── DeepAuditRegistry.cs         extensible deep-scanner contract, registry and default detector adapters
│   ├── PriorityAuditSession.cs      local resumable wizard state and atomic sidecar persistence
│   ├── PriorityAuditCoordinator.cs  sequential scanner stages, isolation, checkpoints and aggregate progress
│   ├── FindingPagination.cs        virtualization, six-page dynamic windows, and sorting
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
│   ├── ImageSafetyClassifier.cs   RGB 224×224 local NSFL/NSFW/SFW ONNX inference
│   ├── ImageSafetyScanner.cs      unchanged-file cache, progress and cancellation
│   ├── ImageSafetyRepository.cs   SQLite persistence for all three scores
│   └── PeopleScanner.cs           local media scanner coordinator
│   ├── Scanners/                   independent scanner modules (Filesystem, Windows)
│   └── Storage/                    local SQLite persistence & snapshots
│   └── AppDataCleanupService.cs   scoped cleanup that rejects foreign roots and reparse traversal
├── tests/PrivacyAudit.Tests/       automated core, storage, UI-construction and lifecycle tests
├── third_party/                    checked-in YuNet and Image Safety XS mirrors with LICENSE.txt, SOURCE.md and SHA256SUMS.txt
├── scripts/verify.ps1              mandatory release verification
├── scripts/package-release.ps1     verified versioned ZIP + legal/privacy/license documents
├── Directory.Build.props           isolated build outputs, no lock against running release
├── docs/                           engineering documentation
│   └── PERSONAL_MODEL_USER_DESCRIPTION.md canonical RU/EN explanation; mandatory synchronization contract
└── dist/                           generated verified executable (ignored by Git)
```

`UI → ScanCoordinator → IPrivacyScanner[] → Finding[] → TriageRouter → IDeepAuditScanner registry → priority report → optional manual deep checks/Application History/Similarity → SQLite + snapshots + views`

Scanner errors are isolated. Cancellation returns a partial report. System culture selects Russian only for `ru-*`; every other culture falls back to English. Russian localization strictly employs a friendly and respectful informal tone ("ты") without bureaucratese or formal "Вы".

# UI, Legend and Tooltip Standards

- **Universal ToolTips**: Every button, dropdown, slider, checkbox, search field, and rating control must have a concise, user-friendly `ToolTip` explaining its purpose in non-technical terms.
- **Collapsible Form Legends**: Key workspace forms (Findings and Media) include a collapsible **«Legend & Information»** drawer explaining all active controls, filters, scanners, and ratings in the same wording as their tooltips.
- **Media analysis controls**: the Media workspace presents all found images by default and keeps people detection and document-photo detection as independent local operations. Each process owns its progress, pause/continue and cancel controls; completed results are preserved independently. The detailed runtime contract is documented in [ARCHITECTURE.md](ARCHITECTURE.md#media-analysis-lifecycle).
- **Similarity sessions**: image/document similarity runs per selected finding with cooperative pause/continue/cancel controls. Completed match lists are embedded into finding metadata and restored with the audit snapshot.
- **Application history ordering**: the default report order is availability/menu-state priority (`Да/Да`, `Да/Нет`, `Нет/Да`, `Нет/Нет`), followed by risk and recency; the AI ordering option remains an explicit alternative.
- **Language preference**: `LocalizationService` resolves the saved `ru`/`en` choice from the app-owned local-data folder, exposes a compact current-language button in `SidebarFooterControl`, and restarts through `App.OnExit` only after the user confirms. The old process releases the single-instance mutex before launching the replacement with the original arguments.
- **Details return navigation**: opening an object by double-click records whether it came from the findings grid, findings tiles, or media tiles and stores the scroll offsets. Details exposes a small back-arrow button and accepts global XButton input, native XBUTTON down/up, routed browser-back commands, Alt+Left, browser-back app commands, and the browser-back virtual key, restoring the original tab/list and position across common mouse-driver mappings.
- **Image Safety media controls**: the optional XS package classifies every found image as NSFW, NSFL or SFW and persists all three scores. The sole NSFW Media filter includes only completed results with `NSFW score > 0.85`; a compact background-free eye/eye-off toggle at the far right controls NSFW thumbnail blur. Details exposes the existing confirmed Recycle Bin workflow for files and hides it for directories.
- **Accurate Detection Risk**: Jump List containers are technical exposure artifacts, not dangerous user files, and never receive a High/Critical finding. Risk is assigned to referenced objects from existing audit evidence; an unmatched historical path is promoted only for explicit sensitive-name/extension or network-path evidence.
- **Reorganized Findings Toolbar**: Upper row houses category and risk dropdowns, deep scanning triggers (PII, Secrets, Configs, Identity, Archives), stop button, and legend toggle; lower row holds discrete size/age sliders, screen exposure toggle, personal ML recommendation toggle, vertically-centered search box, and the reset button.
- **Detection-first Findings order**: The default grid order puts confirmed detector evidence ahead of scanned-clear and `Unknown` findings, using evidence volume, category breadth and then the compact Privacy Risk average. Scanner completion state is persisted in finding metadata so `Unknown` remains distinct from a completed scan with no confirmed detections.
- **Findings risk clarity**: the compact `Privacy Risk` column is shown before the separate `Detect Risk` severity column; a dismissible yellow research hint appears after a primary audit until a deep research action completes or the user closes the hint.
- **Fundamental Findings reset**: clearing filters restores the evidence-first `DetectionPriorityRank` order, so confirmed detector content remains above less-researched items after reset.
- **Priority deep-check wizard**: after the primary audit, Overview offers a budgeted local pass over a diverse scope of up to 10%. The dynamic Priority Report appears only after completion and reuses the same findings, Details navigation and file context actions without exposing scanner-launch controls.
- **Strict wizard sequence**: a new audit immediately enters the mandatory priority-offer step; there is no dismiss/skip action. Report navigation and the Priority Report tab remain unavailable until that audit's priority session reaches `Completed`, and asynchronous Triage Router preparation explicitly clears every stale completed-state control.
- **Stable audit identity and recovery**: priority sessions bind to immutable audit start/completion timestamps rather than the mutable finding count. Legacy count-based fingerprints are accepted by their timestamps and migrated in place, so later deep scans cannot detach a completed Priority Report. Restore and priority read-only states share one logical-tree interaction lock; when no corresponding process exists, Overview actions are explicitly returned to an interactive state.
- **Priority wizard CTA**: the two-click priority-scope action uses a distinct orange accent so it is visually separate from ordinary blue navigation and scan actions.
- **Priority branding**: the wizard and its report title/tab use the localized `✨` marker to make the guided AI-assisted flow immediately recognizable.
- **Full-report navigation**: the completed wizard offers a second action directly below the priority report button; it opens the complete Findings workspace without changing the saved priority report.
- **Accuracy disclaimer**: Priority Check/Priority Report, Findings, Media, Application History and Details display the same localized informational warning that detector signals may be wrong and must be verified before action. Findings, Priority Report, Media and Application History place it below their filter/control controls for a consistent report layout.
- **Partial-scope disclosure**: the running wizard and Priority Report explicitly state that Triage Router covers only a subset. After completion, independently dismissible notices in Findings, Media and Application History point to the relevant whole-audit scanners.
- **Priority Report pagination and image tiles**: filtered/sorted priority findings use the same list/tile presentation contract as Findings. Selecting the Images category reveals the localized **Tiles** switch, zoom control, thumbnail cards, feedback actions and Details navigation; tile scale dynamically determines page size, while list mode keeps 600 rows. Filter and presentation changes return to page one.
- **Dashboard category navigation**: Overview category cards select `TabFindings` directly and apply the matching category tag. Navigation never relies on a numeric tab index, so adding or hiding Priority Report cannot redirect cards into an empty report.
- **Shared tile sizing layout**: Findings and Priority Report vertically center the tile-size label, slider track and pixel value as one control group.
- **Collapsible Media deep-analysis panel**: the people, document and metadata scanner cards are expanded on every application start and can be collapsed for the current window session with the subdued `⌜⌟` affordance aligned to the right edge and positioned by a dedicated visual layer, independent from the button template baseline. The collapsed surface retains only the affordance-height reopening target; Media filters remain independent and visible.
- **Priority Report personal attention**: the report table exposes the same local ✨ AI-priority score and shared like/dislike/clear controls as Findings. Feedback writes to the finding's single audit-wide personal-attention record, so it trains and updates the same model regardless of which report supplied the rating; image tiles use that identical contract too.
- **Audit-bound priority report**: starting any valid new primary audit deletes the previous priority-session sidecar and immediately hides its report. A new Triage Router plan is created only from the completed new finding set, so reports cannot cross audit boundaries.
- **Overview scan controls**: the new-audit actions (`Start scan`, `Stop`, restore cancellation and `Back`) live inside the scan-settings card next to the preset and folder controls; the left side of the Privacy Radar card remains explanatory. The primary pair stays on one row, while restore cancellation and `Back` use separate, evenly spaced rows; the directory field stretches to the available width.
- **Manual update check**: `Services/GitHubUpdateChecker.cs` reads GitHub's latest-release metadata only after the footer button is clicked, compares normalized assembly/release versions, and opens the release page only after a second confirmation. It never downloads or installs an update.

# Selection, deletion and cancellation UX

- `FindingsGrid`, `FindingsTileList` and `MediaTileList` use WPF extended selection (Shift ranges and Ctrl point selection).
- The context menu acts on the current list selection. Directory findings are explicitly marked and expose only **«Show in Explorer»**; the delete path rejects directories defensively. For files, a separate confirmation is required before selected files are sent to the Windows Recycle Bin; successful entries are then removed from the in-memory audit lists and snapshot without a post-delete verification pass.
- The Recent scanner records the actual `.lnk` artifact in Windows Recent, checks its recorded target without opening it, and labels a shortcut with a missing target as a stale Recent trace rather than implying that the target file still exists.
- Archive sensitivity is an on-demand inspection result, not a pre-scan category filter. The Findings toolbar keeps **«Inspect archives»** as the entry point; after completion the view automatically selects the localized archive-inspection filter and shows only inspected archives with sensitive contents, while archive severity badges remain available in details.
- Cooperative cancellation uses `CancellationTokenSource.CancelAsync()` from the cancel action, keeping the WPF dispatcher available for rendering while the scanner unwinds. An indeterminate cancellation progress bar and localized “please wait” status remain visible until the active operation has actually finished unwinding.

# Personal attention model

- `src/PrivacyAudit/Core/PersonalAttentionModel.cs` extracts versioned, content-free feature rows, trains the ML.NET SDCA model off the UI thread, records holdout/precision@20 metrics, and batch-scores findings and application history.
- `src/PrivacyAudit/Storage/AuditDatabase.cs` owns `ml_feedback` and bounded `personal_feature_events`; labels keep current features while the event stream preserves how rated objects gain PII, secret, media and other deep-analysis evidence over time.
- `src/PrivacyAudit/Views/MainWindow.xaml(.cs)` supplies 👍/👎/clear controls, personal-score sorting and recommendation filtering, background retraining/cancellation, and model/history reset controls.
- The Details view opens a localized **How AI recommendations work** window. `docs/PERSONAL_MODEL_USER_DESCRIPTION.md` is its canonical copy; changes to features, learning policy, score semantics, privacy/storage, retraining, or audit influence must update both copies in the same change and are release-blocking otherwise.
- The first normal launch opens a localized three-slide introduction. Its opt-out marker lives under the owned app-data directory and is removed by full application-data cleanup; smoke tests can suppress the modal.
- `src/PrivacyAudit/Core/FileProvenanceAnalyzer.cs` is an explicit Details-only forensic investigation service. It is never registered as an `IPrivacyScanner`; `src/PrivacyAudit/Storage/AuditDatabase.cs` persists versioned results and evidence for cache reuse and invalidation.
# UI modal layer

`MainWindow` contains the shared `ModalOverlay` host. Intro, cleanup and personal-recommendations information dialogs are `UserControl` views shown in that host, keeping navigation and dismissal inside the main application window.
