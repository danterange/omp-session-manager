[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$')]
    [string]$Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-NativeChecked {
    <# Runs a native packager command and stops immediately when it reports an error. #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $($LASTEXITCODE): $FilePath $($Arguments -join ' ')"
    }
}

$RepositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$artifactRoot = Join-Path $RepositoryRoot 'artifacts'
$publishDirectory = Join-Path $artifactRoot "publish\win-x64-$([Guid]::NewGuid().ToString('N'))"
$installerDirectory = Join-Path $artifactRoot 'installer'
$portableArchive = Join-Path $artifactRoot "OmpSessionManager-$Version-win-x64-portable.zip"
$installerScript = Join-Path $RepositoryRoot 'installer\OmpSessionManager.iss'
$innoCompiler = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'

& (Join-Path $PSScriptRoot 'Set-ProjectVersion.ps1') -Version $Version

& (Join-Path $PSScriptRoot 'Invoke-QualityGate.ps1') -PublishDirectory $publishDirectory

if (-not (Test-Path -LiteralPath $innoCompiler)) {
    throw "Inno Setup compiler was not found: $innoCompiler"
}

New-Item -ItemType Directory -Path $installerDirectory -Force | Out-Null
Invoke-NativeChecked -FilePath $innoCompiler -Arguments @(
    "/DMyAppVersion=$Version",
    "/DSourceDir=$publishDirectory",
    "/O$installerDirectory",
    $installerScript
)

$installerPath = Join-Path $installerDirectory "OmpSessionManager-Setup-$Version.exe"
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "Installer output was not found: $installerPath"
}

Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $portableArchive -Force

foreach ($artifactPath in @($installerPath, $portableArchive)) {
    $hash = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $hashPath = "$artifactPath.sha256"
    "$hash *$([System.IO.Path]::GetFileName($artifactPath))" |
        Set-Content -LiteralPath $hashPath -Encoding utf8
}

& (Join-Path $PSScriptRoot 'Test-ReleasePackage.ps1') -InstallerPath $installerPath -PortableArchivePath $portableArchive -ExpectedVersion $Version

Write-Output "[OK] Release package created: $artifactRoot"
