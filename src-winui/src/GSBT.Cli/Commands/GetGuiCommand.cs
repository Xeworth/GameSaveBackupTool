using System.Diagnostics;
using System.Text.Json;
using GSBT.Cli.Output;
using GSBT.Core.Services;
using Spectre.Console;

namespace GSBT.Cli.Commands;

public static class GetGuiCommand
{
    private const string RollbackInventoryFile = ".gsbt-rollback-inventory.json";
    private const string Repo = "Xeworth/GameSaveBackupTool";
    private const long MaxInstallerBytes = 1024L * 1024 * 1024;

    private sealed record GuiRelease(string Url, string? Version);

    public static async Task<int> RunAsync(
        CliOutputMode mode,
        string? installerUrl = null,
        bool force = false,
        bool allowCustomHost = false,
        CancellationToken cancellationToken = default)
    {
        if (!mode.Json)
        {
            CliConsoleFormatter.WriteCommandStart("gsbt get gui");
        }

        var installDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? rollbackDir = null;
        var keepRollbackForRecovery = false;
        try
        {
            var guiExe = Path.Combine(installDir, "gsbt-main.exe");
            var installedVersion = TryGetInstalledVersion(guiExe);
            var release = string.IsNullOrWhiteSpace(installerUrl)
                ? await ResolveInstallerAsync(mode, cancellationToken).ConfigureAwait(false)
                : new GuiRelease(installerUrl.Trim(), null);
            ValidateInstallerUrl(release.Url, allowCustomHost);

            if (File.Exists(guiExe)
                && !force
                && release.Version is not null
                && CompareVersions(installedVersion, release.Version) >= 0)
            {
                WriteResult(
                    mode,
                    true,
                    "GUI is already up to date.",
                    installDir,
                    guiExe,
                    installedVersion,
                    release.Version);
                return 0;
            }

            if (File.Exists(guiExe) && !force && release.Version is null)
            {
                WriteResult(
                    mode,
                    true,
                    "GUI is already installed. Use --force with an explicit installer URL.",
                    installDir,
                    guiExe,
                    installedVersion,
                    null);
                return 0;
            }

            var temp = Path.Combine(Path.GetTempPath(), "gsbt_gui_setup_" + Guid.NewGuid().ToString("N") + ".exe");
            try
            {
                await DownloadAsync(release.Url, temp, mode, cancellationToken).ConfigureAwait(false);
                if (File.Exists(guiExe))
                {
                    rollbackDir = CreateRollbackSnapshot(installDir, cancellationToken);
                }

                var exitCode = RunInstaller(temp, installDir, mode, cancellationToken);
                if (exitCode != 0)
                {
                    var restored = TryRestoreRollback(rollbackDir, installDir);
                    keepRollbackForRecovery = !restored;
                    WriteResult(
                        mode,
                        false,
                        restored
                            ? $"Installer exited with code {exitCode}; the previous GUI files were restored."
                            : $"Installer exited with code {exitCode}; automatic rollback failed. Recovery files remain at {rollbackDir}.",
                        installDir,
                        guiExe,
                        TryGetInstalledVersion(guiExe),
                        release.Version);
                    return 1;
                }

                var installed = File.Exists(guiExe);
                var postVersion = TryGetInstalledVersion(guiExe);
                var versionValid = release.Version is null
                    || CompareVersions(postVersion, release.Version) >= 0;
                if (!installed || !versionValid)
                {
                    var restored = TryRestoreRollback(rollbackDir, installDir);
                    keepRollbackForRecovery = !restored;
                    WriteResult(
                        mode,
                        false,
                        (installed
                            ? $"Installer completed, but GUI version {postVersion ?? "unknown"} is older than expected {release.Version}."
                            : "Installer completed, but gsbt-main.exe was not found.")
                        + (restored ? " The previous GUI files were restored." : $" Automatic rollback failed; recovery files remain at {rollbackDir}."),
                        installDir,
                        guiExe,
                        TryGetInstalledVersion(guiExe),
                        release.Version);
                    return 1;
                }

                WriteResult(
                    mode,
                    true,
                    File.Exists(guiExe) && installedVersion is not null ? "GUI updated." : "GUI installed.",
                    installDir,
                    guiExe,
                    postVersion,
                    release.Version);
                return 0;
            }
            finally
            {
                TryDeleteFile(temp);
            }
        }
        catch (OperationCanceledException)
        {
            var restored = TryRestoreRollback(rollbackDir, installDir);
            keepRollbackForRecovery = !restored;
            if (mode.Ai)
            {
                CliAiContract.WriteError("get gui", "GUI install canceled.", 130, "canceled");
            }
            else
            {
                CliConsoleFormatter.WriteWarning("GUI install canceled.");
            }

            return 130;
        }
        catch (Exception ex)
        {
            var restored = TryRestoreRollback(rollbackDir, installDir);
            keepRollbackForRecovery = !restored;
            var message = restored
                ? ex.Message
                : $"{ex.Message} Automatic rollback failed; recovery files remain at {rollbackDir}.";
            if (mode.Ai)
            {
                CliAiContract.WriteError("get gui", message, 2, "get_gui_failed");
            }
            else
            {
                CliConsoleFormatter.WriteError(message);
            }

            return 2;
        }
        finally
        {
            if (!keepRollbackForRecovery)
            {
                TryDeleteDirectory(rollbackDir);
            }
            if (!mode.Json)
            {
                CliConsoleFormatter.WriteCommandEnd();
            }
        }
    }

