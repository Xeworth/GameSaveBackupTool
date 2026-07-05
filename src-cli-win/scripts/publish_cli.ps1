[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\GSBT.Cli\GSBT.Cli.csproj"
$dotnet = if ([string]::IsNullOrWhiteSpace($env:DOTNET)) { "dotnet" } else { $env:DOTNET }

Write-Host "Publishing GSBT CLI ($Configuration, $Runtime, self-contained)..."
& $dotnet publish $project -c $Configuration -r $Runtime -p:SelfContained=true
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$tfm = "net10.0-windows10.0.19041.0"
$publish = Join-Path $root "src\GSBT.Cli\bin\$Configuration\$tfm\$Runtime\publish"
$required = @(
    "gsbt.exe",
    "7z.dll",
    "data\ludusavi-save-manifest.json"
)

foreach ($file in $required) {
    $path = Join-Path $publish $file
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Publish missing required file: $path"
    }
}

Write-Host "CLI publish OK:"
Write-Host "  $publish"
