using System.Text.Json;
using GSBT.Cli.Output;
using GSBT.Cli.Settings;
using GSBT.Core.Common;
using GSBT.Core.Services;

namespace GSBT.Core.Tests;

[Collection("CLI integration")]
public sealed class HardeningRegressionTests
{
    [Fact]
    public void Root_operation_lease_blocks_a_second_writer()
    {
        using var temp = new TestDirectory();
        var leasePath = Path.Combine(temp.Root, ".gsbt-operation.lock");
        using var first = OperationFileLease.Acquire(
            leasePath,
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        Assert.Throws<TimeoutException>(() => OperationFileLease.Acquire(
            leasePath,
            TimeSpan.FromMilliseconds(150),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Free_space_preflight_saturates_without_overflow()
    {
        using var temp = new TestDirectory();

        var enough = BackupPathSafety.HasSufficientFreeSpace(temp.Root, long.MaxValue, out var error);

        Assert.False(enough);
        Assert.Contains("space", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Backup_path_comparison_is_case_insensitive_on_windows()
    {
        using var temp = new TestDirectory();
        Assert.True(BackupPathSafety.PathsEqual(temp.Root, temp.Root.ToUpperInvariant()));
    }

    [Fact]
    public void Manifest_provenance_reports_valid_bundled_source()
    {
        using var temp = new TestDirectory();
        var bundled = Path.Combine(temp.Root, "bundled.json");
        File.WriteAllText(bundled, """
            {
              "version": 1,
              "generated_at_unix": 1710000000,
              "source_url": "https://example.invalid/manifest",
              "name_index": { "game": ["%APPDATA%\\Game\\Saves"] },
              "steam_index": {}
            }
            """);
        var provider = new LudusaviManifestProvider(Path.Combine(temp.Root, "data"), bundled);

        var provenance = provider.GetProvenance();

        Assert.True(provenance.IsValid);
        Assert.Equal("bundled", provenance.Source);
        Assert.Equal("1", provenance.Version);
        Assert.NotNull(provenance.GeneratedAtUtc);
    }

    [Theory]
    [InlineData("%APPDATA%")]
    [InlineData("%WINDIR%\\System32")]
    [InlineData("%USERPROFILE%")]
    [InlineData("%INSTALLATION_PATH%")]
    [InlineData("%APPDATA%\\..\\Windows")]
    [InlineData("C:\\")]
    public void Manifest_safety_rejects_overly_broad_or_traversing_paths(string path)
    {
        Assert.False(LudusaviManifestProvider.IsSafeManifestPathTemplate(path));
    }

    [Theory]
    [InlineData("%APPDATA%\\Game\\Saves")]
    [InlineData("%USERPROFILE%\\Documents\\My Games\\Title")]
    [InlineData("%INSTALLATION_PATH%\\save")]
    public void Manifest_safety_accepts_scoped_game_paths(string path)
    {
        Assert.True(LudusaviManifestProvider.IsSafeManifestPathTemplate(path));
    }

    [Fact]
    public void Catalog_instances_reload_before_write_and_preserve_other_process_changes()
    {
        using var temp = new TestDirectory();
        var path = Path.Combine(temp.Root, "catalog.json");
        var first = new SaveCatalogManager(path);
        var second = new SaveCatalogManager(path);

        first.AddOrUpdate("Game A", new Dictionary<string, object?> { ["save_path"] = "A" });
        second.AddOrUpdate("Game B", new Dictionary<string, object?> { ["save_path"] = "B" });
        first.RefreshFromDisk();

        Assert.Contains("Game A", first.Catalog.Keys);
        Assert.Contains("Game B", first.Catalog.Keys);
        Assert.True(File.Exists(path + ".meta.json"));
        Assert.True(File.Exists(path + ".bak"));
    }

    [Fact]
    public void Settings_instances_reload_before_write_and_preserve_other_process_changes()
    {
        using var temp = new TestDirectory();
        var first = new WinUiSettingsStore(temp.Root);
        var second = new WinUiSettingsStore(temp.Root);

        first.Set("first", 1);
        second.Set("second", 2);

        Assert.Equal(1, second.Get("first", 0));
        Assert.Equal(2, first.Get("second", 0));
    }

    [Fact]
    public void Diagnostics_export_redacts_recorded_paths()
    {
        using var temp = new TestDirectory();
        var oldRoot = Environment.GetEnvironmentVariable("GSBT_USER_DATA_ROOT");
        Environment.SetEnvironmentVariable("GSBT_USER_DATA_ROOT", temp.Root);
        try
        {
            var privatePath = Path.Combine(temp.Root, "private", "save.dat");
            OperationHistoryStore.Record("backup", "failed", $"Could not read {privatePath}", outputPath: privatePath);
            var output = OperationHistoryStore.ExportRedacted(Path.Combine(temp.Root, "diagnostics.json"));
            var json = File.ReadAllText(output);

            Assert.DoesNotContain(temp.Root, json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("redacted-path", json);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GSBT_USER_DATA_ROOT", oldRoot);
        }
    }

    [Fact]
    public void Ai_progress_event_is_one_compact_json_object_on_stderr()
    {
        var original = Console.Error;
        using var writer = new StringWriter();
        Console.SetError(writer);
        try
        {
            CliProgressEvents.Write(
                CliOutputMode.From(json: false, ai: true),
                "backup",
                "game",
                "Backing up.",
                current: 1,
                total: 2,
                percent: 50,
                heartbeat: true,
                agentStatus: "working",
                knowledgeRef: CliAgentNotebook.ChunkyPlateauKnowledgeId,
                agentHint: "Known behavior.");
        }
        finally
        {
            Console.SetError(original);
        }

        var line = writer.ToString().Trim();
        Assert.DoesNotContain(Environment.NewLine, line);
        using var json = JsonDocument.Parse(line);
        Assert.Equal("backup", json.RootElement.GetProperty("command").GetString());
        Assert.Equal(50, json.RootElement.GetProperty("percent").GetInt32());
        Assert.Equal("working", json.RootElement.GetProperty("agentStatus").GetString());
        Assert.Equal(
            CliAgentNotebook.ChunkyPlateauKnowledgeId,
            json.RootElement.GetProperty("knowledgeRef").GetString());
        Assert.Equal("Known behavior.", json.RootElement.GetProperty("agentHint").GetString());
    }

    private sealed class TestDirectory : IDisposable
    {
        public TestDirectory()
        {
            Root = Path.Combine(Path.GetTempPath(), "gsbt_hardening_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
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
