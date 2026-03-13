param(
  [string]$HubRoot = "C:\AI-Hub"
)

$ErrorActionPreference = "Stop"

function Ensure-Dir {
  param([string]$Path)

  New-Item -ItemType Directory -Force -Path $Path | Out-Null
}

function Convert-ToHashtable {
  param([object]$InputObject)

  if ($null -eq $InputObject) {
    return $null
  }

  if ($InputObject -is [System.Collections.IDictionary]) {
    $dict = @{}
    foreach ($key in $InputObject.Keys) {
      $dict[$key] = Convert-ToHashtable $InputObject[$key]
    }
    return $dict
  }

  if ($InputObject -is [System.Management.Automation.PSCustomObject]) {
    $dict = @{}
    foreach ($property in $InputObject.PSObject.Properties) {
      $dict[$property.Name] = Convert-ToHashtable $property.Value
    }
    return $dict
  }

  if ($InputObject -is [System.Collections.IEnumerable] -and $InputObject -isnot [string]) {
    $items = @()
    foreach ($item in $InputObject) {
      $items += ,(Convert-ToHashtable $item)
    }
    return $items
  }

  return $InputObject
}

function Merge-Servers {
  param(
    [hashtable]$BaseServers,
    [hashtable]$OverlayServers
  )

  $merged = @{}

  if ($BaseServers) {
    foreach ($key in $BaseServers.Keys) {
      $merged[$key] = Convert-ToHashtable $BaseServers[$key]
    }
  }

  if ($OverlayServers) {
    foreach ($key in $OverlayServers.Keys) {
      $merged[$key] = Convert-ToHashtable $OverlayServers[$key]
    }
  }

  return $merged
}

function Convert-ToCodexToml {
  param([hashtable]$Servers)

  $lines = New-Object System.Collections.Generic.List[string]

  foreach ($name in ($Servers.Keys | Sort-Object)) {
    $server = $Servers[$name]
    $lines.Add("[mcp_servers.$name]")
    $lines.Add("command = `"$($server.command)`"")

    if ($server.args) {
      $escapedArgs = $server.args | ForEach-Object { "`"$_`"" }
      $lines.Add("args = [" + ($escapedArgs -join ", ") + "]")
    }

    if ($server.env -and $server.env.Count -gt 0) {
      $lines.Add("[mcp_servers.$name.env]")
      foreach ($envKey in ($server.env.Keys | Sort-Object)) {
        $lines.Add("$envKey = `"$($server.env[$envKey])`"")
      }
    }

    $lines.Add("")
  }

  if ($lines.Count -eq 0) {
    return ""
  }

  return ($lines -join [Environment]::NewLine).Trim() + [Environment]::NewLine
}

$manifestDir = Join-Path $HubRoot 'mcp\manifest'
$claudeOut = Join-Path $HubRoot 'mcp\generated\claude'
$codexOut = Join-Path $HubRoot 'mcp\generated\codex'
$antigravityOut = Join-Path $HubRoot 'mcp\generated\antigravity'

Ensure-Dir $claudeOut
Ensure-Dir $codexOut
Ensure-Dir $antigravityOut

$manifests = Get-ChildItem -LiteralPath $manifestDir -Filter '*.json' -File
if (-not $manifests) {
  throw "No MCP manifest files found in $manifestDir"
}

$manifestMap = @{}
foreach ($manifest in $manifests) {
  $rawJson = Get-Content -LiteralPath $manifest.FullName -Raw | ConvertFrom-Json
  $json = Convert-ToHashtable $rawJson

  if (-not $json.ContainsKey('mcpServers')) {
    throw "Manifest missing 'mcpServers': $($manifest.FullName)"
  }

  $manifestMap[$manifest.BaseName] = $json
}

$globalServers = @{}
if ($manifestMap.ContainsKey('global')) {
  $globalServers = $manifestMap['global'].mcpServers
}

foreach ($profile in ($manifestMap.Keys | Sort-Object)) {
  $profileServers = $manifestMap[$profile].mcpServers
  $effectiveServers = if ($profile -eq 'global') { Merge-Servers @{} $profileServers } else { Merge-Servers $globalServers $profileServers }
  $effectiveManifest = @{ mcpServers = $effectiveServers }

  $effectiveManifest | ConvertTo-Json -Depth 10 | Set-Content -Path (Join-Path $claudeOut "$profile.mcp.json") -Encoding UTF8
  $effectiveManifest | ConvertTo-Json -Depth 10 | Set-Content -Path (Join-Path $antigravityOut "$profile.mcp.json") -Encoding UTF8

  $toml = Convert-ToCodexToml -Servers $effectiveServers
  Set-Content -Path (Join-Path $codexOut "$profile.config.toml") -Value $toml -Encoding UTF8
}

Write-Host "MCP configs generated successfully."
