namespace GSBT.WinUI.ViewModels;

public sealed partial class MainViewModel
{
    public void EnsureManifestLoadedOffline() => _scanService.EnsureManifestLoadedOffline();

    public async Task RefreshManifestAndRescanAsync()
    {
        if (IsScanning)
        {
            return;
        }

        if (App.IsSandboxSimulationChild)
        {
            StatusText = "Manifest download is disabled in the simulation — using the bundled / on-disk manifest only.";
            _sandboxLog.Log("scan", StatusText);
            await Task.CompletedTask;
            return;
        }

        IsBusy = true;
        StatusText = "Downloading latest manifest from GitHub...";
        _sandboxLog.Log("scan", StatusText);
        var status = await _scanService.RefreshManifestOnlineAsync();
        StatusText = status switch
        {
            "updated" => "Manifest updated. Starting scan...",
            "not_modified" => "Manifest already current. Starting scan...",
            "network_error" => "Could not reach GitHub. Using local manifest. Starting scan...",
            _ => "Using local manifest. Starting scan..."
        };
        _sandboxLog.Log("scan", StatusText);
        IsBusy = false;
        await StartScanAsync();
    }

    private async Task RunSandboxSimulationScanAsync()
    {
        IsScanning = true;
        IsBusy = true;
        ScanProgress = 0;
        StatusText = "Loading sandbox simulation games…";
        _sandboxLog.Log("scan", StatusText);
        try
        {
            ClearScanDerivedGameRows();

            var dummyRoot = SimulationSessionContext.SessionDummyDataRoot;
            var bundled = SimulationSessionContext.BundledDummyDataRoot;
            var hasBundled = Directory.Exists(bundled);
            var results = SandboxSimulationChildCatalog.BuildResults(_overrides, dummyRoot, hasBundled ? bundled : null);
            EnqueueUi(() =>
            {
                foreach (var r in results)
                {
                    var d = new Dictionary<string, object?>
                    {
                        ["save_path"] = r.SavePathRaw ?? string.Empty,
                        ["scan_outcome"] = "SAVE_ON_DISK",
                        ["platform"] = r.Platform,
                    };
                    _catalogManager.AddOrUpdate(r.Name, d);
                    UpsertFromResult(r);
                }
            });

            await Task.Delay(350);
            EnqueueUi(() =>
            {
                IsScanning = false;
                IsBusy = false;
                MarkSavedGameListEstablished();
                MergeUserAddedCatalogRowsIntoGames();
                StatusText = $"Scan complete. {Games.Count} simulated game(s).";
                ReconcileLastBackupDiskIntegrity();
                RefreshLastBackupDisplays();
                ReapplyFilterFull();
                if (Games.Count > 0 && !IsTeachingTipMarkedShown("backup_bulk_teaching_tip_shown"))
                {
                    TeachingTipBackupBulkRequested?.Invoke(this, EventArgs.Empty);
                }
            });
            _autoBackup.RestartMonitoringIfNeeded();
        }
        catch (Exception ex)
        {
            StatusText = $"Simulation scan failed: {ex.Message}";
            _sandboxLog.Log("warn", StatusText);
            IsScanning = false;
            IsBusy = false;
        }
    }

    public async Task StartScanAsync()
    {
        if (IsScanning)
        {
            return;
        }

        EnsureManifestLoadedOffline();

        if (App.IsSandboxSimulationChild)
        {
            await RunSandboxSimulationScanAsync();
            return;
        }

        IsScanning = true;
        IsBusy = true;
        ScanProgress = 0;
        ClearScanDerivedGameRows();
        StatusText = "Scanning for installed games...";
        _sandboxLog.Log("scan", StatusText);

        var steamIds = new Dictionary<string, string> { ["steamid64"] = string.Empty, ["steamid3"] = string.Empty };
        var detected = await _scanService.DetectGamesAsync();
        _sandboxLog.Log("scan", $"Detection finished: {detected.Count} candidate game(s).");

        if (detected.Count == 0)
        {
            IsScanning = false;
            IsBusy = false;
            MarkSavedGameListEstablished();
            EnqueueUi(() =>
            {
                MergeUserAddedCatalogRowsIntoGames();
                StatusText = Games.Count > 0
                    ? "No installs detected this scan. Custom games in your list are unchanged."
                    : "No games found.";
                ReconcileLastBackupDiskIntegrity();
                RefreshLastBackupDisplays();
                ReapplyFilterFull();
            });
            _sandboxLog.Log("warn", "No installs detected this scan.");
            _autoBackup.RestartMonitoringIfNeeded();
            return;
        }

        var toScan = CatalogAwareDetectionFilter.FilterForRescan(detected, _catalogManager.Catalog, skipWhenPreviouslyNotFound: true);
        var skippedCount = detected.Count - toScan.Count;
        if (skippedCount > 0)
        {
            _sandboxLog.Log("scan", $"Skipping save lookup for {skippedCount} title(s): already remembered with no usable save path.");
        }

        if (toScan.Count == 0)
        {
            IsScanning = false;
            IsBusy = false;
            MarkSavedGameListEstablished();
            EnqueueUi(() =>
            {
                FinalizeScanIntoGameList(detected, toScan);
                StatusText = Games.Count > 0
                    ? "Nothing new to look up — skipped titles are unchanged. Custom games remain listed."
                    : "No titles left to scan. Games previously remembered with no usable save folder are skipped — remove those rows from the list or edit game_save_data.json if you need another attempt.";
                ReconcileLastBackupDiskIntegrity();
                RefreshLastBackupDisplays();
            });
            _sandboxLog.Log("warn", "Nothing new to look up for installed titles.");
            _autoBackup.RestartMonitoringIfNeeded();
            return;
        }

        var total = toScan.Count;
        var done = 0;
        var dedupeSharedSaves = !_settings.Get("show_duplicate_save_titles", false);
        if (!dedupeSharedSaves)
        {
            _sandboxLog.Log("scan", "Same-save-folder titles will be listed separately (Settings).");
        }

        await _scanService.RunSaveFetchParallelAsync(
            toScan,
            steamIds,
            onEach: result =>
            {
                EnqueueUi(() => { UpsertFromResult(result); });

                _sandboxLog.Log("scan", $"{result.Name} → {(string.IsNullOrWhiteSpace(result.SavePathResolved) && !result.SaveInRegistryOnly ? "no path" : "ok")}");
            },
            trace: null,
            onProgressTick: () => EnqueueUi(() =>
            {
                done++;
                ScanProgress = total == 0 ? 0 : (double)done / total * 100.0;
                StatusText = $"Fetching save paths... ({done}/{total})";
            }),
            onDroppedFromDedup: dropped => EnqueueUi(() => RemoveScanRowsByName(dropped)),
            deduplicateSharedSaveFolders: dedupeSharedSaves);

        // Parallel work completes before UI-thread enqueues finish; give the dispatcher time to apply rows.
        await Task.Delay(350);

        EnqueueUi(() =>
        {
            IsScanning = false;
            IsBusy = false;
            MarkSavedGameListEstablished();
            FinalizeScanIntoGameList(detected, toScan);
            StatusText = $"Scan complete. {Games.Count} game(s) in catalog.";
            ReconcileLastBackupDiskIntegrity();
            RefreshLastBackupDisplays();
            if (Games.Count > 0 && !IsTeachingTipMarkedShown("backup_bulk_teaching_tip_shown"))
            {
                TeachingTipBackupBulkRequested?.Invoke(this, EventArgs.Empty);
            }
        });

        _autoBackup.RestartMonitoringIfNeeded();
    }
}
