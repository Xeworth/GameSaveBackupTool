using System.Diagnostics;
using System.Text.Json;
using GSBT.Cli.Output;
using GSBT.Cli.Services;

namespace GSBT.Cli.Commands;

public static class GetGuiCommand
{
    public static async Task<int> RunAsync(CliOutputMode mode, bool force, CancellationToken cancellationToken)
    {
        var installDir = AppContext.BaseDirectory;
        var guiPath = Path.Combine(installDir, "gsbt-main.exe");

        if (File.Exists(guiPath) && !force)
        {
            if (mode.Json)
            {
                WriteJson(
                    mode.Ai,
                    success: true,
                    alreadyInstalled: true,
                    guiPath,
                    message: "GUI already installed.",
                    releaseTag: null,
                    installerUrl: null,
                    exitCode: 0);
            }
            else
            {
                CliConsoleFormatter.WriteCommandStart("gsbt get gui");
                AnsiConsoleMessage($"GUI already installed: {guiPath}");
                AnsiConsoleMessage("Use gsbt gui to open it, or gsbt get gui --force to reinstall.");
                CliConsoleFormatter.WriteCommandEnd();
            }

            return 0;
        }

        if (!mode.Ai)
        {
            CliConsoleFormatter.WriteCommandStart("gsbt get gui");
        }

        string tagName;
        string installerUrl;
        try
        {
            (tagName, installerUrl) = await GitHubReleaseAssets
                .ResolveGuiInstallerAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Fail(mode, guiPath, ex.Message, releaseTag: null, installerUrl: null);
        }

        if (!mode.Ai)
        {
            Console.Error.WriteLine($"Downloading GUI installer ({tagName})...");
            Console.Error.WriteLine(installerUrl);
        }

        var tempInstaller = Path.Combine(
            Path.GetTempPath(),
            $"gsbt-setup-{Guid.NewGuid():N}.exe");

        try
        {
            await DownloadFileAsync(installerUrl, tempInstaller, cancellationToken).ConfigureAwait(false);

            if (!mode.Ai)
            {
                Console.Error.WriteLine("Running silent installer (per-user, no admin)...");
            }

            var exitCode = RunSilentInstaller(tempInstaller);
            if (exitCode != 0)
            {
                return Fail(
                    mode,
                    guiPath,
                    $"Installer exited with code {exitCode}.",
                    tagName,
                    installerUrl,
                    exitCode);
            }

            if (!File.Exists(guiPath))
            {
                return Fail(
                    mode,
                    guiPath,
                    "Installer finished but gsbt-main.exe was not found beside gsbt.exe. " +
                    "Reinstall to the default folder or run the GUI installer manually.",
                    tagName,
                    installerUrl,
                    exitCode: 1);
            }

            if (mode.Json)
            {
                WriteJson(
                    mode.Ai,
                    success: true,
                    alreadyInstalled: false,
                    guiPath,
                    message: "GUI installed successfully.",
                    releaseTag: tagName,
                    installerUrl,
                    exitCode: 0);
            }
            else
            {
                AnsiConsoleMessage($"GUI installed: {guiPath}");
                AnsiConsoleMessage("Try: gsbt gui");
                CliConsoleFormatter.WriteCommandEnd();
            }

            return 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Fail(mode, guiPath, ex.Message, tagName, installerUrl);
        }
        finally
        {
            TryDelete(tempInstaller);
        }
    }

    private static async Task DownloadFileAsync(string url, string destination, CancellationToken cancellationToken)
    {
        using var response = await GitHubReleaseAssets.HttpGetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = File.Create(destination);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }

    private static int RunSilentInstaller(string installerPath)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        if (process is null)
        {
            throw new InvalidOperationException("Could not start the GUI installer process.");
        }

        process.WaitForExit();
        return process.ExitCode;
    }

    private static int Fail(
        CliOutputMode mode,
        string guiPath,
        string message,
        string? releaseTag,
        string? installerUrl,
        int exitCode = 1)
    {
        if (mode.Json)
        {
            WriteJson(
                mode.Ai,
                success: false,
                alreadyInstalled: File.Exists(guiPath),
                guiPath,
                message,
                releaseTag,
                installerUrl,
                exitCode);
        }
        else
        {
            CliConsoleFormatter.WriteError(message);
            CliConsoleFormatter.WriteCommandEnd();
        }

        return exitCode;
    }

    private static void WriteJson(
        bool ai,
        bool success,
        bool alreadyInstalled,
        string guiPath,
        string message,
        string? releaseTag,
        string? installerUrl,
        int exitCode)
    {
        object payload = ai
            ? new
            {
                schemaVersion = CliAiContract.SchemaVersion,
                command = "get gui",
                success,
                exitCode,
                alreadyInstalled,
                guiInstalled = File.Exists(guiPath),
                guiExecutable = guiPath,
                releaseTag,
                installerUrl,
                message,
                nextActions = success && File.Exists(guiPath)
                    ? new[] { "Run gsbt gui to open the desktop app." }
                    : new[]
                    {
                        "Set GSBT_INSTALLER_URL to a direct gsbt-setup-*.exe URL and retry.",
                        $"Or run: irm {GitHubReleaseAssets.GuiInstallScriptUrl} | iex",
                    },
            }
            : new
            {
                success,
                alreadyInstalled,
                guiInstalled = File.Exists(guiPath),
                releaseTag,
                installerUrl,
                message,
            };

        Console.WriteLine(JsonSerializer.Serialize(payload, CliAiContract.JsonOptions));
    }

    private static void AnsiConsoleMessage(string message) =>
        Spectre.Console.AnsiConsole.WriteLine(message);

    private static void TryDelete(string path)
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
            // best-effort temp cleanup
        }
    }
}
