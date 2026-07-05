using System.Text.Json;
using GSBT.Cli.Output;
using GSBT.Core.Services;

namespace GSBT.Cli.Commands;

public static class ScanCommand
{
    public static async Task<int> RunAsync(CliHost host, bool refreshManifest, CliOutputMode mode)
    {
        if (!mode.Ai)
        {
            CliConsoleFormatter.WriteCommandStart("gsbt scan");
        }

        try
        {
            if (refreshManifest)
            {
                if (!mode.Ai)
                {
                    Console.Error.WriteLine("Refreshing Ludusavi manifest from GitHub…");
                }

                var status = await host.ScanService.RefreshManifestOnlineAsync().ConfigureAwait(false);
                if (!mode.Ai)
                {
                    Console.Error.WriteLine(status switch
                    {
                        "updated" => "Manifest updated.",
                        "not_modified" => "Manifest already current.",
                        "network_error" => "Could not reach GitHub — using local manifest.",
                        _ => "Using local manifest.",
                    });
                }
            }
            else
            {
                host.ScanService.EnsureManifestLoadedOffline();
            }

            if (!mode.Ai)
            {
                Console.Error.WriteLine("Detecting installed games…");
            }

            var detected = await host.ScanService.DetectGamesAsync().ConfigureAwait(false);
            if (!mode.Ai)
            {
                Console.Error.WriteLine($"Found {detected.Count} installed title(s).");
            }

            if (detected.Count == 0)
            {
                if (mode.Ai)
                {
                    WriteAiSummary(host, 0, 0, refreshManifest);
                }
                else
                {
                    Console.WriteLine("No games detected. Custom catalog entries are unchanged.");
                    Console.WriteLine("Run gsbt list to see your catalog.");
                }

                return 0;
            }

            var toScan = CatalogAwareDetectionFilter.FilterForRescan(
                detected,
                host.CatalogManager.Catalog,
                skipWhenPreviouslyNotFound: true);

            if (toScan.Count == 0)
            {
                if (mode.Ai)
                {
                    WriteAiSummary(host, detected.Count, 0, refreshManifest);
                }
                else
                {
                    Console.WriteLine("Nothing new to look up — skipped titles are unchanged.");
                    Console.WriteLine("Run gsbt list to see numbered games.");
                }

                return 0;
            }

            var total = toScan.Count;
            var done = 0;
            var dedupe = !host.Settings.Get("show_duplicate_save_titles", false);
            var steamIds = new Dictionary<string, string> { ["steamid64"] = string.Empty, ["steamid3"] = string.Empty };

            await host.ScanService.RunSaveFetchParallelAsync(
                toScan,
                steamIds,
                onEach: _ => { },
                trace: null,
                onProgressTick: () =>
                {
                    done++;
                    if (!mode.Ai && done <= total)
                    {
                        Console.Error.Write($"\rFetching save paths… ({done}/{total})");
                    }
                },
                deduplicateSharedSaveFolders: dedupe).ConfigureAwait(false);

            if (!mode.Ai)
            {
                Console.Error.WriteLine();
                Console.WriteLine($"Scan complete. {host.CatalogManager.Catalog.Count} game(s) in catalog.");
                Console.WriteLine("Run gsbt list to see numbered games.");
            }
            else
            {
                WriteAiSummary(host, detected.Count, toScan.Count, refreshManifest);
            }

            return 0;
        }
        catch (Exception ex)
        {
            var message = $"Scan failed: {ex.Message}";
            if (mode.Ai)
            {
                CliAiContract.WriteError("scan", message, 2, "scan_failed");
            }
            else
            {
                CliConsoleFormatter.WriteError(message);
            }

            return 2;
        }
        finally
        {
            if (!mode.Ai)
            {
                CliConsoleFormatter.WriteCommandEnd();
            }
        }
    }

    private static void WriteAiSummary(CliHost host, int detected, int scanned, bool refreshManifest)
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schemaVersion = CliAiContract.SchemaVersion,
            command = "scan",
            success = true,
            refreshManifest,
            detectedCount = detected,
            scannedCount = scanned,
            catalogCount = host.CatalogManager.Catalog.Count,
            nextActions = new[]
            {
                "Run gsbt list --ai to inspect indexed games.",
                "Run gsbt backup --ai to back up all eligible games.",
            },
        }, CliAiContract.JsonOptions));
    }
}
