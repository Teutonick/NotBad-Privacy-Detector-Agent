# Testing

Documentation ownership is defined in [PROJECT_MAP.md](PROJECT_MAP.md#documentation-boundaries). This file is the canonical source for verification scope and release gates; README links here without reproducing individual test or script mechanics.

Required release command:

```powershell
.\scripts\verify.ps1
```

It redirects temporary state into the workspace, restores dependencies, runs Release tests, publishes a self-contained single-file EXE, executes `--smoke-test`, checks its exit code and confirms that its PID and child processes are gone.

Coverage includes scoring, classification, age and bidirectional media-resolution filters, original image-header dimensions, pagination windows and viewport-anchor offset restoration, localization fallback, exclusions, SQLite, scanner isolation, partial cancellation, independent Media people/document operation state and localized control text, filesystem discovery, WPF construction and packaged-process lifecycle. Tests never delete system data or require administrator rights.

Snapshot and similarity coverage verifies that completed per-object similarity matches survive metadata serialization while unrelated detector metadata remains intact. Snapshot tests also verify that asynchronous restore honors a pre-canceled token. Manual responsiveness checks use a large restored audit and confirm that snapshot enrichment, media candidate preparation and model integrity verification do not execute on the WPF dispatcher; the restore-cancel action must open the new-audit form immediately and preserve the old snapshot until a new audit starts.

Overview state coverage verifies that the primary-audit Stop button is hidden while the form is idle, during restore and after restore, while the separate restore-cancel action remains available. The folder path field and picker button are also checked visually as one horizontal control row.

Triage Router coverage verifies scanner applicability, soft down-ranking of thumbnails without exclusion, ten-percent selection, the absolute cap, registry-driven future-scanner execution and local session round-tripping. Manual wizard checks cover preparation off the dispatcher, pause/resume/cancel persistence, unavailable-scanner isolation, stage snapshots, read-only controls during execution, dynamic report visibility and return navigation from the priority grid.

Privacy Radar coverage verifies audit-context snapshot round-tripping, backward-compatible optional context, evidence-aware ranking from persisted deep-detector metadata, and safe audit-result reset that does not erase personal feedback or exclusions.

Personal-model coverage verifies independent objective/personal axes, the 80/20 combined priority, bounded critical-evidence floors, the combined Privacy Risk average sort key, detection-evidence priority over an unknown Recent item, Unknown versus scanned-clear states, feedback-to-deep-signal event history, refreshed feature snapshots, schema compatibility and validation-metric generation during training.

Application-history coverage verifies deterministic default ordering by availability and menu persistence (`Да/Да`, `Да/Нет`, `Нет/Да`, `Нет/Нет`) before risk and recency tie-breakers.

Findings UI coverage also checks the privacy-first column order and localized Detect Risk label. The post-audit research hint is visible only while the current audit has no completed deep research, can be dismissed, and is hidden after a persisted deep-detector completion.

The reset-filters path is expected to restore `DetectionPriorityRank` descending, not modification-date sorting; completed scans with no positive evidence use the short localized `No` badge.

Cleanup tests use a dedicated temporary fake local-app-data root. They verify that secondary cleanup preserves AI recommendations and ratings, full cleanup removes only the owned root, and foreign roots are rejected.

`scripts\package-release.ps1` invokes the required verification first and then produces a compact versioned ZIP containing the portable EXE, runtime/legal documents, third-party license files and SHA-256 sidecar. Source-only project documentation and demo media are not copied into the ZIP.
