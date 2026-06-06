using System.Text.Json;
using System.Text.Json.Serialization;
using GSBT.Core.Services;
using GSBT.WinUI;

namespace GSBT.WinUI.Services;

internal static class SandboxBenchmarkFormat
{
    public static SandboxCompressionBenchmarkEntry FromResult(string backupRootDisplay, BackupCompressionResult r)
    {
        var wall = r.WallSeconds;
        var raw = r.RawBytes;
        var arch = r.ArchiveBytes;
        var ratioPct = raw > 0 ? 100.0 * arch / raw : 0.0;
        var mibPerS = wall > 0.001 && raw > 0 ? (raw / (1024.0 * 1024.0)) / wall : 0.0;
        var o = r.Options;
        var archiveName = string.IsNullOrEmpty(r.ArchivePath) ? "—" : Path.GetFileName(r.ArchivePath);
        var title = r.Success
            ? $"{archiveName} · {wall:F2}s · {_human(arch)} archive"
            : $"Failed · {r.Message}";
        var priorityLines = new[]
        {
            $"Engine: {o.Engine}  |  {o.SummaryLabel}",
            $"Time elapsed: {wall:F3} s",
            $"Raw size: {_human(raw)} ({raw:N0} bytes)",
            $"Archive size: {_human(arch)} ({arch:N0} bytes)",
            $"Archive / raw: {ratioPct:F1}%",
            $"Mean throughput (input): {mibPerS:F2} MiB/s",
        };
        var extraLines = new List<string>
        {
            $"When (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}",
            $"Backup folder: {backupRootDisplay}",
            $"Archive file: {archiveName}",
            $"Outcome: {(r.Success ? "OK" : "Error")} — {r.Message}",
        };
        var priorityText = string.Join(Environment.NewLine, priorityLines);
        var extraText = string.Join(Environment.NewLine, extraLines);
        return new SandboxCompressionBenchmarkEntry
        {
            Id = Guid.NewGuid(),
            UtcTicks = DateTime.UtcNow.Ticks,
            BackupRootDisplay = backupRootDisplay,
            ArchivePath = r.ArchivePath,
            Success = r.Success,
            TitleLine = title,
            PriorityDetailText = priorityText,
            ExtraDetailText = extraText,
            DetailText = priorityText + Environment.NewLine + Environment.NewLine + extraText,
            WallSeconds = wall,
            RawBytes = raw,
            ArchiveBytes = arch,
            Engine = o.Engine,
            OptionsSummary = o.SummaryLabel,
        };
    }

    private static string _human(long bytes)
    {
        var n = (double)Math.Max(0, bytes);
        if (n >= 1024 * 1024 * 1024)
        {
            return $"{n / (1024 * 1024 * 1024):F2} GiB";
        }

        if (n >= 1024 * 1024)
        {
            return $"{n / (1024 * 1024):F2} MiB";
        }

        if (n >= 1024)
        {
            return $"{n / 1024:F2} KiB";
        }

        return $"{bytes} B";
    }
}

/// <summary>Persists Sandbox monitor → Benchmark tab rows under %AppData%\Roaming\GSBT\winui.</summary>
public sealed class SandboxCompressionBenchmarkStore
{
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly object _gate = new();

