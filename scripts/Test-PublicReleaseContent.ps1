[CmdletBinding()]
param(
    [switch]$IncludeGitHistory,
    [switch]$Staged
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
Set-Location -LiteralPath $RepositoryRoot

function Invoke-GitChecked {
    <# Executes Git without printing data that may contain a secret or personal value. #>
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $output = & git @Arguments 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "Git command failed: git $($Arguments -join ' ')"
    }

    return @($output)
}

$textExtensions = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
@('.cs', '.csproj', '.props', '.ps1', '.md', '.yml', '.yaml', '.json', '.jsonl', '.iss', '.txt', '.gitattributes', '.gitignore') |
    ForEach-Object { [void]$textExtensions.Add($_) }

$rules = @(
    [PSCustomObject]@{ Name = 'private key'; Pattern = '-----BEGIN(?: [A-Z]+)* PRIVATE KEY-----' },
    [PSCustomObject]@{ Name = 'GitHub token'; Pattern = '\b(?:ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9]{20,}\b|\bgithub_pat_[A-Za-z0-9_]{20,}\b' },
    [PSCustomObject]@{ Name = 'OpenAI key'; Pattern = '\bsk-[A-Za-z0-9_-]{20,}\b' },
    [PSCustomObject]@{ Name = 'AWS access key'; Pattern = '\bAKIA[0-9A-Z]{16}\b' },
    [PSCustomObject]@{ Name = 'Slack token'; Pattern = '\bxox[baprs]-[A-Za-z0-9-]{20,}\b' },
    [PSCustomObject]@{ Name = 'absolute user profile path'; Pattern = '[A-Z]:\\Users\\[^\\\r\n]+' },
    [PSCustomObject]@{ Name = 'email address'; Pattern = '\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b' }
)

$findings = [System.Collections.Generic.List[string]]::new()
$candidateFiles = if ($Staged) {
    Invoke-GitChecked -Arguments @('ls-files', '--cached')
}
else {
    Invoke-GitChecked -Arguments @('ls-files', '--cached', '--others', '--exclude-standard')
}

foreach ($relativePath in $candidateFiles) {
    $extension = [System.IO.Path]::GetExtension($relativePath)
    if (-not $textExtensions.Contains($extension) -and $relativePath -notin @('.gitignore', '.gitattributes')) {
        continue
    }

    $fullPath = Join-Path $RepositoryRoot $relativePath
    if (-not (Test-Path -LiteralPath $fullPath)) {
        # 已删除但尚未暂存的受跟踪文件不能出现在发布候选内容中。
        continue
    }

    $content = Get-Content -LiteralPath $fullPath -Raw -ErrorAction Stop
    foreach ($rule in $rules) {
        if ([regex]::IsMatch($content, $rule.Pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
            [void]$findings.Add("$relativePath [$($rule.Name)]")
        }
    }
}

if ($IncludeGitHistory) {
    $historyEmails = Invoke-GitChecked -Arguments @('log', '--all', '--format=%ae%n%ce')
    $nonAnonymousEmails = @($historyEmails | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_) -and $_ -notmatch '(?i)@users\.noreply\.github\.com$'
    })
    if ($nonAnonymousEmails.Count -gt 0) {
        [void]$findings.Add('reachable Git history [non-anonymous commit email]')
    }

    $historyNames = Invoke-GitChecked -Arguments @('log', '--all', '--format=%an%n%cn')
    $nonGenericNames = @($historyNames | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_) -and $_ -ne 'OmpSessionManager Contributors'
    })
    if ($nonGenericNames.Count -gt 0) {
        [void]$findings.Add('reachable Git history [non-generic commit identity]')
    }

    $commitMessages = Invoke-GitChecked -Arguments @('log', '--all', '--format=%B')
    $commitMessageText = $commitMessages -join "`n"
    foreach ($rule in $rules) {
        if ([regex]::IsMatch($commitMessageText, $rule.Pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
            [void]$findings.Add("reachable Git history [commit message: $($rule.Name)]")
        }
    }

    $revisions = Invoke-GitChecked -Arguments @('rev-list', '--all')
    foreach ($revision in $revisions) {
        $historyFiles = Invoke-GitChecked -Arguments @('ls-tree', '-r', '--name-only', $revision)
        foreach ($historyPath in $historyFiles) {
            $extension = [System.IO.Path]::GetExtension($historyPath)
            if (-not $textExtensions.Contains($extension) -and $historyPath -notin @('.gitignore', '.gitattributes')) {
                continue
            }

            $historicalContent = Invoke-GitChecked -Arguments @('show', '--no-textconv', ($revision + ':' + $historyPath))
            $historicalText = $historicalContent -join "`n"
            foreach ($rule in $rules) {
                if ([regex]::IsMatch($historicalText, $rule.Pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
                    [void]$findings.Add("reachable Git history [file content: $($rule.Name)]")
                }
            }
        }
    }

    $taggerEmails = Invoke-GitChecked -Arguments @('for-each-ref', '--format=%(taggeremail)', 'refs/tags')
    $nonAnonymousTaggerEmails = @($taggerEmails | ForEach-Object { $_.Trim('<', '>') } | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_) -and $_ -notmatch '(?i)@users\.noreply\.github\.com$'
    })
    if ($nonAnonymousTaggerEmails.Count -gt 0) {
        [void]$findings.Add('reachable Git history [non-anonymous tagger email]')
    }

    $taggerNames = Invoke-GitChecked -Arguments @('for-each-ref', '--format=%(taggername)', 'refs/tags')
    $nonGenericTaggerNames = @($taggerNames | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_) -and $_ -ne 'OmpSessionManager Contributors'
    })
    if ($nonGenericTaggerNames.Count -gt 0) {
        [void]$findings.Add('reachable Git history [non-generic tagger identity]')
    }
}

if ($findings.Count -gt 0) {
    throw "Public-release scan found $($findings.Count) issue(s): $($findings -join '; ')"
}

Write-Output "[OK] Public-release content scan passed for $($candidateFiles.Count) candidate file(s)."
