[CmdletBinding()]
param(
    [switch]$SkipUi,
    [switch]$SkipPublish,
    [string]$PublishDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-NativeChecked {
    <# Runs a native command and converts its exit code into a terminating PowerShell error. #>
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
Set-Location -LiteralPath $RepositoryRoot

& (Join-Path $PSScriptRoot 'Test-PublicReleaseContent.ps1')

Invoke-NativeChecked -FilePath 'dotnet' -Arguments @('restore', '.\OmpSessionManager.Tests\OmpSessionManager.Tests.csproj')
Invoke-NativeChecked -FilePath 'dotnet' -Arguments @('restore', '.\OmpSessionManager.csproj', '--runtime', 'win-x64')
Invoke-NativeChecked -FilePath 'dotnet' -Arguments @('format', '.\OmpSessionManager.csproj', '--no-restore', '--verify-no-changes')
Invoke-NativeChecked -FilePath 'dotnet' -Arguments @('format', '.\OmpSessionManager.Tests\OmpSessionManager.Tests.csproj', '--no-restore', '--verify-no-changes')
Invoke-NativeChecked -FilePath 'dotnet' -Arguments @('build', '.\OmpSessionManager.csproj', '--configuration', 'Release', '--no-restore', '--warnaserror')
Invoke-NativeChecked -FilePath 'dotnet' -Arguments @('test', '.\OmpSessionManager.Tests\OmpSessionManager.Tests.csproj', '--configuration', 'Release', '--no-restore', '--filter', 'TestCategory=Unit')
Invoke-NativeChecked -FilePath 'dotnet' -Arguments @('test', '.\OmpSessionManager.Tests\OmpSessionManager.Tests.csproj', '--configuration', 'Release', '--no-restore', '--filter', 'TestCategory=Integration')

if (-not $SkipUi) {
    Invoke-NativeChecked -FilePath 'dotnet' -Arguments @('test', '.\OmpSessionManager.Tests\OmpSessionManager.Tests.csproj', '--configuration', 'Release', '--no-restore', '--filter', 'TestCategory=UI')
}

if (-not $SkipPublish) {
    $artifactRoot = Join-Path $RepositoryRoot 'artifacts'
    $resolvedArtifactRoot = [System.IO.Path]::GetFullPath($artifactRoot)
    if ([string]::IsNullOrWhiteSpace($PublishDirectory)) {
        $PublishDirectory = Join-Path $artifactRoot 'publish\win-x64'
    }

    $publishDirectory = [System.IO.Path]::GetFullPath($PublishDirectory)
    if (-not $publishDirectory.StartsWith($resolvedArtifactRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to publish outside the artifacts directory: $publishDirectory"
    }

    if (Test-Path -LiteralPath $publishDirectory) {
        $resolvedPublishDirectory = (Resolve-Path -LiteralPath $publishDirectory).Path
        if (-not $resolvedPublishDirectory.StartsWith($resolvedArtifactRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clear an unexpected publish directory: $resolvedPublishDirectory"
        }

        Remove-Item -LiteralPath $resolvedPublishDirectory -Recurse -Force
    }

    New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
    Invoke-NativeChecked -FilePath 'dotnet' -Arguments @(
        'publish', '.\OmpSessionManager.csproj', '--configuration', 'Release', '--no-restore',
        '--runtime', 'win-x64', '--self-contained', 'true', '--output', $publishDirectory,
        '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:DebugType=None', '-p:DebugSymbols=false'
    )
    & (Join-Path $PSScriptRoot 'Test-ReleaseArtifact.ps1') -ReleaseDirectory $publishDirectory -ExpectedVersion (([xml](Get-Content -LiteralPath (Join-Path $RepositoryRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version)
}

Write-Output '[OK] Quality gate completed.'
