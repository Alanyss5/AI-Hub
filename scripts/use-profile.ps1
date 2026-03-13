param(
  [Parameter(Mandatory = $true)]
  [string]$ProjectPath,

  [Parameter(Mandatory = $true)]
  [ValidateSet("global", "frontend", "backend")]
  [string]$Profile,

  [string]$HubRoot = "C:\AI-Hub"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $ProjectPath)) {
  throw "Project path does not exist: $ProjectPath"
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

  Set-Content -LiteralPath $DestinationPath -Value $sourceContent -Encoding UTF8 -NoNewline
}

Ensure-StandardDirectory (Join-Path $ProjectPath '.claude')
Ensure-StandardDirectory (Join-Path $ProjectPath '.agents')
Ensure-StandardDirectory (Join-Path $ProjectPath '.agent')
Ensure-StandardDirectory (Join-Path $ProjectPath '.codex')

Ensure-Junction (Join-Path $ProjectPath '.claude\skills') (Join-Path $HubRoot "skills\$Profile")
Ensure-Junction (Join-Path $ProjectPath '.claude\commands') (Join-Path $HubRoot "claude\commands\$Profile")
Ensure-Junction (Join-Path $ProjectPath '.claude\agents') (Join-Path $HubRoot "claude\agents\$Profile")
Ensure-Junction (Join-Path $ProjectPath '.agents\skills') (Join-Path $HubRoot "skills\$Profile")
Ensure-Junction (Join-Path $ProjectPath '.agent\skills') (Join-Path $HubRoot "skills\$Profile")

Ensure-RenderedTemplate (Join-Path $HubRoot "claude\settings\$Profile.settings.json") (Join-Path $ProjectPath '.claude\settings.json') $HubRoot
Ensure-TextCopy (Join-Path $HubRoot "mcp\generated\claude\$Profile.mcp.json") (Join-Path $ProjectPath '.mcp.json')
Ensure-TextCopy (Join-Path $HubRoot "mcp\generated\codex\$Profile.config.toml") (Join-Path $ProjectPath '.codex\config.toml')

Write-Host "Project profile '$Profile' has been applied to $ProjectPath"

