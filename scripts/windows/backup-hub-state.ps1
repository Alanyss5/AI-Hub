[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$HubRoot,

    [string]$OutputRoot,

    [string]$Label = 'upgrade-preflight',

    [switch]$CreateZip
)

$ErrorActionPreference = 'Stop'

$resolvedHubRoot = (Resolve-Path -LiteralPath $HubRoot).Path
if (-not $OutputRoot) {
    $OutputRoot = Join-Path $resolvedHubRoot 'backups'
}

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupRoot = Join-Path $OutputRoot (Join-Path $Label $timestamp)
New-Item -ItemType Directory -Force -Path $backupRoot | Out-Null

$targets = @(
    'config',
    'projects',
    'mcp',
    'skills-overrides'
)

$included = New-Object System.Collections.Generic.List[string]
foreach ($target in $targets) {
    $sourcePath = Join-Path $resolvedHubRoot $target
    if (-not (Test-Path -LiteralPath $sourcePath)) {
        continue
    }

    $destinationPath = Join-Path $backupRoot $target
    Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Recurse -Force
    $included.Add($target)
}

$manifest = [ordered]@{
    createdAt = [DateTimeOffset]::Now.ToString('O')
    hubRoot = $resolvedHubRoot
    label = $Label
    backupRoot = $backupRoot
    included = $included
}
$manifestPath = Join-Path $backupRoot 'backup-manifest.json'
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding utf8

if ($CreateZip) {
    $zipPath = "$backupRoot.zip"
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    Compress-Archive -Path (Join-Path $backupRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Output $zipPath
    return
}

Write-Output $backupRoot