    public Task<IReadOnlyList<SandboxCompressionBenchmarkEntry>> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = AppPaths.SandboxCompressionBenchmarksPath;
                lock (_gate)
                {
                    try
                    {
                        if (!File.Exists(path))
                        {
                            return (IReadOnlyList<SandboxCompressionBenchmarkEntry>)Array.Empty<SandboxCompressionBenchmarkEntry>();
                        }

                        var json = File.ReadAllText(path);
                        var root = JsonSerializer.Deserialize<BenchmarkFileDto>(json, _json);
                        if (root?.Entries is not { Count: > 0 } list)
                        {
                            return (IReadOnlyList<SandboxCompressionBenchmarkEntry>)Array.Empty<SandboxCompressionBenchmarkEntry>();
                        }

                        return list.OrderByDescending(e => e.UtcTicks).ToList();
                    }
                    catch
                    {
                        return (IReadOnlyList<SandboxCompressionBenchmarkEntry>)Array.Empty<SandboxCompressionBenchmarkEntry>();
                    }
                }
            },
            cancellationToken);

    public Task SaveAllAsync(IReadOnlyList<SandboxCompressionBenchmarkEntry> entries, CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(AppPaths.WinUiUserDataRoot);
                var path = AppPaths.SandboxCompressionBenchmarksPath;
                var ordered = entries.OrderByDescending(e => e.UtcTicks).Take(200).ToList();
                var dto = new BenchmarkFileDto { Schema = 1, Entries = ordered };
                var json = JsonSerializer.Serialize(dto, _json);
                lock (_gate)
                {
                    File.WriteAllText(path, json);
                }
            },
            cancellationToken);

    public async Task AppendAsync(SandboxCompressionBenchmarkEntry entry, CancellationToken cancellationToken = default)
    {
        var list = (await LoadAsync(cancellationToken).ConfigureAwait(false)).ToList();
        list.Insert(0, entry);
        await SaveAllAsync(list, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> ExportJsonAsync(CancellationToken cancellationToken = default)
    {
        var list = (await LoadAsync(cancellationToken).ConfigureAwait(false)).OrderByDescending(e => e.UtcTicks).ToList();
        await Task.Yield();
        return ExportEntriesJson(list);
    }

    /// <summary>Merges entries from another export; skips duplicate <see cref="SandboxCompressionBenchmarkEntry.Id"/>.</summary>
    public async Task<int> MergeImportFromJsonAsync(string json, CancellationToken cancellationToken = default)
    {
        BenchmarkFileDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<BenchmarkFileDto>(json, _json);
        }
        catch
        {
            return 0;
        }

        if (dto?.Entries is not { Count: > 0 } incoming)
        {
            return 0;
        }

        var cur = (await LoadAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var ids = new HashSet<Guid>(cur.Select(e => e.Id));
        var added = 0;
        foreach (var e in incoming)
        {
            if (e.Id == Guid.Empty)
            {
                e.Id = Guid.NewGuid();
            }

            if (ids.Contains(e.Id))
            {
                continue;
            }

            ids.Add(e.Id);
            cur.Add(e);
            added++;
        }

        await SaveAllAsync(cur, cancellationToken).ConfigureAwait(false);
        return added;
    }

    public async Task RemoveByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var list = (await LoadAsync(cancellationToken).ConfigureAwait(false)).Where(e => e.Id != id).ToList();
        await SaveAllAsync(list, cancellationToken).ConfigureAwait(false);
    }

    public string ExportEntriesJson(IReadOnlyList<SandboxCompressionBenchmarkEntry> entries)
    {
        var dto = new BenchmarkFileDto { Schema = 1, Entries = entries.OrderByDescending(e => e.UtcTicks).ToList() };
        return JsonSerializer.Serialize(dto, _json);
    }

    private sealed class BenchmarkFileDto
    {
        public int Schema { get; set; }
        public List<SandboxCompressionBenchmarkEntry> Entries { get; set; } = new();
    }
}

public sealed class SandboxCompressionBenchmarkEntry
{
    public Guid Id { get; set; }
    public long UtcTicks { get; set; }
    public string BackupRootDisplay { get; set; } = string.Empty;
    public string ArchivePath { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string TitleLine { get; set; } = string.Empty;
    public string DetailText { get; set; } = string.Empty;
    public double? WallSeconds { get; set; }
    public long? RawBytes { get; set; }
    public long? ArchiveBytes { get; set; }
    public string Engine { get; set; } = string.Empty;
    public string OptionsSummary { get; set; } = string.Empty;

    /// <summary>Key metrics block (shown directly under the title). Older JSON may omit this.</summary>
    public string? PriorityDetailText { get; set; }

    /// <summary>Secondary details. Older JSON may omit this.</summary>
    public string? ExtraDetailText { get; set; }
}
