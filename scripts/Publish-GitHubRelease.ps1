[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$')]
    [string]$Version,

    [switch]$Resume
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-NativeChecked {
    <# Runs a native command and prevents the publication flow from continuing after a failed command. #>
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
$tagName = "v$Version"
$publicationName = 'OmpSessionManager Contributors'
# 构造固定的 GitHub noreply 身份，避免在发布候选文本中保留任何完整邮箱地址。
$publicationEmail = 'noreply' + '@' + 'users.noreply.github.com'

$branchName = (& git branch --show-current).Trim()
if ($LASTEXITCODE -ne 0 -or $branchName -ne 'main') {
    throw 'Release publishing must run from the main branch.'
}

& gh auth status 2>$null
if ($LASTEXITCODE -ne 0) {
    throw 'GitHub CLI authentication is required before publishing a release.'
}

$localTagReference = @(& git rev-parse -q --verify "refs/tags/$tagName" 2>$null)
$localTagExitCode = $LASTEXITCODE
if ($localTagExitCode -ne 0 -and $localTagExitCode -ne 1) {
    throw 'Could not inspect the local release tag state.'
}

$remoteTagReference = @(& git ls-remote --tags origin "refs/tags/$tagName" "refs/tags/$tagName^{}" 2>$null)
$remoteTagExitCode = $LASTEXITCODE
if ($remoteTagExitCode -ne 0) {
    throw 'Could not inspect the remote release tag state.'
}

$localTagExists = $localTagReference.Count -gt 0
$remoteTagExists = $remoteTagReference.Count -gt 0
if ($Resume) {
    if (-not $localTagExists -or -not $remoteTagExists) {
        throw 'Resume requires the release tag to exist both locally and on origin.'
    }

    $tagCommit = (& git rev-list -n 1 $tagName).Trim()
    $tagCommitExitCode = $LASTEXITCODE
    $headCommit = (& git rev-parse HEAD).Trim()
    $headCommitExitCode = $LASTEXITCODE
    if ($tagCommitExitCode -ne 0 -or $headCommitExitCode -ne 0 -or $tagCommit -ne $headCommit) {
        throw 'Resume requires main HEAD to match the existing release tag.'
    }

    $remotePeeledReference = @($remoteTagReference | Where-Object {
        $referenceParts = $_ -split '\s+'
        $referenceParts.Count -ge 2 -and $referenceParts[1] -eq "refs/tags/$tagName^{}"
    })
    $remoteTagCommit = if ($remotePeeledReference.Count -eq 1) {
        ($remotePeeledReference[0] -split '\s+')[0]
    }
    elseif ($remoteTagReference.Count -eq 1) {
        # A lightweight tag has no peeled ^{} reference; its only object ID is its commit target.
        ($remoteTagReference[0] -split '\s+')[0]
    }
    else {
        throw 'Resume could not resolve a unique remote release tag target.'
    }

    $remoteMainReference = @(& git ls-remote origin 'refs/heads/main' 2>$null)
    $remoteMainExitCode = $LASTEXITCODE
    if ($remoteMainExitCode -ne 0 -or $remoteMainReference.Count -ne 1) {
        throw 'Resume could not resolve origin main.'
    }

    $remoteMainCommit = ($remoteMainReference[0] -split '\s+')[0]
    if ($remoteTagCommit -ne $headCommit -or $remoteMainCommit -ne $headCommit) {
        throw 'Resume requires origin main and the remote release tag to target local main HEAD.'
    }

    [xml]$versionDocument = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'Directory.Build.props') -Raw
    $currentVersion = $versionDocument.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
    if ($currentVersion -ne $Version) {
        throw "Resume requires Directory.Build.props Version $Version, but found $currentVersion."
    }

    $worktreeChanges = @(& git status --porcelain)
    if ($LASTEXITCODE -ne 0 -or $worktreeChanges.Count -gt 0) {
        throw 'Resume requires a clean worktree so rebuilt assets match the existing tag.'
    }
}
elseif ($localTagExists -or $remoteTagExists) {
    throw "Release tag $tagName already exists. Use -Resume only after verifying main and the tag match."
}

& (Join-Path $PSScriptRoot 'New-ReleasePackage.ps1') -Version $Version

& (Join-Path $PSScriptRoot 'Test-PublicReleaseContent.ps1') -IncludeGitHistory

if (-not $Resume) {
    Invoke-NativeChecked -FilePath 'git' -Arguments @('diff', '--check')
    Invoke-NativeChecked -FilePath 'git' -Arguments @('add', '--all')
    Invoke-NativeChecked -FilePath 'git' -Arguments @('diff', '--cached', '--check')
    & (Join-Path $PSScriptRoot 'Test-PublicReleaseContent.ps1') -IncludeGitHistory -Staged

    $stagedChanges = @(& git diff --cached --name-only)
    if ($LASTEXITCODE -ne 0 -or $stagedChanges.Count -eq 0) {
        throw 'No staged release changes were found.'
    }

    Invoke-NativeChecked -FilePath 'git' -Arguments @(
        '-c', "user.name=$publicationName",
        '-c', "user.email=$publicationEmail",
        '-c', 'commit.gpgSign=false',
        'commit', '-m', "发布: $tagName"
    )
    Invoke-NativeChecked -FilePath 'git' -Arguments @(
        '-c', "user.name=$publicationName",
        '-c', "user.email=$publicationEmail",
        '-c', 'tag.gpgSign=false',
        'tag', '-a', $tagName, '-m', "发布 $tagName"
    )
    & (Join-Path $PSScriptRoot 'Test-PublicReleaseContent.ps1') -IncludeGitHistory -Staged
    Invoke-NativeChecked -FilePath 'git' -Arguments @('push', 'origin', 'main')
    Invoke-NativeChecked -FilePath 'git' -Arguments @('push', 'origin', $tagName)
}

$artifactRoot = Join-Path $RepositoryRoot 'artifacts'
$releaseNotes = Join-Path $artifactRoot 'release-notes.md'
@(
    "# $tagName",
    '',
    '- 首个公开安装包发布。',
    '- 包含 self-contained Windows x64 安装程序和便携版 ZIP。',
    '- 请先核对同名 `.sha256` 文件，再安装或运行。',
    '- 安装程序未进行代码签名；Windows 可能显示来源提示。'
) | Set-Content -LiteralPath $releaseNotes -Encoding utf8

$assets = @(
    (Join-Path $artifactRoot "installer\OmpSessionManager-Setup-$Version.exe"),
    (Join-Path $artifactRoot "installer\OmpSessionManager-Setup-$Version.exe.sha256"),
    (Join-Path $artifactRoot "OmpSessionManager-$Version-win-x64-portable.zip"),
    (Join-Path $artifactRoot "OmpSessionManager-$Version-win-x64-portable.zip.sha256")
)
$releaseView = @(& gh release view $tagName --json id 2>$null)
$releaseViewExitCode = $LASTEXITCODE
if ($releaseViewExitCode -eq 0) {
    $releaseArguments = @('release', 'upload', $tagName, '--clobber') + $assets
    Invoke-NativeChecked -FilePath 'gh' -Arguments $releaseArguments
}
elseif ($releaseViewExitCode -eq 1) {
    $releaseArguments = @('release', 'create', $tagName, '--title', "OMP Session Manager $tagName", '--notes-file', $releaseNotes) + $assets
    Invoke-NativeChecked -FilePath 'gh' -Arguments $releaseArguments
}
else {
    throw "Could not inspect the GitHub Release state for $tagName."
}

$publishedAssetNames = @(& gh release view $tagName --json assets --jq '.assets[].name')
if ($LASTEXITCODE -ne 0) {
    throw "Could not verify GitHub Release assets for $tagName."
}

$expectedAssetNames = @($assets | ForEach-Object { [System.IO.Path]::GetFileName($_) })
$missingAssetNames = @($expectedAssetNames | Where-Object { $_ -notin $publishedAssetNames })
if ($missingAssetNames.Count -gt 0) {
    throw "GitHub Release is missing expected assets: $($missingAssetNames -join ', ')"
}

$verificationDirectory = Join-Path $artifactRoot "release-verify-$Version-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $verificationDirectory -Force | Out-Null
try {
    Invoke-NativeChecked -FilePath 'gh' -Arguments @('release', 'download', $tagName, '--dir', $verificationDirectory)
    foreach ($assetPath in @($assets[0], $assets[2])) {
        $assetFileName = [System.IO.Path]::GetFileName($assetPath)
        $downloadedAssetPath = Join-Path $verificationDirectory $assetFileName
        $downloadedHashPath = "$downloadedAssetPath.sha256"
        if (-not (Test-Path -LiteralPath $downloadedAssetPath) -or -not (Test-Path -LiteralPath $downloadedHashPath)) {
            throw "Downloaded Release verification assets are incomplete for $assetFileName."
        }

        $expectedHash = (Get-Content -LiteralPath $downloadedHashPath -Raw).Trim().Split([char[]]" `t")[0]
        $actualHash = (Get-FileHash -LiteralPath $downloadedAssetPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if (-not [string]::Equals($expectedHash, $actualHash, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Downloaded Release SHA-256 verification failed for $assetFileName."
        }
    }
}
finally {
    if (Test-Path -LiteralPath $verificationDirectory) {
        $resolvedArtifactRoot = (Resolve-Path -LiteralPath $artifactRoot).Path
        $resolvedVerificationDirectory = (Resolve-Path -LiteralPath $verificationDirectory).Path
        if ($resolvedVerificationDirectory.StartsWith($resolvedArtifactRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolvedVerificationDirectory -Recurse -Force
        }
    }
}

Write-Output "[OK] GitHub Release published: $tagName"