    private static async Task<GuiRelease> ResolveInstallerAsync(
        CliOutputMode mode,
        CancellationToken cancellationToken)
    {
        var environmentUrl = Environment.GetEnvironmentVariable("GSBT_INSTALLER_URL");
        if (!string.IsNullOrWhiteSpace(environmentUrl))
        {
            return new GuiRelease(environmentUrl, ParseVersionFromAssetName(environmentUrl));
        }

        CliProgressEvents.Write(mode, "get gui", "resolve", "Resolving latest GUI installer.");
        using var http = CreateHttpClient();
        using var response = await http.GetAsync(
                $"https://api.github.com/repos/{Repo}/releases/latest",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!doc.RootElement.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Latest GitHub release has no assets.");
        }

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? string.Empty;
            if (name.StartsWith("GSBT_Setup_", StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                && asset.TryGetProperty("browser_download_url", out var urlElement))
            {
                var url = urlElement.GetString() ?? throw new InvalidOperationException("Installer asset has no download URL.");
                return new GuiRelease(url, ParseVersionFromAssetName(name));
            }
        }

        throw new InvalidOperationException("No GSBT_Setup_*.exe asset found on the latest release.");
    }

    private static async Task DownloadAsync(
        string url,
        string destination,
        CliOutputMode mode,
        CancellationToken cancellationToken)
    {
        CliProgressEvents.Write(mode, "get gui", "download", "Downloading GUI installer.", percent: 0);
        if (!mode.Json)
        {
            AnsiConsole.MarkupLine($"Downloading [cyan]{Markup.Escape(url)}[/]");
        }

        using var http = CreateHttpClient();
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        if (total is > MaxInstallerBytes)
        {
            throw new InvalidDataException("Installer exceeds the 1 GiB safety limit.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            1024 * 128,
            FileOptions.WriteThrough);

        var buffer = new byte[1024 * 128];
        long readTotal = 0;
        var lastPercent = -1;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            readTotal += read;
            if (readTotal > MaxInstallerBytes)
            {
                throw new InvalidDataException("Installer exceeds the 1 GiB safety limit.");
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            if (total is > 0)
            {
                var percent = (int)Math.Clamp(readTotal * 100 / total.Value, 0, 100);
                if (percent != lastPercent && (percent == 100 || percent - lastPercent >= 5))
                {
                    lastPercent = percent;
                    CliProgressEvents.Write(mode, "get gui", "download", "Downloading GUI installer.", percent: percent);
                }
            }
        }

        await target.FlushAsync(cancellationToken).ConfigureAwait(false);
        target.Flush(flushToDisk: true);
        if (readTotal == 0)
        {
            throw new InvalidDataException("Installer download was empty.");
        }

        CliProgressEvents.Write(mode, "get gui", "download", "Download complete.", percent: 100);
    }

    private static int RunInstaller(
        string installerPath,
        string installDir,
        CliOutputMode mode,
        CancellationToken cancellationToken)
    {
        CliProgressEvents.Write(mode, "get gui", "install", "Running GUI installer.");
        if (!mode.Json)
        {
            AnsiConsole.WriteLine("Running installer silently...");
        }

        var start = new ProcessStartInfo(installerPath) { UseShellExecute = false };
        start.ArgumentList.Add("/VERYSILENT");
        start.ArgumentList.Add("/SUPPRESSMSGBOXES");
        start.ArgumentList.Add("/NORESTART");
        start.ArgumentList.Add("/TASKS=addpath");
        start.ArgumentList.Add($"/DIR={installDir}");

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start installer.");
        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Best effort cancellation.
            }
        });
        process.WaitForExit();
        cancellationToken.ThrowIfCancellationRequested();
        CliProgressEvents.Write(mode, "get gui", "install", "Installer finished.", percent: process.ExitCode == 0 ? 100 : null);
        return process.ExitCode;
    }

    private static void ValidateInstallerUrl(string value, bool allowCustomHost)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Installer URL must be an absolute HTTPS URL.");
        }

        var allowed = uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
        if (!allowed && !allowCustomHost)
        {
            throw new InvalidOperationException(
                "Custom installer hosts require --allow-custom-host. Only use it for a source you explicitly trust.");
        }
    }

    private static string? CreateRollbackSnapshot(string installDir, CancellationToken cancellationToken)
    {
        var rollback = Path.Combine(Path.GetTempPath(), "gsbt_update_rollback_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rollback);
        var inventory = new List<string>();
        foreach (var source in EnumerateRollbackFiles(installDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(installDir, source);
            inventory.Add(relative);
            if (relative.Equals("gsbt.exe", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(relative).StartsWith("unins", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var target = Path.Combine(rollback, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: false);
        }

        File.WriteAllText(
            Path.Combine(rollback, RollbackInventoryFile),
            JsonSerializer.Serialize(inventory));

        return rollback;
    }

    private static bool TryRestoreRollback(string? rollback, string installDir)
    {
        try
        {
        if (string.IsNullOrWhiteSpace(rollback) || !Directory.Exists(rollback))
        {
            return true;
        }

        var inventoryPath = Path.Combine(rollback, RollbackInventoryFile);
        var priorFiles = File.Exists(inventoryPath)
            ? JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(inventoryPath))
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        priorFiles = new HashSet<string>(priorFiles, StringComparer.OrdinalIgnoreCase);

        foreach (var current in EnumerateRollbackFiles(installDir).ToList())
        {
            var relative = Path.GetRelativePath(installDir, current);
            if (priorFiles.Contains(relative)
                || relative.Equals("gsbt.exe", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(relative).StartsWith("unins", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            File.Delete(current);
        }

        foreach (var source in EnumerateRollbackFiles(rollback))
        {
            var relative = Path.GetRelativePath(rollback, source);
            if (relative.Equals(RollbackInventoryFile, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var target = Path.Combine(installDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
        }
        return true;
        }
        catch
        {
            // Keep the original updater error; the caller reports manual recovery guidance.
            return false;
        }
    }

    private static IEnumerable<string> EnumerateRollbackFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(root));
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var file in Directory.EnumerateFiles(current))
            {
                yield return file;
            }

            foreach (var directory in Directory.EnumerateDirectories(current))
            {
                var attributes = File.GetAttributes(directory);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException($"Updater rollback cannot traverse linked directory: {directory}");
                }

                pending.Push(directory);
            }
        }
    }

    private static string? TryGetInstalledVersion(string guiExe)
    {
        if (!File.Exists(guiExe))
        {
            return null;
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(guiExe);
            return CleanVersion(info.ProductVersion) ?? CleanVersion(info.FileVersion);
        }
        catch
        {
            return null;
        }
    }

    private static string? ParseVersionFromAssetName(string value)
    {
        var fileName = Path.GetFileName(new Uri(value, UriKind.RelativeOrAbsolute).IsAbsoluteUri
            ? new Uri(value).AbsolutePath
            : value);
        const string prefix = "GSBT_Setup_";
        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return fileName[prefix.Length..^4];
    }

    private static string? CleanVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Split('+', '-', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
    }

    private static int CompareVersions(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left))
        {
            return -1;
        }

        if (string.IsNullOrWhiteSpace(right))
        {
            return 1;
        }

        var l = left.Split('.').Select(ParseVersionPart).ToArray();
        var r = right.Split('.').Select(ParseVersionPart).ToArray();
        for (var i = 0; i < Math.Max(l.Length, r.Length); i++)
        {
            var lv = i < l.Length ? l[i] : 0;
            var rv = i < r.Length ? r[i] : 0;
            var comparison = lv.CompareTo(rv);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    private static int ParseVersionPart(string value) =>
        int.TryParse(new string(value.TakeWhile(char.IsDigit).ToArray()), out var result) ? result : 0;

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("gsbt-cli");
        return http;
    }

    private static void WriteResult(
        CliOutputMode mode,
        bool success,
        string message,
        string installDir,
        string guiExe,
        string? installedVersion,
        string? latestVersion)
    {
        OperationHistoryStore.Record(
            "get-gui",
            success ? "succeeded" : "failed",
            message,
            outputPath: guiExe);
        if (mode.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                schemaVersion = CliAiContract.SchemaVersion,
                command = "get gui",
                success,
                message,
                installDir,
                guiExecutable = guiExe,
                guiInstalled = File.Exists(guiExe),
                installedVersion,
                latestVersion,
            }, CliAiContract.JsonOptions));
            return;
        }

        if (success)
        {
            Console.WriteLine(message);
            AnsiConsole.MarkupLine("Run [bold]gsbt gui[/] to open it.");
        }
        else
        {
            CliConsoleFormatter.WriteError(message);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort temp cleanup.
        }
    }

    private static void TryDeleteDirectory(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best effort temp cleanup.
        }
    }
}
