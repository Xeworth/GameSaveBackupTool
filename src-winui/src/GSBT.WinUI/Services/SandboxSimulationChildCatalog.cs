using GSBT.Core.Models;
using GSBT.Core.Services;

namespace GSBT.WinUI.Services;

/// <summary>Builds deterministic dummy <see cref="SaveScanResult"/> rows for the simulated child process.</summary>
public static class SandboxSimulationChildCatalog
{
    private static readonly string[] BaseGames = ["Game 1", "Game 2", "Game 3", "Game A"];

    public static IReadOnlyList<SaveScanResult> BuildResults(ISandboxRuntimeOverrides sim, string sessionDummyRoot, string? bundledTemplateRoot)
    {
        Directory.CreateDirectory(sessionDummyRoot);
        var list = new List<SaveScanResult>();
        foreach (var name in BaseGames)
        {
            list.Add(CreateRow(name, sessionDummyRoot, bundledTemplateRoot, "Windows"));
        }

        if (sim.IncludeSimulatedLargeGameB)
        {
            list.Add(CreateRow("Game B", sessionDummyRoot, bundledTemplateRoot, "Windows"));
        }

        if (sim.IncludeSimulatedLargeGameC)
        {
            list.Add(CreateRow("Game C", sessionDummyRoot, bundledTemplateRoot, "Windows"));
        }

        return list;
    }

    private static SaveScanResult CreateRow(string name, string sessionDummyRoot, string? bundledTemplateRoot, string platform)
    {
        var safe = string.Join('_', name.Split(Path.GetInvalidFileNameChars()));
        var dest = Path.Combine(sessionDummyRoot, safe);
        Directory.CreateDirectory(dest);
        TrySeedFromTemplate(name, dest, bundledTemplateRoot);
        EnsurePlaceholderFile(dest);

        return new SaveScanResult
        {
            RowId = name,
            Name = name,
            AppId = null,
            InstallPath = null,
            Platform = platform,
            SavePathRaw = dest,
            SavePathResolved = dest,
            SaveLocationDisplay = dest,
            SaveInRegistryOnly = false,
            Source = "SandboxSim",
            WallSec = 0,
            ScanOutcome = "OK",
        };
    }

    private static void TrySeedFromTemplate(string displayName, string destDir, string? bundledTemplateRoot)
    {
        if (string.IsNullOrWhiteSpace(bundledTemplateRoot) || !Directory.Exists(bundledTemplateRoot))
        {
            return;
        }

        var safe = string.Join('_', displayName.Split(Path.GetInvalidFileNameChars()));
        var src = Path.Combine(bundledTemplateRoot, safe);
        if (!Directory.Exists(src))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(src, file);
                var target = Path.Combine(destDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (!File.Exists(target))
                {
                    File.Copy(file, target, overwrite: false);
                }
            }
        }
        catch
        {
            // best-effort
        }
    }

    private static void EnsurePlaceholderFile(string destDir)
    {
        try
        {
            var marker = Path.Combine(destDir, ".gsbt_sim_placeholder.txt");
            if (!File.Exists(marker))
            {
                File.WriteAllText(marker, "GSBT sandbox simulation dummy save root.\r\n");
            }
        }
        catch
        {
            // ignore
        }
    }
}
