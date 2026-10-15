[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ReleaseDirectory,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$')]
    [string]$ExpectedVersion
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedReleaseDirectory = (Resolve-Path -LiteralPath $ReleaseDirectory -ErrorAction Stop).Path
if (-not (Test-Path -LiteralPath (Join-Path $resolvedReleaseDirectory 'OmpSessionManager.exe'))) {
    throw "Release executable was not found in: $resolvedReleaseDirectory"
}

$executablePath = Join-Path $resolvedReleaseDirectory 'OmpSessionManager.exe'
$versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($executablePath)
$fileVersion = $versionInfo.FileVersion
$productVersion = $versionInfo.ProductVersion
$expectedCoreVersion = ($ExpectedVersion -split '-', 2)[0]
if ([string]::IsNullOrWhiteSpace($fileVersion) -or -not $fileVersion.StartsWith($expectedCoreVersion, [System.StringComparison]::Ordinal)) {
    throw "Release executable FileVersion does not match expected core version $expectedCoreVersion."
}

if ($ExpectedVersion.Contains('-') -and ([string]::IsNullOrWhiteSpace($productVersion) -or -not $productVersion.StartsWith($ExpectedVersion, [System.StringComparison]::Ordinal))) {
    throw "Release executable ProductVersion does not preserve expected prerelease version $ExpectedVersion."
}

$pdbFiles = @(Get-ChildItem -LiteralPath $resolvedReleaseDirectory -Filter '*.pdb' -File -Recurse -ErrorAction Stop)
if ($pdbFiles.Count -gt 0) {
    throw "Release output contains PDB files and must not be published."
}

$scanPatterns = @(
    '(?i)[A-Z]:\\Users\\',
    '-----BEGIN(?: [A-Z]+)* PRIVATE KEY-----',
    '\b(?:ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9]{20,}\b|\bgithub_pat_[A-Za-z0-9_]{20,}\b',
    '\bsk-[A-Za-z0-9_-]{20,}\b'
)

foreach ($pattern in $scanPatterns) {
    $matches = & rg --text --files-with-matches --glob '*' -- $pattern $resolvedReleaseDirectory 2>$null
    if ($LASTEXITCODE -eq 0 -and @($matches).Count -gt 0) {
        throw "Release artifact scan found a prohibited data pattern."
    }

    if ($LASTEXITCODE -gt 1) {
        throw "Artifact scanner failed with exit code $LASTEXITCODE."
    }
}

Write-Output "[OK] Release artifact scan passed: $resolvedReleaseDirectory (FileVersion $fileVersion; ProductVersion $productVersion)"
