[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$VersionSuffix = 'internal',
    [string]$OutputRoot,
    [string]$HubRootToBackup,
    [switch]$SkipBackup,
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$desktopRoot = Join-Path $repoRoot 'desktop'
$appProject = Join-Path $desktopRoot 'apps\AIHub.Desktop\AIHub.Desktop.csproj'
$backupScript = Join-Path $PSScriptRoot 'backup-hub-state.ps1'
$packageScript = Join-Path $PSScriptRoot 'package-installer.ps1'
$dotnet = 'C:\Users\Administrator\.dotnet\dotnet.exe'
$nugetConfig = Join-Path $desktopRoot 'NuGet.Config'

if (-not $OutputRoot) {
    $OutputRoot = Join-Path $desktopRoot '.artifacts'
}

if (-not $SkipBackup -and $HubRootToBackup) {
    & $backupScript -HubRoot $HubRootToBackup -OutputRoot (Join-Path $OutputRoot 'hub-backups') -Label 'release' -CreateZip | Out-Host
}

$version = (& $dotnet msbuild $appProject -nologo '-getProperty:Version' "/p:VersionSuffix=$VersionSuffix").Trim()
if (-not $version) {
    throw 'Failed to resolve application version.'
}

$publishDirectory = Join-Path $OutputRoot (Join-Path 'publish' (Join-Path $version $Runtime))
New-Item -ItemType Directory -Force -Path $publishDirectory | Out-Null

& $dotnet publish $appProject -c $Configuration -r $Runtime --self-contained true --configfile $nugetConfig -p:PublishSingleFile=false -p:VersionSuffix=$VersionSuffix -o $publishDirectory -v minimal
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet publish failed.'
}

$hashes = Get-ChildItem -LiteralPath $publishDirectory -Recurse -File |
    ForEach-Object {
        [ordered]@{
            path = $_.FullName.Substring($publishDirectory.Length).TrimStart('\')
            size = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }
    }

$manifest = [ordered]@{
    createdAt = [DateTimeOffset]::Now.ToString('O')
    configuration = $Configuration
    runtime = $Runtime
    version = $version
    publishDirectory = $publishDirectory
    selfContained = $true
    packageArtifact = $null
    hashes = $hashes
}

if (-not $SkipInstaller -and (Test-Path -LiteralPath $packageScript)) {
    $packageArtifact = & $packageScript -PublishDirectory $publishDirectory -Version $version -OutputRoot (Join-Path $OutputRoot 'installer')
    if ($LASTEXITCODE -ne 0) {
        throw 'desktop packaging failed.'
    }

    if ($packageArtifact) {
        $manifest.packageArtifact = $packageArtifact | Select-Object -Last 1
    }
}

$manifestPath = Join-Path $publishDirectory 'release-manifest.json'
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding utf8

Write-Output $publishDirectory
