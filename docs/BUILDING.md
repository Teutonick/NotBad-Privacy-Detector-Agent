# Building

The mandatory verification smoke-test starts with an isolated app-data environment and suppresses interactive restore UI. Normal desktop launches automatically restore the last Privacy Radar snapshot after the window is shown; this does not change build or publish commands.

Documentation ownership is defined in [PROJECT_MAP.md](PROJECT_MAP.md#documentation-boundaries). Build and packaging details belong here; README keeps only the product overview and a link to this guide.

Requirements: Windows 10/11 x64 and .NET 8 SDK.

The application project is `src\PrivacyAudit\PrivacyAudit.csproj`; `PrivacyAudit.sln` also includes the test project. Build, test, publish, and test-result directories are generated locally and excluded from source control.

```powershell
.\scripts\verify.ps1
```

The only supported release artifact is `dist\NotBadPrivacyDetectorAgent.exe` produced after all checks pass.
