param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDir
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $PublishDir)) {
    Write-Error "Publish directory not found: $PublishDir"
}

# WinApp SDK + .NET may emit many xx-YY folders; ship US English only.
$keepLocaleNames = @('en-us')

# Required non-locale directories (case-insensitive).
$keepDirNames = @(
    'Assets',
    'assets',
    'audio',
    'video',
    'branding',
    'data',
    'Microsoft.UI.Xaml',
    'NpuDetect',
    'x64',
    'x86'
)

function Test-LocaleFolderName([string]$Name) {
    # BCP-47 style folders (de, de-DE, ca-Es-VALENCIA, zh-Hans-CN, …).
    return $Name -match '^(?i)[a-z]{2,3}(-[a-z0-9]+)+$' -or $Name -match '^(?i)[a-z]{2,3}$'
}

$removed = 0
Get-ChildItem -LiteralPath $PublishDir -Directory | ForEach-Object {
    $name = $_.Name
    $lower = $name.ToLowerInvariant()

    foreach ($keep in $keepDirNames) {
        if ($lower -eq $keep.ToLowerInvariant()) {
            return
        }
    }

    if (Test-LocaleFolderName $name) {
        if ($keepLocaleNames -contains $lower) {
            return
        }

        Remove-Item -LiteralPath $_.FullName -Recurse -Force
        $removed++
    }
}

Write-Host "Pruned $removed non-English locale folders from $PublishDir"
