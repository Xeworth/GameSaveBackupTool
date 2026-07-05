[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA "Game Save Backup Tool"),
    [switch]$NoPath,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$tfm = "net10.0-windows10.0.19041.0"
$publish = Join-Path $root "src\GSBT.Cli\bin\Release\$tfm\win-x64\publish"

if (-not $NoBuild) {
    & (Join-Path $PSScriptRoot "publish_cli.ps1") -Configuration Release -Runtime win-x64
}

$installDirFull = $executionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($InstallDir)
New-Item -ItemType Directory -Force -Path $installDirFull | Out-Null
Get-ChildItem -LiteralPath $publish -Force | Copy-Item -Destination $installDirFull -Recurse -Force

if (-not $NoPath) {
    $envKey = "HKCU:\Environment"
    $current = (Get-ItemProperty -Path $envKey -Name Path -ErrorAction SilentlyContinue).Path
    $parts = if ([string]::IsNullOrWhiteSpace($current)) { @() } else { $current -split ';' }
    $exists = $parts | Where-Object { $_.TrimEnd('\') -ieq $installDirFull.TrimEnd('\') } | Select-Object -First 1
    if (-not $exists) {
        $newPath = if ([string]::IsNullOrWhiteSpace($current)) { $installDirFull } else { "$current;$installDirFull" }
        Set-ItemProperty -Path $envKey -Name Path -Value $newPath
        $env:PATH = "$installDirFull;$env:PATH"
        Write-Host "Added to user PATH: $installDirFull"
    }
}

$exe = Join-Path $installDirFull "gsbt.exe"
$status = & $exe status --ai | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $status.success -ne $true) {
    throw "Installed gsbt.exe did not pass status --ai verification."
}

Write-Host "GSBT CLI installed:"
Write-Host "  $exe"
