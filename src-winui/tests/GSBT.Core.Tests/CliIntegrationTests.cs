using System.Text.Json;

namespace GSBT.Core.Tests;

[CollectionDefinition("CLI integration", DisableParallelization = true)]
public sealed class CliIntegrationCollection;

[Collection("CLI integration")]
public sealed class CliIntegrationTests
{
    [Fact]
    public async Task Status_ai_is_isolated_and_contains_version()
    {
        using var environment = new CliTestEnvironment();
        var (exitCode, stdout, _) = await environment.RunAsync(["status", "--ai"]);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(stdout);
        Assert.Equal("status", json.RootElement.GetProperty("command").GetString());
        Assert.Equal(GSBT.Core.Common.AppVersionInfo.DisplayVersion, json.RootElement.GetProperty("version").GetString());
    }

    [Fact]
    public async Task Settings_ai_writes_only_to_test_user_data()
    {
        using var environment = new CliTestEnvironment();
        var backup = Path.Combine(environment.Root, "backups");
        var (exitCode, stdout, _) = await environment.RunAsync(
            ["settings", "backup-path", backup, "--ai"]);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(stdout);
        Assert.Equal(Path.GetFullPath(backup), json.RootElement.GetProperty("values").GetProperty("defaultBackupPath").GetString());
        Assert.True(File.Exists(Path.Combine(
            environment.UserDataRoot,
            GSBT.Core.Common.UserDataDir.AppFolderName,
            GSBT.Core.Common.UserDataDir.WinUiSubdir,
            "winui_settings.json")));
    }

    [Fact]
    public async Task Settings_ai_suggests_the_hyphenated_backup_path_command()
    {
        using var environment = new CliTestEnvironment();
        var (exitCode, stdout, _) = await environment.RunAsync(["settings", "backuppath", "--ai"]);

        Assert.Equal(1, exitCode);
        using var json = JsonDocument.Parse(stdout);
        var message = json.RootElement.GetProperty("error").GetProperty("message").GetString();
        Assert.Contains("gsbt settings backup-path", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Help_ai_advertises_only_the_available_gui_flow_and_never_update()
    {
        using var environment = new CliTestEnvironment();
        var (exitCode, stdout, _) = await environment.RunAsync(["help", "--ai"]);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(stdout);
        var names = json.RootElement
            .GetProperty("commands")
            .EnumerateArray()
            .Select(command => command.GetProperty("name").GetString())
            .ToArray();

        Assert.DoesNotContain("update", names);
        Assert.Equal(GSBT.Cli.CliInstallationState.IsGuiInstalled, names.Contains("gui"));
        Assert.Equal(!GSBT.Cli.CliInstallationState.IsGuiInstalled, names.Contains("get gui"));
        Assert.Contains("add custom", names);

        var notebook = json.RootElement.GetProperty("agentNotebook");
        Assert.Equal("ai-agent", notebook.GetProperty("audience").GetString());
        Assert.Equal("machine-facing", notebook.GetProperty("visibility").GetString());
        var behaviors = notebook
            .GetProperty("topics")
            .GetProperty("compressionProgress")
            .GetProperty("knownBehaviors")
            .EnumerateArray()
            .Select(behavior => behavior.GetProperty("id").GetString())
            .ToArray();
        Assert.Contains(GSBT.Cli.Output.CliAgentNotebook.ChunkyPlateauKnowledgeId, behaviors);
        Assert.Contains(GSBT.Cli.Output.CliAgentNotebook.FinalizationKnowledgeId, behaviors);
        Assert.Equal(
            GSBT.Cli.Output.CliAgentNotebook.CustomFolderKnowledgeId,
            notebook.GetProperty("topics").GetProperty("productScope").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Add_custom_ai_registers_any_verified_folder_and_returns_one_json_result()
    {
        using var environment = new CliTestEnvironment();
        var folder = Path.Combine(environment.Root, "custom maps");
        Directory.CreateDirectory(folder);

        var (exitCode, stdout, stderr) = await environment.RunAsync(
            ["add", "custom", "Warcraft III Custom Maps", folder, "--ai"]);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, stderr);
        using var json = JsonDocument.Parse(stdout);
        Assert.Equal("add custom", json.RootElement.GetProperty("command").GetString());
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            "Warcraft III Custom Maps",
            json.RootElement.GetProperty("entry").GetProperty("name").GetString());
        Assert.Equal(
            Path.GetFullPath(folder),
            json.RootElement.GetProperty("entry").GetProperty("folder").GetString());
    }

    [Fact]
    public void Gui_install_detection_uses_the_main_executable_identity()
    {
        var root = Path.Combine(Path.GetTempPath(), "gsbt_install_state_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.False(GSBT.Cli.CliInstallationState.IsGuiInstalledAt(root));
            File.WriteAllText(Path.Combine(root, GSBT.Cli.CliInstallationState.GuiExecutableName), string.Empty);
            Assert.True(GSBT.Cli.CliInstallationState.IsGuiInstalledAt(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class CliTestEnvironment : IDisposable
    {
        private readonly string? _oldUserData = Environment.GetEnvironmentVariable("GSBT_USER_DATA_ROOT");
        private readonly string? _oldLocalData = Environment.GetEnvironmentVariable("GSBT_LOCAL_DATA_ROOT");

        public CliTestEnvironment()
        {
            Root = Path.Combine(Path.GetTempPath(), "gsbt_cli_test_" + Guid.NewGuid().ToString("N"));
            UserDataRoot = Path.Combine(Root, "roaming");
            Directory.CreateDirectory(UserDataRoot);
            Environment.SetEnvironmentVariable("GSBT_USER_DATA_ROOT", UserDataRoot);
            Environment.SetEnvironmentVariable("GSBT_LOCAL_DATA_ROOT", Path.Combine(Root, "local"));
        }

        public string Root { get; }

        public string UserDataRoot { get; }

        public async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(string[] args)
        {
            var originalOut = Console.Out;
            var originalError = Console.Error;
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            Console.SetOut(stdout);
            Console.SetError(stderr);
            try
            {
                var exitCode = await GSBT.Cli.Program.Main(args);
                return (exitCode, stdout.ToString().Trim(), stderr.ToString().Trim());
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("GSBT_USER_DATA_ROOT", _oldUserData);
            Environment.SetEnvironmentVariable("GSBT_LOCAL_DATA_ROOT", _oldLocalData);
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch
            {
                // Best effort test cleanup.
            }
        }
    }
}
