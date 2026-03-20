param(
  [string]$HubRoot = "C:\AI-Hub",
  [string]$UserHome = $env:USERPROFILE,
  [string]$PersonalRoot,
  [switch]$SkipLegacyCodexPath
)

$ErrorActionPreference = "Stop"

function Normalize-Path {
  param([string]$Path)

  if ([string]::IsNullOrWhiteSpace($Path)) {
    return $null
  }

  return ([System.IO.Path]::GetFullPath($Path)).TrimEnd('\')
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
    [string]$TargetPath,
    [switch]$IgnoreIfLocked
  )

  if (-not (Test-Path -LiteralPath $TargetPath)) {
    throw "Target does not exist: $TargetPath"
  }

  Ensure-StandardDirectory (Split-Path -Parent $LinkPath)

  try {
    if (Test-Path -LiteralPath $LinkPath) {
      $existingTarget = Get-LinkTarget $LinkPath
      if ($existingTarget -and (Paths-Match $existingTarget $TargetPath)) {
        return
      }

      Backup-IfExists $LinkPath
    }

    New-Item -ItemType Junction -Path $LinkPath -Target $TargetPath | Out-Null
  }
  catch {
    if ($IgnoreIfLocked) {
      Write-Warning "Skipped locked path: $LinkPath"
      return
    }

    throw
  }
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

function Ensure-SkillsOverlay {
  param(
    [string]$RootPath,
    [string]$CompanyTarget,
    [string]$PersonalTarget
  )

  Ensure-StandardDirectory $RootPath
  Ensure-Junction (Join-Path $RootPath 'company') $CompanyTarget
  Ensure-Junction (Join-Path $RootPath 'personal') $PersonalTarget
}

$normalizedHubRoot = Normalize-Path $HubRoot
$normalizedUserHome = Normalize-Path $UserHome

if ([string]::IsNullOrWhiteSpace($PersonalRoot)) {
  $PersonalRoot = Join-Path $normalizedUserHome 'AI-Personal'
}

$normalizedPersonalRoot = Normalize-Path $PersonalRoot
$effectiveRoot = Join-Path $normalizedHubRoot '.runtime\effective\global'
$companySkills = Join-Path $normalizedHubRoot 'skills\global'
$personalSkills = Join-Path $normalizedPersonalRoot 'skills\global'
$effectiveCommands = Join-Path $effectiveRoot 'claude\commands'
$effectiveAgents = Join-Path $effectiveRoot 'claude\agents'

if (-not (Test-Path -LiteralPath $effectiveRoot)) {
  throw "Effective global output does not exist: $effectiveRoot"
}

Ensure-StandardDirectory (Join-Path $normalizedUserHome '.claude')
Ensure-StandardDirectory (Join-Path $normalizedUserHome '.agents')
Ensure-StandardDirectory (Join-Path $normalizedUserHome '.codex')
Ensure-StandardDirectory (Join-Path $normalizedUserHome '.gemini')
Ensure-StandardDirectory (Join-Path $normalizedUserHome '.gemini\antigravity')
Ensure-StandardDirectory $normalizedPersonalRoot
Ensure-StandardDirectory (Join-Path $normalizedPersonalRoot 'skills')
Ensure-StandardDirectory $companySkills
Ensure-StandardDirectory $personalSkills

Ensure-SkillsOverlay (Join-Path $normalizedUserHome '.claude\skills') $companySkills $personalSkills
Ensure-SkillsOverlay (Join-Path $normalizedUserHome '.agents\skills') $companySkills $personalSkills
Ensure-SkillsOverlay (Join-Path $normalizedUserHome '.gemini\antigravity\skills') $companySkills $personalSkills
Ensure-Junction (Join-Path $normalizedUserHome '.claude\commands') $effectiveCommands
Ensure-Junction (Join-Path $normalizedUserHome '.claude\agents') $effectiveAgents

Ensure-StandardDirectory (Join-Path $normalizedUserHome '.codex\skills')
if (-not $SkipLegacyCodexPath) {
  Ensure-Junction (Join-Path $normalizedUserHome '.codex\skills\ai-hub') $companySkills -IgnoreIfLocked
  Ensure-Junction (Join-Path $normalizedUserHome '.codex\skills\personal') $personalSkills -IgnoreIfLocked
}

Ensure-TextCopy (Join-Path $effectiveRoot 'claude\settings.json') (Join-Path $normalizedUserHome '.claude\settings.json')
Ensure-TextCopy (Join-Path $effectiveRoot 'mcp\claude.mcp.json') (Join-Path $normalizedUserHome '.claude.json')
Ensure-TextCopy (Join-Path $effectiveRoot 'mcp\codex.config.toml') (Join-Path $normalizedUserHome '.codex\config.toml')
Ensure-TextCopy (Join-Path $effectiveRoot 'mcp\antigravity.mcp.json') (Join-Path $normalizedUserHome '.gemini\antigravity\mcp_config.json')

Write-Host "Global links and effective client configs have been initialized."
