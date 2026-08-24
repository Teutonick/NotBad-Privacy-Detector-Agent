# Testing

Required release command:

```powershell
.\scripts\verify.ps1
```

It redirects temporary state into the workspace, restores dependencies, runs Release tests, publishes a self-contained single-file EXE, executes `--smoke-test`, checks its exit code and confirms that its PID and child processes are gone.

Coverage includes scoring, classification, age, localization fallback, exclusions, SQLite, scanner isolation, partial cancellation, filesystem discovery, WPF construction and packaged-process lifecycle. Tests never delete system data or require administrator rights.

Cleanup tests use a dedicated temporary fake local-app-data root. They verify that secondary cleanup preserves AI recommendations and ratings, full cleanup removes only the owned root, and foreign roots are rejected.

`scripts\package-release.ps1` invokes the required verification first and then produces a compact versioned ZIP containing the portable EXE, runtime/legal documents, third-party license files and SHA-256 sidecar. Source-only project documentation and demo media are not copied into the ZIP.
