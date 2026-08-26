[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$FilePath,
    [string]$CertificateThumbprint = $env:NOTBAD_SIGNING_CERT_THUMBPRINT,
    [string]$TimestampUrl = $(if ($env:NOTBAD_TIMESTAMP_URL) { $env:NOTBAD_TIMESTAMP_URL } else { 'https://timestamp.digicert.com' }),
    [switch]$MachineStore
)

$ErrorActionPreference = 'Stop'
$resolvedFile = (Resolve-Path -LiteralPath $FilePath).Path
if ([IO.Path]::GetExtension($resolvedFile) -ne '.exe') { throw "Only an EXE release artifact may be signed: $resolvedFile" }
if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    throw 'Set NOTBAD_SIGNING_CERT_THUMBPRINT to the SHA-1 thumbprint of the trusted code-signing certificate in the Windows My certificate store.'
}
$thumbprint = ($CertificateThumbprint -replace '[^0-9A-Fa-f]', '').ToUpperInvariant()
if ($thumbprint.Length -ne 40) { throw 'The code-signing certificate thumbprint must contain exactly 40 hexadecimal characters.' }

$storeLocation = if ($MachineStore) { 'LocalMachine' } else { 'CurrentUser' }
$certificatePath = "Cert:\$storeLocation\My\$thumbprint"
if (-not (Test-Path -LiteralPath $certificatePath)) { throw "Code-signing certificate was not found: $certificatePath" }
$certificate = Get-Item -LiteralPath $certificatePath
if (-not $certificate.HasPrivateKey) { throw 'The selected certificate has no accessible private key.' }
if ($certificate.NotAfter -le (Get-Date).ToUniversalTime()) { throw 'The selected code-signing certificate has expired.' }
$codeSigningOid = '1.3.6.1.5.5.7.3.3'
if (-not ($certificate.EnhancedKeyUsageList.ObjectId.Value -contains $codeSigningOid)) { throw 'The selected certificate is not valid for code signing.' }

$kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$signTool = Get-ChildItem -LiteralPath $kitsRoot -Filter signtool.exe -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1
if (-not $signTool) { throw 'SignTool was not found. Install the Windows SDK Signing Tools feature.' }

$storeArgs = if ($MachineStore) { @('/sm') } else { @() }
& $signTool.FullName sign /sha1 $thumbprint /s My @storeArgs /fd SHA256 /tr $TimestampUrl /td SHA256 /d 'NotBad Privacy Detector Agent' /v $resolvedFile
if ($LASTEXITCODE -ne 0) { throw "SignTool failed with exit code $LASTEXITCODE." }

& $signTool.FullName verify /pa /all /v $resolvedFile
if ($LASTEXITCODE -ne 0) { throw "Authenticode verification failed with exit code $LASTEXITCODE." }

$signature = Get-AuthenticodeSignature -LiteralPath $resolvedFile
if ($signature.Status -ne 'Valid') { throw "Windows reports an invalid Authenticode signature: $($signature.StatusMessage)" }
if ($signature.SignerCertificate.Thumbprint -ne $thumbprint) { throw 'The resulting signature does not use the requested certificate.' }
if ($null -eq $signature.TimeStamperCertificate) { throw 'The executable has no trusted timestamp. Release was blocked.' }

Write-Host "SIGNED: $resolvedFile"
Write-Host "PUBLISHER: $($signature.SignerCertificate.Subject)"
Write-Host "CERTIFICATE: $thumbprint"
Write-Host "TIMESTAMP: $($signature.TimeStamperCertificate.Subject)"
