# Building

The mandatory verification smoke-test starts with an isolated app-data environment and suppresses interactive restore UI. The self-contained single-file publish deliberately disables bundle compression: the artifact is larger, but startup avoids managed bundle decompression that can resemble packer behavior to antivirus heuristics. Native runtime extraction is still performed by the .NET single-file host, so the verification gate allows up to 120 seconds for the packaged process to start and exit. Normal desktop launches automatically restore the last Privacy Radar snapshot after the window is shown; this does not change build or publish commands.

Documentation ownership is defined in [PROJECT_MAP.md](PROJECT_MAP.md#documentation-boundaries). Build and packaging details belong here; README keeps only the product overview and a link to this guide.

Requirements: Windows 10/11 x64 and .NET 8 SDK.

The application project is `src\PrivacyAudit\PrivacyAudit.csproj`; `PrivacyAudit.sln` also includes the test project. Build, test, publish, and test-result directories are generated locally and excluded from source control.

```powershell
.\scripts\verify.ps1
```

The only supported release artifact is `dist\NotBadPrivacyDetectorAgent.exe` produced after all checks pass.

## Authenticode release signing

Authenticode is optional until the project obtains a publicly trusted certificate. When `NOTBAD_SIGNING_CERT_THUMBPRINT` is set, `verify.ps1` signs the published EXE before its smoke-test and requires the signing operation to succeed. Without a certificate, `package-release.ps1` produces an explicitly reported unsigned release and relies on the adjacent SHA-256 sidecar for byte identity. A self-signed certificate is intentionally not used: it would not establish trust on other users' computers.

Use a publicly trusted organization or individual code-signing certificate whose private key is protected by the Windows certificate store, hardware token or CA cloud-signing provider. Do not place `.pfx`, passwords, exported keys or signing credentials in this repository. Install the public certificate in `CurrentUser\My`, make its protected private key available to the release workstation, and set only its non-secret thumbprint for the release shell:

```powershell
$env:NOTBAD_SIGNING_CERT_THUMBPRINT = '40_HEXADECIMAL_CHARACTERS_WITHOUT_SECRETS'
$env:NOTBAD_TIMESTAMP_URL = 'https://timestamp.digicert.com' # or the RFC 3161 URL supplied by the CA
.\scripts\package-release.ps1
```

`scripts\sign-release.ps1` locates the x64 Windows SDK `SignTool`, signs with SHA-256, requests an RFC 3161 SHA-256 timestamp, verifies the Windows Authenticode policy, checks the exact signer thumbprint and requires a timestamp certificate. Signing happens before the packaged startup/termination smoke-test; the signed bytes are copied to `dist` and packaged. Install the Windows SDK Signing Tools feature if `signtool.exe` is unavailable.

## GitHub Releases

Run `scripts\package-release.ps1` after changing `<Version>` in `src\PrivacyAudit\PrivacyAudit.csproj` to a new `major.minor.patch` value. If protected signing credentials are configured, the script signs and verifies the EXE; otherwise it clearly reports `NotSigned`. It then verifies the build, creates `artifacts\releases\NotBadPrivacyDetectorAgent-v<version>-win-x64.zip`, writes its `.sha256` sidecar, and produces `publish-github-release-v<version>.txt` with the exact upload steps.

Create a normal, published GitHub Release at [the Releases page](https://github.com/Teutonick/NotBad-Privacy-Detector-Agent/releases/new) using the tag `v<version>` from the current `main` branch. Upload both the ZIP and its `.sha256` file. Do not mark the release as Draft or Pre-release: the application checks the public `releases/latest` endpoint, reads the `v<version>` tag, and opens the release page for the user to download manually. The application never downloads or installs the asset itself.
