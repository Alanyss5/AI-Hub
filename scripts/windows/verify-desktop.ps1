[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$dotnet = 'C:\Users\Administrator\.dotnet\dotnet.exe'
$solution = Join-Path $repoRoot 'desktop\AIHub.sln'

if (-not $SkipBuild) {
    & $dotnet build $solution
    if ($LASTEXITCODE -ne 0) {
        throw 'dotnet build failed.'
    }
}

if (-not $SkipTests) {
    & $dotnet test $solution --no-build
    if ($LASTEXITCODE -ne 0) {
        throw 'dotnet test failed.'
    }
}

Write-Output 'Desktop verification completed.'
