[CmdletBinding()]
param(
    [string]$ExecutablePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
    $ExecutablePath = Join-Path $RepositoryRoot 'bin\Release\net9.0-windows\OmpSessionManager.exe'
}

$resolvedExecutablePath = (Resolve-Path -LiteralPath $ExecutablePath -ErrorAction Stop).Path
$demoAgentDirectory = (Resolve-Path -LiteralPath (Join-Path $RepositoryRoot 'samples\demo-agent')).Path
$temporaryParent = [System.IO.Path]::GetTempPath()
$temporaryDirectory = Join-Path $temporaryParent "OmpSessionManagerDemo-$([Guid]::NewGuid().ToString('N'))"
$temporaryIndexPath = Join-Path $temporaryDirectory 'sessions.json'

New-Item -ItemType Directory -Path $temporaryDirectory -Force | Out-Null
try {
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $resolvedExecutablePath
    $startInfo.UseShellExecute = $false
    $startInfo.Arguments = "--agent-directory `"$demoAgentDirectory`" --index-file `"$temporaryIndexPath`" --disable-window-scan --disable-auto-refresh --redact-file-path"
    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw 'Demo application process could not be started.'
    }

    $process.WaitForExit()
}
finally {
    $resolvedTemporaryParent = (Resolve-Path -LiteralPath $temporaryParent).Path
    if (Test-Path -LiteralPath $temporaryDirectory) {
        $resolvedTemporaryDirectory = (Resolve-Path -LiteralPath $temporaryDirectory).Path
        if ($resolvedTemporaryDirectory.StartsWith($resolvedTemporaryParent, [System.StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
        }
    }
}
