[CmdletBinding()]
param(
    [switch]$RequireSignature
)

$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$tempRoot = Join-Path $workspace '.tmp\verify'
$cliRoot = Join-Path $workspace '.dotnet-cli'
$publishRoot = Join-Path $workspace ('.tmp\publish-' + [Guid]::NewGuid().ToString('N'))
$resultsRoot = Join-Path $workspace 'artifacts\test-results'
$distRoot = Join-Path $workspace 'dist'

if ($RequireSignature -and [string]::IsNullOrWhiteSpace($env:NOTBAD_SIGNING_CERT_THUMBPRINT)) {
    throw 'Public release requires Authenticode signing. Set NOTBAD_SIGNING_CERT_THUMBPRINT to a trusted code-signing certificate in the Windows My store.'
}

New-Item -ItemType Directory -Force -Path $tempRoot, $cliRoot, $publishRoot, $resultsRoot, $distRoot | Out-Null
$env:TEMP = $tempRoot
$env:TMP = $tempRoot
$env:DOTNET_CLI_HOME = $cliRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

Push-Location $workspace
try {
    dotnet restore PrivacyAudit.sln -m:1 -nr:false
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

    dotnet test tests\PrivacyAudit.Tests\PrivacyAudit.Tests.csproj -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false --blame-hang-timeout 30s --logger "trx;LogFileName=privacy-audit-tests.trx" --results-directory $resultsRoot
    if ($LASTEXITCODE -ne 0) { throw 'Automated tests failed. Release artifact was not produced.' }

    dotnet publish src\PrivacyAudit\PrivacyAudit.csproj -c Release -r win-x64 --self-contained true --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -p:PublishSingleFile=true -o $publishRoot
    if ($LASTEXITCODE -ne 0) { throw 'Single-file publish failed.' }

    $publishedExe = Join-Path $publishRoot 'NotBadPrivacyDetectorAgent.exe'
    if (-not (Test-Path -LiteralPath $publishedExe)) { throw "Published executable is missing: $publishedExe" }

    if ($env:NOTBAD_SIGNING_CERT_THUMBPRINT) {
        & (Join-Path $PSScriptRoot 'sign-release.ps1') -FilePath $publishedExe
        if ($LASTEXITCODE -ne 0) { throw 'Authenticode signing failed.' }
    }
    elseif ($RequireSignature) {
        throw 'Public release requires Authenticode signing. Set NOTBAD_SIGNING_CERT_THUMBPRINT to a trusted code-signing certificate in the Windows My store.'
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $publishedExe
    if ($RequireSignature -and ($signature.Status -ne 'Valid' -or $null -eq $signature.TimeStamperCertificate)) {
        throw 'Public release requires a valid Authenticode signature with a trusted timestamp.'
    }

    $process = Start-Process -FilePath $publishedExe -ArgumentList '--smoke-test' -PassThru -WindowStyle Hidden
    $launchedPid = $process.Id
    # A fresh single-file publish may spend several seconds extracting its bundled runtime
    # before the WPF dispatcher reaches the smoke-test timer.
    if (-not $process.WaitForExit(120000)) {
        Stop-Process -Id $launchedPid -Force -ErrorAction SilentlyContinue
        throw "Packaged startup smoke-test timed out (PID $launchedPid)."
    }
    if ($process.ExitCode -ne 0) { throw "Packaged startup smoke-test failed with exit code $($process.ExitCode)." }

    Start-Sleep -Milliseconds 400
    if (Get-Process -Id $launchedPid -ErrorAction SilentlyContinue) { throw "NotBadPrivacyDetectorAgent process remained alive after shutdown (PID $launchedPid)." }
    $children = Get-CimInstance Win32_Process -Filter "ParentProcessId = $launchedPid" -ErrorAction SilentlyContinue
    if ($children) { throw "Child process remained after NotBadPrivacyDetectorAgent shutdown: $($children.ProcessId -join ', ')." }

    $target = Join-Path $distRoot 'NotBadPrivacyDetectorAgent.exe'
    $publishedHash = (Get-FileHash -LiteralPath $publishedExe -Algorithm SHA256).Hash
    $targetIsCurrent = (Test-Path -LiteralPath $target) -and ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -eq $publishedHash)
    if (-not $targetIsCurrent) {
        $copied = $false
        for ($attempt = 1; $attempt -le 5; $attempt++) {
            try {
                Copy-Item -LiteralPath $publishedExe -Destination $target -Force
                $copied = $true
                break
            }
            catch [System.IO.IOException] {
                Start-Sleep -Milliseconds 300
            }
        }
        if (-not $copied) {
            $target = Join-Path $distRoot "NotBadPrivacyDetectorAgent-$(Get-Random).exe"
            Copy-Item -LiteralPath $publishedExe -Destination $target -Force
            Write-Host "Canonical EXE is in use; verified build was written to $target"
        }
    }
    $hash = Get-FileHash -LiteralPath $target -Algorithm SHA256
    $file = Get-Item -LiteralPath $target
    Write-Host "VERIFIED: $target"
    Write-Host "SIZE: $($file.Length) bytes"
    Write-Host "SHA256: $($hash.Hash)"
    Write-Host "AUTHENTICODE: $($signature.Status)"
}
finally {
    Pop-Location
}
