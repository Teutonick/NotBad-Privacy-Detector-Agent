# Building

The mandatory verification smoke-test starts with an isolated app-data environment and suppresses interactive restore UI. Normal desktop launches automatically restore the last Privacy Radar snapshot after the window is shown; this does not change build or publish commands.

Documentation ownership is defined in [PROJECT_MAP.md](PROJECT_MAP.md#documentation-boundaries). Build and packaging details belong here; README keeps only the product overview and a link to this guide.

Requirements: Windows 10/11 x64 and .NET 8 SDK.

The application project is `src\PrivacyAudit\PrivacyAudit.csproj`; `PrivacyAudit.sln` also includes the test project. Build, test, publish, and test-result directories are generated locally and excluded from source control.

```powershell
.\scripts\verify.ps1
```

The only supported release artifact is `dist\NotBadPrivacyDetectorAgent.exe` produced after all checks pass.

## GitHub Releases

Run `scripts\package-release.ps1` after changing `<Version>` in `src\PrivacyAudit\PrivacyAudit.csproj` to a new `major.minor.patch` value. The script verifies the build, creates `artifacts\releases\NotBadPrivacyDetectorAgent-v<version>-win-x64.zip`, writes its `.sha256` sidecar, and produces `publish-github-release-v<version>.txt` with the exact upload steps.

Create a normal, published GitHub Release at [the Releases page](https://github.com/Teutonick/NotBad-Privacy-Detector-Agent/releases/new) using the tag `v<version>` from the current `main` branch. Upload both the ZIP and its `.sha256` file. Do not mark the release as Draft or Pre-release: the application checks the public `releases/latest` endpoint, reads the `v<version>` tag, and opens the release page for the user to download manually. The application never downloads or installs the asset itself.
