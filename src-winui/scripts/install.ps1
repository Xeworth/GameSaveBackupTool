# Install Game Save Backup Tool (GSBT) on Windows.
# Usage (after you publish a GitHub Release with GSBT_Setup_*.exe):
#   irm https://raw.githubusercontent.com/Xeworth/GameSaveBackupTool/main/src-winui/scripts/install.ps1 | iex
#
# Optional:
#   $env:GSBT_INSTALLER_URL = "https://github.com/.../GSBT_Setup_0.2.0.260704.exe"
#   & { ... }  # or save and run this script

$ErrorActionPreference = "Stop"

$Repo = "Xeworth/GameSaveBackupTool"
$DefaultTag = "latest"

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

  Write-Host "Running installer (per-user, adds gsbt to PATH if you leave the default task checked)..."
  $proc = Start-Process -FilePath $temp -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART" -Wait -PassThru
  Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue

  if ($proc.ExitCode -ne 0) {
    throw "Installer exited with code $($proc.ExitCode)"
  }

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
