# Testing

Documentation ownership is defined in [PROJECT_MAP.md](PROJECT_MAP.md#documentation-boundaries). This file is the canonical source for verification scope and release gates; README links here without reproducing individual test or script mechanics.

Required release command:

```powershell
.\scripts\verify.ps1
```

It redirects temporary state into the workspace, restores dependencies, runs Release tests, publishes a self-contained single-file EXE, executes `--smoke-test`, checks its exit code and confirms that its PID and child processes are gone.

Coverage includes scoring, classification, age and bidirectional media-resolution filters, original image-header dimensions, pagination windows and viewport-anchor offset restoration, localization fallback, exclusions, SQLite, scanner isolation, partial cancellation, independent Media people/document operation state and localized control text, filesystem discovery, WPF construction and packaged-process lifecycle. Tests never delete system data or require administrator rights.

Snapshot and similarity coverage verifies that completed per-object similarity matches survive metadata serialization while unrelated detector metadata remains intact. Snapshot tests also verify that asynchronous restore honors a pre-canceled token. Manual responsiveness checks use a large restored audit and confirm that snapshot enrichment, media candidate preparation and model integrity verification do not execute on the WPF dispatcher; the restore-cancel action must open the new-audit form immediately and preserve the old snapshot until a new audit starts.

Privacy Radar coverage verifies audit-context snapshot round-tripping, backward-compatible optional context, evidence-aware ranking from persisted deep-detector metadata, and safe audit-result reset that does not erase personal feedback or exclusions.

Personal-model coverage verifies independent objective/personal axes, the 80/20 combined priority, bounded critical-evidence floors, the combined Privacy Risk average sort key, detection-evidence priority over an unknown Recent item, Unknown versus scanned-clear states, feedback-to-deep-signal event history, refreshed feature snapshots, schema compatibility and validation-metric generation during training.

Cleanup tests use a dedicated temporary fake local-app-data root. They verify that secondary cleanup preserves AI recommendations and ratings, full cleanup removes only the owned root, and foreign roots are rejected.

`scripts\package-release.ps1` invokes the required verification first and then produces a compact versioned ZIP containing the portable EXE, runtime/legal documents, third-party license files and SHA-256 sidecar. Source-only project documentation and demo media are not copied into the ZIP.
