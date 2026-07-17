param(
    [Parameter(Mandatory = $true)]
    [string]$CliPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [int[]]$Levels = @(1, 3, 5, 7, 9),

    [string[]]$Modes = @("chunky", "smooth"),

    [int]$PollSeconds = 5
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$CliPath = (Resolve-Path -LiteralPath $CliPath).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$summaryPath = Join-Path $OutputDirectory "summary.json"

function Invoke-CliJson {
    param([string[]]$Arguments)

    $output = & $CliPath @Arguments
    $exitCode = $LASTEXITCODE
    $text = $output -join [Environment]::NewLine
    if ($exitCode -ne 0) {
        throw "gsbt $($Arguments -join ' ') failed with exit code $exitCode`: $text"
    }

    return $text | ConvertFrom-Json
}

function Get-LatestProgressEvent {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    $lines = @(Get-Content -LiteralPath $Path -Tail 30 -ErrorAction SilentlyContinue)
    [array]::Reverse($lines)
    foreach ($line in $lines) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        try {
            $event = $line | ConvertFrom-Json
            if ($event.type -eq "progress" -and $null -ne $event.percent) {
                return $event
            }
        }
        catch {
            # The process may still be flushing the newest line; retry on the next poll.
        }
    }

    return $null
}

function Get-PlateauSummary {
    param([object[]]$Events, [DateTimeOffset]$FinishedAt)

    $percentEvents = @($Events | Where-Object { $null -ne $_.percent })
    if ($percentEvents.Count -eq 0) {
        return [pscustomobject]@{
            LongestPercent = $null
            LongestSeconds = 0.0
            Finalization99Seconds = 0.0
            NormalDuplicateEvents = 0
        }
    }

    $groups = New-Object System.Collections.Generic.List[object]
    $start = [DateTimeOffset]::Parse($percentEvents[0].timestampUtc)
    $percent = [int]$percentEvents[0].percent
    $normalDuplicates = 0
    for ($index = 1; $index -lt $percentEvents.Count; $index++) {
        $event = $percentEvents[$index]
        $eventPercent = [int]$event.percent
        $eventTime = [DateTimeOffset]::Parse($event.timestampUtc)
        if ($eventPercent -eq $percent) {
            $previousEvent = $percentEvents[$index - 1]
            if ($previousEvent.phase -eq "compress" -and
                $event.phase -eq "compress" -and
                $event.heartbeat -ne $true) {
                $normalDuplicates++
            }

            continue
        }

        $groups.Add([pscustomobject]@{
            Percent = $percent
            Seconds = [Math]::Round(($eventTime - $start).TotalSeconds, 3)
        })
        $percent = $eventPercent
        $start = $eventTime
    }

    $groups.Add([pscustomobject]@{
        Percent = $percent
        Seconds = [Math]::Round(($FinishedAt - $start).TotalSeconds, 3)
    })
    $longest = $groups | Sort-Object Seconds -Descending | Select-Object -First 1
    $at99 = $groups | Where-Object Percent -eq 99 | Select-Object -First 1
    return [pscustomobject]@{
        LongestPercent = $longest.Percent
        LongestSeconds = $longest.Seconds
        Finalization99Seconds = if ($null -eq $at99) { 0.0 } else { $at99.Seconds }
        NormalDuplicateEvents = $normalDuplicates
    }
}

$originalStatus = Invoke-CliJson @("status", "--ai")
$originalLevel = [int]$originalStatus.compression.level
$originalMode = [string]$originalStatus.compression.mode
$results = New-Object System.Collections.Generic.List[object]
if (Test-Path -LiteralPath $summaryPath) {
    $savedResults = @(Get-Content -Raw -LiteralPath $summaryPath | ConvertFrom-Json)
    foreach ($savedResult in $savedResults) {
        $results.Add($savedResult)
    }

    Write-Output "Resuming with $($results.Count) completed run(s)."
}

