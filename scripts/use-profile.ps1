param(
  [Parameter(Mandatory = $true)]
  [string]$ProjectPath,

  [Parameter(Mandatory = $true)]
  [string]$Profile,

  [string]$HubRoot = "C:\AI-Hub"
)

$ErrorActionPreference = "Stop"

function Normalize-Path {
  param([string]$Path)

  if ([string]::IsNullOrWhiteSpace($Path)) {
    return $null
  }

  return ([System.IO.Path]::GetFullPath($Path)).TrimEnd('\')
}

function Normalize-ProfileId {
  param([string]$Value)

  if ([string]::IsNullOrWhiteSpace($Value)) {
    return "global"
  }

  $trimmed = $Value.Trim().ToLowerInvariant()
  switch ($trimmed) {
    "global" { return "global" }
    "frontend" { return "frontend" }
    "backend" { return "backend" }
    "全局" { return "global" }
    "前端" { return "frontend" }
    "后端" { return "backend" }
  }

  $normalized = [System.Text.RegularExpressions.Regex]::Replace($trimmed, '[^a-z0-9]+', '-').Trim('-')
  if ([string]::IsNullOrWhiteSpace($normalized)) {
    return "global"
  }

  return $normalized
}

function Paths-Match {
  param(
    [string]$Left,
    [string]$Right
  )

  if ([string]::IsNullOrWhiteSpace($Left) -or [string]::IsNullOrWhiteSpace($Right)) {
    return $false
  }

  return (Normalize-Path $Left) -ieq (Normalize-Path $Right)
}

function Backup-IfExists {
  param([string]$Path)

  if (Test-Path -LiteralPath $Path) {
    $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $backupLeaf = '{0}.bak.{1}' -f (Split-Path -Leaf $Path), $timestamp
    Rename-Item -LiteralPath $Path -NewName $backupLeaf
  }
}

function Ensure-StandardDirectory {
  param([string]$Path)

  if (Test-Path -LiteralPath $Path) {
    $item = Get-Item -LiteralPath $Path -Force
    $isReparsePoint = ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0

    if ($item.PSIsContainer -and -not $isReparsePoint) {
      return
    }

    Backup-IfExists $Path
  }

  New-Item -ItemType Directory -Force -Path $Path | Out-Null
}

function Get-LinkTarget {
  param([string]$Path)

  if (-not (Test-Path -LiteralPath $Path)) {
    return $null
  }

  $item = Get-Item -LiteralPath $Path -Force
  $isReparsePoint = ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0
  if (-not $isReparsePoint) {
    return $null
  }

  if ($item.Target -is [System.Array]) {
    return [string]$item.Target[0]
  }

  return [string]$item.Target
}

function Ensure-Junction {
  param(
    [string]$LinkPath,
    [string]$TargetPath
  )

  if (-not (Test-Path -LiteralPath $TargetPath)) {
    throw "Target does not exist: $TargetPath"
  }

  Ensure-StandardDirectory (Split-Path -Parent $LinkPath)

  if (Test-Path -LiteralPath $LinkPath) {
    $existingTarget = Get-LinkTarget $LinkPath
    if ($existingTarget -and (Paths-Match $existingTarget $TargetPath)) {
      return
    }

    Backup-IfExists $LinkPath
  }

  New-Item -ItemType Junction -Path $LinkPath -Target $TargetPath | Out-Null
}

function Ensure-TextCopy {
  param(
    [string]$SourcePath,
    [string]$DestinationPath
  )

  if (-not (Test-Path -LiteralPath $SourcePath)) {
    return
  }

  $sourceContent = Get-Content -LiteralPath $SourcePath -Raw

  if (Test-Path -LiteralPath $DestinationPath) {
    $existingContent = Get-Content -LiteralPath $DestinationPath -Raw
    if ($existingContent -eq $sourceContent) {
      return
    }

    Backup-IfExists $DestinationPath
  }

  Ensure-StandardDirectory (Split-Path -Parent $DestinationPath)
  Set-Content -LiteralPath $DestinationPath -Value $sourceContent -Encoding UTF8 -NoNewline
}

$normalizedProjectPath = Normalize-Path $ProjectPath
if (-not (Test-Path -LiteralPath $normalizedProjectPath)) {
  throw "Project path does not exist: $normalizedProjectPath"
}

$normalizedProfile = Normalize-ProfileId $Profile
$normalizedHubRoot = Normalize-Path $HubRoot
$effectiveRoot = Join-Path $normalizedHubRoot ".runtime\effective\$normalizedProfile"

if (-not (Test-Path -LiteralPath $effectiveRoot)) {
  throw "Effective profile output does not exist: $effectiveRoot"
}

Ensure-StandardDirectory (Join-Path $normalizedProjectPath '.claude')
Ensure-StandardDirectory (Join-Path $normalizedProjectPath '.agents')
Ensure-StandardDirectory (Join-Path $normalizedProjectPath '.agent')
Ensure-StandardDirectory (Join-Path $normalizedProjectPath '.codex')

Ensure-Junction (Join-Path $normalizedProjectPath '.claude\skills') (Join-Path $effectiveRoot 'skills')
Ensure-Junction (Join-Path $normalizedProjectPath '.claude\commands') (Join-Path $effectiveRoot 'claude\commands')
Ensure-Junction (Join-Path $normalizedProjectPath '.claude\agents') (Join-Path $effectiveRoot 'claude\agents')
Ensure-Junction (Join-Path $normalizedProjectPath '.agents\skills') (Join-Path $effectiveRoot 'skills')
Ensure-Junction (Join-Path $normalizedProjectPath '.agent\skills') (Join-Path $effectiveRoot 'skills')

Ensure-TextCopy (Join-Path $effectiveRoot 'claude\settings.json') (Join-Path $normalizedProjectPath '.claude\settings.json')
Ensure-TextCopy (Join-Path $effectiveRoot 'mcp\claude.mcp.json') (Join-Path $normalizedProjectPath '.mcp.json')
Ensure-TextCopy (Join-Path $effectiveRoot 'mcp\codex.config.toml') (Join-Path $normalizedProjectPath '.codex\config.toml')

Write-Host "Project profile '$normalizedProfile' has been applied to $normalizedProjectPath"
