# Building

Requirements: Windows 10/11 x64 and .NET 8 SDK.

The application project is `src\PrivacyAudit\PrivacyAudit.csproj`; `PrivacyAudit.sln` also includes the test project. Build, test, publish, and test-result directories are generated locally and excluded from source control.

```powershell
.\scripts\verify.ps1
```

The only supported release artifact is `dist\NotBadPrivacyDetectorAgent.exe` produced after all checks pass.
