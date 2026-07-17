# Install Game Save Backup Tool (GSBT) on Windows.
# Usage (after you publish a GitHub Release with GSBT_Setup_*.exe):
#   irm https://raw.githubusercontent.com/Xeworth/GameSaveBackupTool/main/src-winui/scripts/install.ps1 | iex
#
# Optional:
#   $env:GSBT_INSTALLER_URL = "https://github.com/.../GSBT_Setup_0.3.2.260717.exe"
#   & { ... }  # or save and run this script

$ErrorActionPreference = "Stop"

$Repo = "Xeworth/GameSaveBackupTool"
$DefaultTag = "latest"
$DefaultInstallDir = Join-Path $env:LOCALAPPDATA "Game Save Backup Tool"

function Add-UserPath {
  param([Parameter(Mandatory)][string]$Directory)

  $envKey = "HKCU:\Environment"
  $current = (Get-ItemProperty -Path $envKey -Name Path -ErrorAction SilentlyContinue).Path
  $parts = @()
  if (-not [string]::IsNullOrWhiteSpace($current)) {
    $parts = $current -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
  }

  if ($parts | Where-Object { $_.TrimEnd('\') -ieq $Directory.TrimEnd('\') }) {
    $env:PATH = "$Directory;$env:PATH"
    Write-Host "PATH already contains: $Directory"
    return
  }

  $newPath = if ([string]::IsNullOrWhiteSpace($current)) { $Directory } else { "$current;$Directory" }
  Set-ItemProperty -Path $envKey -Name Path -Value $newPath
  $env:PATH = "$Directory;$env:PATH"
  Write-Host "Added to user PATH: $Directory"
}

function Get-InstallerUrl {
    param([string]$Tag = "latest")

    if ($env:GSBT_INSTALLER_URL) {
        return $env:GSBT_INSTALLER_URL
    }

    $api = "https://api.github.com/repos/$Repo/releases/$Tag"
    Write-Host "Resolving installer from GitHub release ($Tag)..."
    $release = Invoke-RestMethod -Uri $api -Headers @{ "User-Agent" = "gsbt-install" }
    $asset = $release.assets | Where-Object { $_.name -like "GSBT_Setup_*.exe" } | Select-Object -First 1
    if (-not $asset) {
        throw "No GSBT_Setup_*.exe found on release. Publish a release first or set `$env:GSBT_INSTALLER_URL."
    }
    return $asset.browser_download_url
}

function Install-Gsbt {
  param([string]$InstallerUrl)

  $temp = Join-Path $env:TEMP ("gsbt_setup_" + [guid]::NewGuid().ToString("n") + ".exe")
  Write-Host "Downloading $InstallerUrl"
  Invoke-WebRequest -Uri $InstallerUrl -OutFile $temp -UseBasicParsing

  Write-Host "Running installer (per-user, adds gsbt to PATH)..."
  $proc = Start-Process -FilePath $temp -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/TASKS=addpath" -Wait -PassThru
  Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue

  if ($proc.ExitCode -ne 0) {
    throw "Installer exited with code $($proc.ExitCode)"
  }

  Add-UserPath -Directory $DefaultInstallDir

  Write-Host ""
  Write-Host "GSBT installed. Open a new terminal and try:"
  Write-Host "  gsbt list"
  Write-Host "  gsbt gui"
}

try {
  $url = Get-InstallerUrl -Tag $DefaultTag
  Install-Gsbt -InstallerUrl $url
}
catch {
  Write-Error $_
  exit 1
}
