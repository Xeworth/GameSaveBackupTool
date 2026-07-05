[CmdletBinding()]
param(
    [string]$Version = "dev"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$tfm = "net10.0-windows10.0.19041.0"
$publish = Join-Path $root "src\GSBT.Cli\bin\Release\$tfm\win-x64\publish"
$artifacts = Join-Path $root "artifacts"
$name = if ($Version -eq "dev") { "gsbt-cli-win.zip" } else { "gsbt-cli-win-$Version.zip" }
$zip = Join-Path $artifacts $name

& (Join-Path $PSScriptRoot "publish_cli.ps1") -Configuration Release -Runtime win-x64

Copy-Item -LiteralPath (Join-Path $root "README.md") -Destination (Join-Path $publish "README.md") -Force
Copy-Item -LiteralPath (Join-Path $root "LICENSE") -Destination (Join-Path $publish "LICENSE") -Force
Copy-Item -LiteralPath (Join-Path $root "PRIVACY.md") -Destination (Join-Path $publish "PRIVACY.md") -Force

New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
if (Test-Path -LiteralPath $zip) {
    Remove-Item -LiteralPath $zip -Force
}

Write-Host "Packing $zip"
Get-ChildItem -LiteralPath $publish -Force | Compress-Archive -DestinationPath $zip -Force
Write-Host "CLI package OK:"
Write-Host "  $zip"
