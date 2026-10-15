[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$')]
    [string]$Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$VersionFile = Join-Path $RepositoryRoot 'Directory.Build.props'
if (-not (Test-Path -LiteralPath $VersionFile)) {
    throw "Version source was not found: $VersionFile"
}

[xml]$versionDocument = Get-Content -LiteralPath $VersionFile -Raw -ErrorAction Stop
$versionNode = $versionDocument.SelectSingleNode('/Project/PropertyGroup/Version')
if ($null -eq $versionNode) {
    throw "The single Version node was not found in: $VersionFile"
}

$versionNode.InnerText = $Version
$settings = [System.Xml.XmlWriterSettings]::new()
$settings.Encoding = [System.Text.UTF8Encoding]::new($false)
$settings.Indent = $true
$settings.NewLineChars = "`r`n"
$settings.NewLineHandling = [System.Xml.NewLineHandling]::Replace
$writer = [System.Xml.XmlWriter]::Create($VersionFile, $settings)
try {
    $versionDocument.Save($writer)
}
finally {
    $writer.Dispose()
}

Write-Output "[OK] Version updated to $Version"
