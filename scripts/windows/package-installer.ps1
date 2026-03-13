[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$OutputRoot,

    [string]$IsccPath,

    [bool]$AllowZipFallback = $true
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$templatePath = Join-Path $repoRoot 'installer\AIHub.Desktop.iss'
$resolvedPublishDirectory = (Resolve-Path -LiteralPath $PublishDirectory).Path

if (-not $OutputRoot) {
    $OutputRoot = Join-Path $repoRoot 'desktop\.artifacts\installer'
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

function Write-PackageManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackagePath,

        [Parameter(Mandatory = $true)]
        [string]$PackageKind,

        [Parameter(Mandatory = $true)]
        [string]$PackagingTool
    )

    $manifest = [ordered]@{
        createdAt = [DateTimeOffset]::Now.ToString('O')
        version = $Version
        publishDirectory = $resolvedPublishDirectory
        packageKind = $PackageKind
        packagingTool = $PackagingTool
        packagePath = $PackagePath
    }

    $manifestPath = Join-Path $OutputRoot 'package-manifest.json'
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding utf8
}

if (-not $IsccPath) {
    $candidates = @(
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe'
    )

    $IsccPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}

if (-not $IsccPath) {
    if (-not $AllowZipFallback) {
        throw 'ISCC.exe not found. Install Inno Setup 6 or pass -IsccPath explicitly.'
    }

    $zipPath = Join-Path $OutputRoot "aihub-desktop-$Version-win-x64-portable.zip"
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    Compress-Archive -Path (Join-Path $resolvedPublishDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal
    Write-PackageManifest -PackagePath $zipPath -PackageKind 'portable-zip' -PackagingTool 'PowerShell.Compress-Archive'
    Write-Warning 'ISCC.exe not found. Created a portable zip package instead of an installer.'
    Write-Output $zipPath
    return
}

$arguments = @(
    "/DAppVersion=$Version",
    "/DPublishDir=$resolvedPublishDirectory",
    "/DOutputDir=$OutputRoot",
    "/DRepoRoot=$repoRoot",
    $templatePath
)

& $IsccPath @arguments
if ($LASTEXITCODE -ne 0) {
    throw 'Inno Setup packaging failed.'
}

$installerPath = Join-Path $OutputRoot "aihub-desktop-$Version-win-x64.exe"
Write-PackageManifest -PackagePath $installerPath -PackageKind 'inno-setup-installer' -PackagingTool $IsccPath
Write-Output $installerPath
