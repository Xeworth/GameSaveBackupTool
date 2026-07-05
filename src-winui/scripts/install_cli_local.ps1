# Build and install the local GSBT CLI.
#
# Default install dir:
#   %LocalAppData%\Game Save Backup Tool
#
# Examples:
#   .\scripts\install_cli_local.ps1
#   .\scripts\install_cli_local.ps1 -InstallDir "D:\Tools\GSBT"
#   .\scripts\install_cli_local.ps1 -NoPath

[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA "Game Save Backup Tool"),
    [switch]$NoPath,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([Parameter(Mandatory)][string]$Path)

    $executionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

function Get-DotnetCommand {
    param([Parameter(Mandatory)][string]$RepoRoot)

    $repoDotnet = Join-Path $RepoRoot ".dotnet\dotnet.exe"
    if (Test-Path -LiteralPath $repoDotnet) {
        $env:DOTNET_ROOT = Split-Path -Parent $repoDotnet
        $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
        return $repoDotnet
    }

    $userDotnet = Join-Path $env:USERPROFILE ".dotnet10\dotnet.exe"
    if (Test-Path -LiteralPath $userDotnet) {
        $env:DOTNET_ROOT = Split-Path -Parent $userDotnet
        $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
        return $userDotnet
    }

    return "dotnet"
}

function Add-UserPath {
    param([Parameter(Mandatory)][string]$Directory)

    $envKey = "HKCU:\Environment"
    $current = (Get-ItemProperty -Path $envKey -Name Path -ErrorAction SilentlyContinue).Path
    $parts = @()
    if (-not [string]::IsNullOrWhiteSpace($current)) {
        $parts = $current -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    }

    $alreadyPresent = $parts | Where-Object { $_.TrimEnd('\') -ieq $Directory.TrimEnd('\') } | Select-Object -First 1
    if ($alreadyPresent) {
        Write-Host "PATH already contains: $Directory"
        return
    }

    $newPath = if ([string]::IsNullOrWhiteSpace($current)) { $Directory } else { "$current;$Directory" }
    Set-ItemProperty -Path $envKey -Name Path -Value $newPath
    $env:PATH = "$Directory;$env:PATH"
    Write-Host "Added to user PATH: $Directory"
    Write-Host "Open a new terminal for PATH changes to apply everywhere."
}

$scriptRoot = Split-Path -Parent $PSCommandPath
$gsbtRoot = Resolve-FullPath (Join-Path $scriptRoot "..")
$repoRoot = Resolve-FullPath (Join-Path $gsbtRoot "..")
$tfm = "net10.0-windows10.0.19041.0"
$rid = "win-x64"
$dotnet = Get-DotnetCommand -RepoRoot $repoRoot
$cliProject = Join-Path $gsbtRoot "src\GSBT.Cli\GSBT.Cli.csproj"
$publishDir = Join-Path $gsbtRoot "src\GSBT.Cli\bin\Release\$tfm\$rid\publish"
$installDirFull = Resolve-FullPath $InstallDir

if (-not $NoBuild) {
    Write-Host "Publishing GSBT CLI with $dotnet"
    & $dotnet publish $cliProject -c Release -r $rid -p:SelfContained=true
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }
}

$requiredFiles = @(
    "gsbt.exe",
    "7z.dll",
    "data\ludusavi-save-manifest.json"
)

foreach ($file in $requiredFiles) {
    $path = Join-Path $publishDir $file
    if (-not (Test-Path -LiteralPath $path)) {
        throw "CLI publish is missing required file: $path"
    }
}

New-Item -ItemType Directory -Path $installDirFull -Force | Out-Null
Write-Host "Installing CLI into: $installDirFull"
Get-ChildItem -LiteralPath $publishDir -Force | Copy-Item -Destination $installDirFull -Recurse -Force

if (-not $NoPath) {
    Add-UserPath -Directory $installDirFull
}

$exe = Join-Path $installDirFull "gsbt.exe"
Write-Host "Verifying installed CLI..."
$statusJson = & $exe status --ai
if ($LASTEXITCODE -ne 0) {
    throw "Installed gsbt.exe status --ai failed with exit code $LASTEXITCODE"
}

$status = $statusJson | ConvertFrom-Json
if ($status.schemaVersion -ne 1 -or $status.command -ne "status" -or $status.success -ne $true) {
    throw "Installed gsbt.exe returned an unexpected status payload."
}

Write-Host ""
Write-Host "GSBT CLI installed successfully."
Write-Host "Try:"
Write-Host "  gsbt status --ai"
Write-Host "  gsbt list"
Write-Host ""
Write-Host "Installed executable:"
Write-Host "  $exe"
