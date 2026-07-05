# Install GSBT CLI for Windows from a GitHub Release zip.
#
# Usage:
#   irm https://raw.githubusercontent.com/Xeworth/GameSaveBackupTool/main/src-cli-win/scripts/install.ps1 | iex
#
# Optional overrides:
#   $env:GSBT_CLI_ZIP_URL = "https://github.com/Xeworth/GameSaveBackupTool/releases/download/v0.2.0/gsbt-cli-win-v0.2.0.zip"
#   $env:GSBT_REPO = "Xeworth/GameSaveBackupTool"

[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA "Game Save Backup Tool"),
    [switch]$NoPath
)

$ErrorActionPreference = "Stop"
$Repo = if ([string]::IsNullOrWhiteSpace($env:GSBT_REPO)) { "Xeworth/GameSaveBackupTool" } else { $env:GSBT_REPO }

function Get-CliZipUrl {
    if (-not [string]::IsNullOrWhiteSpace($env:GSBT_CLI_ZIP_URL)) {
        return $env:GSBT_CLI_ZIP_URL
    }

    $api = "https://api.github.com/repos/$Repo/releases/latest"
    Write-Host "Resolving latest GSBT CLI package from $Repo..."
    $release = Invoke-RestMethod -Uri $api -Headers @{ "User-Agent" = "gsbt-cli-install" }

    $asset = $release.assets |
        Where-Object { $_.name -like "gsbt-cli-win*.zip" } |
        Select-Object -First 1

    if (-not $asset) {
        $asset = $release.assets |
            Where-Object { $_.name -like "*portable*.zip" } |
            Select-Object -First 1
    }

    if (-not $asset) {
        throw "No gsbt-cli-win*.zip or *portable*.zip asset found on the latest release. Set `$env:GSBT_CLI_ZIP_URL to test a direct zip URL."
    }

    Write-Host "Using release asset: $($asset.name)"
    return $asset.browser_download_url
}

function Add-UserPath {
    param([Parameter(Mandatory)][string]$Directory)

    $envKey = "HKCU:\Environment"
    $current = (Get-ItemProperty -Path $envKey -Name Path -ErrorAction SilentlyContinue).Path
    $parts = if ([string]::IsNullOrWhiteSpace($current)) { @() } else { $current -split ';' }
    $exists = $parts | Where-Object { $_.TrimEnd('\') -ieq $Directory.TrimEnd('\') } | Select-Object -First 1
    if ($exists) {
        Write-Host "PATH already contains: $Directory"
        return
    }

    $newPath = if ([string]::IsNullOrWhiteSpace($current)) { $Directory } else { "$current;$Directory" }
    Set-ItemProperty -Path $envKey -Name Path -Value $newPath
    $env:PATH = "$Directory;$env:PATH"
    Write-Host "Added to user PATH: $Directory"
    Write-Host "Open a new terminal for PATH changes to apply everywhere."
}

function Test-InstalledCli {
    param([Parameter(Mandatory)][string]$ExePath)

    if (-not (Test-Path -LiteralPath $ExePath)) {
        return $false
    }

    try {
        $status = & $ExePath status --ai 2>$null | ConvertFrom-Json
        if ($LASTEXITCODE -eq 0 -and $status.success -eq $true) {
            return $true
        }
    }
    catch {
        # Older builds may not support status --ai yet.
    }

    & $ExePath help *> $null
    return $LASTEXITCODE -eq 0
}

$zipUrl = Get-CliZipUrl
$tempRoot = Join-Path $env:TEMP ("gsbt-cli-install-" + [guid]::NewGuid().ToString("N"))
$zip = Join-Path $tempRoot "gsbt-cli-package.zip"
$extract = Join-Path $tempRoot "extract"
$installDirFull = $executionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($InstallDir)

try {
    New-Item -ItemType Directory -Force -Path $tempRoot, $extract, $installDirFull | Out-Null

    Write-Host "Downloading $zipUrl"
    Invoke-WebRequest -Uri $zipUrl -OutFile $zip -UseBasicParsing

    Write-Host "Extracting CLI package..."
    Expand-Archive -LiteralPath $zip -DestinationPath $extract -Force

    $exe = Get-ChildItem -LiteralPath $extract -Recurse -Filter "gsbt.exe" | Select-Object -First 1
    if (-not $exe) {
        throw "Downloaded package did not contain gsbt.exe."
    }

    $payloadRoot = $exe.Directory.FullName
    Get-ChildItem -LiteralPath $payloadRoot -Force | Copy-Item -Destination $installDirFull -Recurse -Force

    if (-not $NoPath) {
        Add-UserPath -Directory $installDirFull
    }

    $installedExe = Join-Path $installDirFull "gsbt.exe"
    if (-not (Test-InstalledCli -ExePath $installedExe)) {
        throw "Installed gsbt.exe could not be verified. Try running it directly or set `$env:GSBT_CLI_ZIP_URL."
    }

    Write-Host ""
    Write-Host "GSBT CLI installed successfully."
    Write-Host "Try:"
    Write-Host "  gsbt status"
    Write-Host "  gsbt list"
    if (-not (Test-Path (Join-Path $installDirFull "gsbt-main.exe"))) {
        Write-Host "  gsbt get gui    # after upgrading to a CLI build that supports it"
        Write-Host "  irm https://raw.githubusercontent.com/Xeworth/GameSaveBackupTool/main/src-winui/scripts/install.ps1 | iex"
    }
}
finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
