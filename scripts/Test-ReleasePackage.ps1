[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$InstallerPath,

    [Parameter(Mandatory = $true)]
    [string]$PortableArchivePath,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$')]
    [string]$ExpectedVersion
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedInstallerPath = (Resolve-Path -LiteralPath $InstallerPath -ErrorAction Stop).Path
$resolvedPortableArchivePath = (Resolve-Path -LiteralPath $PortableArchivePath -ErrorAction Stop).Path
if ((Get-Item -LiteralPath $resolvedInstallerPath).Length -eq 0 -or (Get-Item -LiteralPath $resolvedPortableArchivePath).Length -eq 0) {
    throw 'Release package assets must not be empty.'
}

$installerPatterns = @(
    '(?i)[A-Z]:\\Users\\',
    '-----BEGIN(?: [A-Z]+)* PRIVATE KEY-----',
    '\b(?:ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9]{20,}\b|\bgithub_pat_[A-Za-z0-9_]{20,}\b',
    '\bsk-[A-Za-z0-9_-]{20,}\b'
)
foreach ($pattern in $installerPatterns) {
    & rg --text --quiet -- $pattern $resolvedInstallerPath 2>$null
    $scanExitCode = $LASTEXITCODE
    if ($scanExitCode -eq 0) {
        throw 'Installer binary contains a prohibited data pattern.'
    }

    if ($scanExitCode -gt 1) {
        throw "Installer binary scan failed with exit code $scanExitCode."
    }
}

$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "OmpSessionManagerPackageTest-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $temporaryDirectory -Force | Out-Null
try {
    Expand-Archive -LiteralPath $resolvedPortableArchivePath -DestinationPath $temporaryDirectory -Force
    & (Join-Path $PSScriptRoot 'Test-ReleaseArtifact.ps1') -ReleaseDirectory $temporaryDirectory -ExpectedVersion $ExpectedVersion
}
finally {
    if (Test-Path -LiteralPath $temporaryDirectory) {
        $resolvedTemporaryParent = (Resolve-Path -LiteralPath ([System.IO.Path]::GetTempPath())).Path
        $resolvedTemporaryDirectory = (Resolve-Path -LiteralPath $temporaryDirectory).Path
        if ($resolvedTemporaryDirectory.StartsWith($resolvedTemporaryParent, [System.StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolvedTemporaryDirectory -Recurse -Force
        }
    }
}

Write-Output '[OK] Installer binary and portable archive package checks passed.'
