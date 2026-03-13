param(
  [string]$HubRoot = "C:\AI-Hub",
  [string]$UserHome = $env:USERPROFILE,
  [string]$PersonalRoot,
  [switch]$SkipLegacyCodexPath
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($PersonalRoot)) {
  $PersonalRoot = Join-Path $UserHome 'AI-Personal'
}

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

  $targetValue = $item.Target
  if ($null -eq $targetValue) {
    return $null
  }

  if ($targetValue -is [System.Array]) {
    return [string]$targetValue[0]
  }

  return [string]$targetValue
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

function Ensure-RenderedTemplate {
  param(
    [string]$TemplatePath,
    [string]$DestinationPath,
    [string]$HubRootValue
  )

  if (-not (Test-Path -LiteralPath $TemplatePath)) {
    return
  }

  $content = Get-Content -LiteralPath $TemplatePath -Raw
  $hubRootJson = $HubRootValue -replace '\\', '\\\\'
  $content = $content.Replace('__AI_HUB_ROOT_JSON__', $hubRootJson)

  if (Test-Path -LiteralPath $DestinationPath) {
    $existingContent = Get-Content -LiteralPath $DestinationPath -Raw
    if ($existingContent -eq $content) {
      return
    }

    Backup-IfExists $DestinationPath
  }

  Set-Content -Path $DestinationPath -Value $content -Encoding UTF8 -NoNewline
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

Ensure-StandardDirectory (Join-Path $UserHome '.claude')
Ensure-StandardDirectory (Join-Path $UserHome '.agents')
Ensure-StandardDirectory (Join-Path $UserHome '.codex')
Ensure-StandardDirectory (Join-Path $UserHome '.gemini')
Ensure-StandardDirectory (Join-Path $UserHome '.gemini\antigravity')
Ensure-StandardDirectory $PersonalRoot
Ensure-StandardDirectory (Join-Path $PersonalRoot 'skills')
Ensure-StandardDirectory (Join-Path $PersonalRoot 'skills\global')

$companySkills = Join-Path $HubRoot 'skills\global'
$personalSkills = Join-Path $PersonalRoot 'skills\global'

Ensure-SkillsOverlay (Join-Path $UserHome '.claude\skills') $companySkills $personalSkills
Ensure-Junction (Join-Path $UserHome '.claude\commands') (Join-Path $HubRoot 'claude\commands\global')
Ensure-Junction (Join-Path $UserHome '.claude\agents') (Join-Path $HubRoot 'claude\agents\global')
Ensure-SkillsOverlay (Join-Path $UserHome '.agents\skills') $companySkills $personalSkills
Ensure-SkillsOverlay (Join-Path $UserHome '.gemini\antigravity\skills') $companySkills $personalSkills

Ensure-StandardDirectory (Join-Path $UserHome '.codex\skills')
if (-not $SkipLegacyCodexPath) {
  Ensure-Junction (Join-Path $UserHome '.codex\skills\ai-hub') $companySkills -IgnoreIfLocked
  Ensure-Junction (Join-Path $UserHome '.codex\skills\personal') $personalSkills -IgnoreIfLocked
}

Ensure-RenderedTemplate (Join-Path $HubRoot 'claude\settings\global.settings.json') (Join-Path $UserHome '.claude\settings.json') $HubRoot

Write-Host "Global links and Claude settings have been initialized."
