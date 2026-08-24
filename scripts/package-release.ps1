[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectPath = Join-Path $workspace 'src\PrivacyAudit\PrivacyAudit.csproj'
[xml]$project = Get-Content -LiteralPath $projectPath
$version = [string]$project.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) { throw 'PrivacyAudit.csproj does not define Version.' }
if ($version -notmatch '^[0-9A-Za-z][0-9A-Za-z._-]*$') { throw "Unsafe package version: $version" }

& (Join-Path $PSScriptRoot 'verify.ps1')
if ($LASTEXITCODE -ne 0) { throw 'verify.ps1 failed; release ZIP was not created.' }

$releaseRoot = Join-Path $workspace 'artifacts\releases'
$stageRoot = Join-Path $workspace ('.tmp\release-stage-' + [Guid]::NewGuid().ToString('N'))
$packageName = "NotBadPrivacyDetectorAgent-v$version-win-x64"
$packageRoot = Join-Path $stageRoot $packageName
$licenseRoot = Join-Path $packageRoot 'third-party-licenses'
$zipPath = Join-Path $releaseRoot ($packageName + '.zip')

New-Item -ItemType Directory -Force -Path $releaseRoot, $packageRoot, $licenseRoot | Out-Null
try {
    $required = @(
        'LICENSE',
        'README.md',
        'README.ru.md',
        'PRIVACY.md',
        'SECURITY.md',
        'DISCLAIMER.md',
        'THIRD_PARTY_NOTICES.md',
        'docs\PRIVACY.md',
        'docs\DISCLAIMER.md',
        'docs\THIRD_PARTY_NOTICES.md',
        'docs\PROJECT_MAP.md',
        'docs\ARCHITECTURE.md',
        'docs\TESTING.md',
        'docs\BUILDING.md',
        'docs\PUBLIC_RELEASE_AUDIT.md',
        'docs\assets\notbad-privacy-demo.gif'
    )
    foreach ($relative in $required) {
        $source = Join-Path $workspace $relative
        if (-not (Test-Path -LiteralPath $source)) { throw "Required release document is missing: $relative" }
        $destination = Join-Path $packageRoot $relative
        New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination
    }
    $verifiedExe = Join-Path $workspace 'dist\NotBadPrivacyDetectorAgent.exe'
    if (-not (Test-Path -LiteralPath $verifiedExe)) { throw 'Verified portable EXE is missing after verify.' }
    Copy-Item -LiteralPath $verifiedExe -Destination (Join-Path $packageRoot 'NotBadPrivacyDetectorAgent.exe')

    $assetsPath = Join-Path $workspace '.tmp\build\PrivacyAudit\obj\project.assets.json'
    if (-not (Test-Path -LiteralPath $assetsPath)) { throw 'NuGet assets file is missing after verify.' }
    $assets = Get-Content -Raw -LiteralPath $assetsPath | ConvertFrom-Json
    foreach ($libraryProperty in $assets.libraries.PSObject.Properties) {
        if ($libraryProperty.Value.type -ne 'package') { continue }
        $parts = $libraryProperty.Name -split '/', 2
        $packageId = $parts[0]
        $packageVersion = $parts[1]
        $packageDirectory = Join-Path (Join-Path $env:USERPROFILE '.nuget\packages') (Join-Path $packageId.ToLowerInvariant() $packageVersion)
        $targetDirectory = Join-Path $licenseRoot ($packageId + '-' + $packageVersion)
        New-Item -ItemType Directory -Force -Path $targetDirectory | Out-Null
        $licenseFiles = Get-ChildItem -LiteralPath $packageDirectory -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '^(LICENSE|NOTICE|ThirdPartyNotices)(\..*)?$' }
        foreach ($licenseFile in $licenseFiles) {
            Copy-Item -LiteralPath $licenseFile.FullName -Destination (Join-Path $targetDirectory $licenseFile.Name)
        }
        $nuspec = Get-ChildItem -LiteralPath $packageDirectory -Filter '*.nuspec' -File | Select-Object -First 1
        if ($nuspec) { Copy-Item -LiteralPath $nuspec.FullName -Destination (Join-Path $targetDirectory $nuspec.Name) }
    }

    foreach ($runtimeNotice in @('LICENSE.txt', 'ThirdPartyNotices.txt')) {
        $source = Join-Path (Split-Path (Get-Command dotnet).Source -Parent) $runtimeNotice
        if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination (Join-Path $licenseRoot ('.NET-' + $runtimeNotice)) }
    }

    $repository = 'https://github.com/Teutonick/NotBad-Privacy-Detector-Agent'
    @(
        "NotBad Privacy Detector Agent v$version",
        '',
        'This is a portable application. It is not installed into Windows.',
        'Delete NotBadPrivacyDetectorAgent.exe manually to remove the program binary.',
        '',
        "Source code and updates: $repository"
    ) | Set-Content -LiteralPath (Join-Path $packageRoot 'PORTABLE-README.txt') -Encoding UTF8

    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    Compress-Archive -LiteralPath $packageRoot -DestinationPath $zipPath -CompressionLevel Optimal
    $hash = Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
    Set-Content -LiteralPath ($zipPath + '.sha256') -Value "$($hash.Hash)  $([IO.Path]::GetFileName($zipPath))" -Encoding ASCII
    Write-Host "RELEASE: $zipPath"
    Write-Host "SHA256: $($hash.Hash)"
}
finally {
    if (Test-Path -LiteralPath $stageRoot) {
        $resolvedStage = (Resolve-Path -LiteralPath $stageRoot).Path
        $expectedPrefix = (Join-Path $workspace '.tmp\release-stage-')
        if (-not $resolvedStage.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove unexpected staging directory: $resolvedStage"
        }
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
}
