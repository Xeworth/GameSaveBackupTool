# Install Game Save Backup Tool (GSBT) on Windows.
# Usage (after you publish a GitHub Release with a setup .exe asset):
#   irm https://raw.githubusercontent.com/Xeworth/GameSaveBackupTool/main/src-winui/scripts/install.ps1 | iex
#
# Optional:
#   $env:GSBT_INSTALLER_URL = "https://github.com/Xeworth/GameSaveBackupTool/releases/download/v0.1.3.260619/gsbt-setup-0.1.3.260619.exe"
#   $env:GSBT_REPO = "Xeworth/GameSaveBackupTool"

$ErrorActionPreference = "Stop"

$Repo = if ([string]::IsNullOrWhiteSpace($env:GSBT_REPO)) { "Xeworth/GameSaveBackupTool" } else { $env:GSBT_REPO }
$DefaultTag = "latest"

function Get-InstallerUrl {
    param([string]$Tag = "latest")

    if ($env:GSBT_INSTALLER_URL) {
        return $env:GSBT_INSTALLER_URL
    }

    $api = "https://api.github.com/repos/$Repo/releases/$Tag"
    Write-Host "Resolving installer from GitHub release ($Tag)..."
    $release = Invoke-RestMethod -Uri $api -Headers @{ "User-Agent" = "gsbt-install" }
    $asset = $release.assets |
        Where-Object { $_.name -like "*setup*.exe" } |
        Select-Object -First 1
    if (-not $asset) {
        throw "No *setup*.exe found on release. Publish a release first or set `$env:GSBT_INSTALLER_URL."
    }
    Write-Host "Using release asset: $($asset.name)"
    return $asset.browser_download_url
}

function Install-Gsbt {
  param([string]$InstallerUrl)

  $temp = Join-Path $env:TEMP ("gsbt_setup_" + [guid]::NewGuid().ToString("n") + ".exe")
  Write-Host "Downloading $InstallerUrl"
  Invoke-WebRequest -Uri $InstallerUrl -OutFile $temp -UseBasicParsing

  Write-Host "Running installer (per-user, silent; adds gsbt to PATH if the default task is checked)..."
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