try {
    foreach ($mode in $Modes) {
        foreach ($level in $Levels) {
            $runName = "$mode-mx$level"
            $completedRun = $results | Where-Object {
                $_.Mode -eq $mode -and [int]$_.Level -eq $level -and $_.Success -eq $true
            } | Select-Object -First 1
            if ($null -ne $completedRun) {
                Write-Output "[$runName] already complete; skipping"
                continue
            }

            Write-Output "[$runName] configuring"
            $null = Invoke-CliJson @("settings", "compression", "set", "mode", $mode, "--ai")
            $null = Invoke-CliJson @("settings", "compression", "set", "level", $level.ToString(), "--ai")

            $stdoutPath = Join-Path $OutputDirectory "$runName.stdout.json"
            $stderrPath = Join-Path $OutputDirectory "$runName.stderr.ndjson"
            $startedAt = [DateTimeOffset]::UtcNow
            $stopwatch = [Diagnostics.Stopwatch]::StartNew()
            $startProcessArguments = @{
                FilePath = $CliPath
                ArgumentList = "compress --ai"
                RedirectStandardOutput = $stdoutPath
                RedirectStandardError = $stderrPath
                WindowStyle = "Hidden"
                PassThru = $true
            }
            $process = Start-Process @startProcessArguments

            $lastReportedPercent = -1
            $lastReportedHeartbeatSeconds = -1
            while (-not $process.HasExited) {
                Start-Sleep -Seconds $PollSeconds
                $event = Get-LatestProgressEvent $stderrPath
                if ($null -eq $event) {
                    continue
                }

                $percent = [int]$event.percent
                $heartbeatSeconds = if ($null -eq $event.plateauSeconds) { -1 } else { [int]$event.plateauSeconds }
                if ($percent -ne $lastReportedPercent -or $heartbeatSeconds -ne $lastReportedHeartbeatSeconds) {
                    $suffix = if ($event.heartbeat -eq $true) { " heartbeat; plateau $heartbeatSeconds s" } else { "" }
                    Write-Output "[$runName] $percent%$suffix"
                    $lastReportedPercent = $percent
                    $lastReportedHeartbeatSeconds = $heartbeatSeconds
                }
            }

            $process.WaitForExit()
            $stopwatch.Stop()
            $finishedAt = [DateTimeOffset]::UtcNow
            $stdout = Get-Content -Raw -LiteralPath $stdoutPath | ConvertFrom-Json
            $eventLines = @(Get-Content -LiteralPath $stderrPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
            $events = @($eventLines | ForEach-Object { $_ | ConvertFrom-Json })
            $plateau = Get-PlateauSummary $events $finishedAt
            $result = [pscustomobject]@{
                Mode = $mode
                Level = $level
                ExitCode = $process.ExitCode
                Success = [bool]$stdout.success
                StartedAtUtc = $startedAt.ToString("O")
                FinishedAtUtc = $finishedAt.ToString("O")
                HarnessElapsedSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 3)
                CompressionElapsedSeconds = [Math]::Round([double]$stdout.metrics.elapsedSeconds, 3)
                InputBytes = [long]$stdout.metrics.inputBytes
                ArchiveBytes = [long]$stdout.metrics.archiveBytes
                ArchivePercentOfInput = [double]$stdout.metrics.archivePercentOfInput
                EventCount = $events.Count
                CompressionEventCount = @($events | Where-Object phase -eq "compress").Count
                HeartbeatCount = @($events | Where-Object heartbeat -eq $true).Count
                UniquePercentCount = @($events.percent | Where-Object { $null -ne $_ } | Sort-Object -Unique).Count
                NormalDuplicateEvents = $plateau.NormalDuplicateEvents
                LongestPlateauPercent = $plateau.LongestPercent
                LongestPlateauSeconds = $plateau.LongestSeconds
                Finalization99Seconds = $plateau.Finalization99Seconds
                ArchivePath = [string]$stdout.archivePath
                StdoutFile = [IO.Path]::GetFileName($stdoutPath)
                StderrFile = [IO.Path]::GetFileName($stderrPath)
            }
            $results.Add($result)
            $results | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
            Write-Output "[$runName] complete in $($result.CompressionElapsedSeconds) s; $($result.EventCount) events; longest plateau $($result.LongestPlateauPercent)% for $($result.LongestPlateauSeconds) s"
            Start-Sleep -Seconds 2
        }
    }
}
finally {
    Write-Output "Restoring compression settings: $originalMode mx$originalLevel"
    $null = Invoke-CliJson @("settings", "compression", "set", "mode", $originalMode, "--ai")
    $null = Invoke-CliJson @("settings", "compression", "set", "level", $originalLevel.ToString(), "--ai")
}

$results | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
$results | Export-Csv -LiteralPath (Join-Path $OutputDirectory "summary.csv") -NoTypeInformation -Encoding UTF8
Write-Output "Benchmark complete: $summaryPath"
