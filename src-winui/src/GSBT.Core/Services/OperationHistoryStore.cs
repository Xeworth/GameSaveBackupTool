using System.Text.Json;
using GSBT.Core.Common;

namespace GSBT.Core.Services;

public sealed record OperationHistoryEntry(
    string TimestampUtc,
    string Operation,
    string Status,
    string Message,
    string? GameName = null,
    string? OutputPath = null,
    long? Bytes = null,
    int? ItemCount = null);

/// <summary>Small local NDJSON operation history with a redacted diagnostics export.</summary>
public static class OperationHistoryStore
{
    private const long MaxHistoryBytes = 2L * 1024 * 1024;
    private const int MaxHistoryEntries = 1000;

    public static string HistoryPath =>
        Path.Combine(UserDataDir.GetWinUiUserDataDir(), "logs", "operations.ndjson");

    public static void Record(
        string operation,
        string status,
        string message,
        string? gameName = null,
        string? outputPath = null,
        long? bytes = null,
        int? itemCount = null)
    {
        try
        {
            var path = HistoryPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var processLock = CrossProcessLock.Acquire("file:" + Path.GetFullPath(path));
            var entry = new OperationHistoryEntry(
                DateTimeOffset.UtcNow.ToString("O"),
                operation,
                status,
                message,
                gameName,
                outputPath,
                bytes,
                itemCount);
            File.AppendAllText(path, JsonSerializer.Serialize(entry) + Environment.NewLine);
            if (new FileInfo(path).Length > MaxHistoryBytes)
            {
                var retained = File.ReadLines(path).TakeLast(MaxHistoryEntries).ToList();
                AtomicFileWrite.WriteAllText(path, string.Join(Environment.NewLine, retained) + Environment.NewLine);
            }
        }
        catch
        {
            // Diagnostics must never break a backup or restore operation.
        }
    }

    public static IReadOnlyList<OperationHistoryEntry> ReadRecent(int count = 200)
    {
        try
        {
            if (!File.Exists(HistoryPath))
            {
                return [];
            }

            var entries = new List<OperationHistoryEntry>();
            foreach (var line in File.ReadLines(HistoryPath).TakeLast(Math.Clamp(count, 1, MaxHistoryEntries)))
            {
                try
                {
                    var entry = JsonSerializer.Deserialize<OperationHistoryEntry>(line);
                    if (entry is not null)
                    {
                        entries.Add(entry);
                    }
                }
                catch
                {
                    // Skip one malformed historical line.
                }
            }

            return entries;
        }
        catch
        {
            return [];
        }
    }

    public static string ExportRedacted(string destination, ManifestProvenance? manifest = null)
    {
        var output = Path.GetFullPath(destination);
        var entries = ReadRecent(MaxHistoryEntries)
            .Select(entry => entry with
            {
                OutputPath = RedactPath(entry.OutputPath),
                Message = RedactEmbeddedPaths(entry.Message),
            })
            .ToList();
        var payload = new
        {
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            appVersion = AppVersionInfo.DisplayVersion,
            environment = new
            {
                os = Environment.OSVersion.VersionString,
                runtime = Environment.Version.ToString(),
                architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            },
            manifest,
            operations = entries,
        };
        AtomicFileWrite.WriteAllText(output, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        return output;
    }

    private static string? RedactPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        try
        {
            return $"<redacted-path>\\{Path.GetFileName(path)}";
        }
        catch
        {
            return "<redacted-path>";
        }
    }

    private static string RedactEmbeddedPaths(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return System.Text.RegularExpressions.Regex.Replace(
            value,
            @"(?i)(?:[a-z]:\\|\\\\)[^\r\n;,'"" ]+",
            "<redacted-path>");
    }
}
