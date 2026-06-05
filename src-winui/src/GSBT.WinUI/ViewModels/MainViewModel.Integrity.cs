namespace GSBT.WinUI.ViewModels;

public sealed partial class MainViewModel
{
    private void SimulationBackupIntegrityPoll_Tick(DispatcherQueueTimer sender, object args)
    {
        try
        {
            ReconcileLastBackupDiskIntegrity();
        }
        catch
        {
            // non-fatal
        }
    }
    private void OnAutoBackupSucceeded(string gameName)
    {
        var row = Games.FirstOrDefault(g => string.Equals(g.GameName, gameName, StringComparison.OrdinalIgnoreCase));
        if (row is null)
        {
            return;
        }

        row.LastBackupIntegrityWarning = false;
        row.LastBackupCheckpointWarning = false;
        row.LastBackup = FormatLastBackup(DateTime.UtcNow.ToString("O"));
    }

    private string FormatLastBackup(string? iso) =>
        BackupDateFormatter.FormatDisplay(iso, _settings.Get("date_format", "iso"));

    /// <summary>
    /// Ensures catalog/UI last-backup times match folders under <see cref="SettingsStore"/> default backup path only.
    /// </summary>
    public void ReconcileLastBackupDiskIntegrity()
    {
        BackupRunManifestStore.PruneOrphanManifestFiles();

        var defaultPath = (_settings.Get("default_backup_path", string.Empty) ?? string.Empty).Trim();
        var subfolder = _settings.Get("backup_subfolder_per_game", true);

        if (string.IsNullOrWhiteSpace(defaultPath))
        {
            foreach (var g in Games)
            {
                g.LastBackupCheckpointWarning = false;
            }

            BackupIntegrityStripVisible = false;
            _backupIntegrityCoordinator.RefreshWatcherPath();
            return;
        }

        if (!Directory.Exists(defaultPath))
        {
            foreach (var g in Games)
            {
                g.LastBackupCheckpointWarning = false;
            }

            var warnedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _catalogManager.Catalog)
            {
                var lb = CatalogUserAdded.CoerceString(kv.Value.GetValueOrDefault("last_backup"));
                if (!string.IsNullOrWhiteSpace(lb))
                {
                    warnedNames.Add(kv.Key);
                }
            }

            if (warnedNames.Count > 0)
            {
                _catalogManager.ClearAllLastBackupFields();
                foreach (var g in Games)
                {
                    g.LastBackupIntegrityWarning = warnedNames.Contains(g.GameName);
                }

                RefreshLastBackupDisplays();
                ShowBackupIntegrityStrip(
                    "Your default backup folder is missing or unreachable.\n"
                    + "Last-backup dates were cleared until you back up again.");
                TryNotifyAutoBackupToast(
                    "Your default backup folder is missing or unreachable.",
                    AutoBackupToastChrome.DismissAndClearHighlights,
                    "Last-backup dates were cleared from the catalog until you back up again.",
                    BackupToastSeverity.Error);
            }

            _backupIntegrityCoordinator.RefreshWatcherPath();
            return;
        }

        foreach (var kv in _catalogManager.Catalog.ToList())
        {
            var lb = CatalogUserAdded.CoerceString(kv.Value.GetValueOrDefault("last_backup"));
            if (!string.IsNullOrWhiteSpace(lb))
            {
                continue;
            }

            var iso = BackupRetentionVerifier.TryInferLatestLastBackupIso(defaultPath, kv.Key, subfolder);
            if (string.IsNullOrWhiteSpace(iso))
            {
                continue;
            }

            _catalogManager.UpdateLastBackup(kv.Key, iso);
            var row = Games.FirstOrDefault(g => string.Equals(g.GameName, kv.Key, StringComparison.OrdinalIgnoreCase));
            if (row is not null)
            {
                ApplyLastBackupDisplayFromCatalog(row);
            }
        }

        var staleNames = new List<string>();
        foreach (var kv in _catalogManager.Catalog)
        {
            var lb = CatalogUserAdded.CoerceString(kv.Value.GetValueOrDefault("last_backup"));
            if (string.IsNullOrWhiteSpace(lb))
            {
                continue;
            }

            if (!BackupRetentionVerifier.HasRetentionArtifact(defaultPath, kv.Key, subfolder))
            {
                staleNames.Add(kv.Key);
            }
        }

        if (staleNames.Count > 0)
        {
            _catalogManager.ClearLastBackupFieldsForGames(staleNames);
            RefreshLastBackupDisplays();
            foreach (var name in staleNames)
            {
                var row = Games.FirstOrDefault(g => string.Equals(g.GameName, name, StringComparison.OrdinalIgnoreCase));
                if (row is not null)
                {
                    row.LastBackupIntegrityWarning = true;
                }
            }

            TryNotifyAutoBackupToast(
                "Some game backup folders are no longer under your default backup location.",
                AutoBackupToastChrome.DismissAndClearHighlights,
                "Last-backup dates were cleared from the catalog.",
                BackupToastSeverity.Error);
        }

        var suppressCheckpointDriftToast = staleNames.Count > 0;
        ApplyBackupCheckpointDriftForAllRows(defaultPath, subfolder, suppressCheckpointDriftToast);

        _backupIntegrityCoordinator.RefreshWatcherPath();
    }

    private void ApplyBackupCheckpointDriftForAllRows(string defaultPath, bool subfolderPerGame, bool suppressCheckpointDriftToast)
    {
        var anyNewDrift = false;
        foreach (var g in Games)
        {
            var hadDrift = g.LastBackupCheckpointWarning;

            if (string.Equals(g.LastBackup, "Not yet backed up", StringComparison.Ordinal))
            {
                g.LastBackupCheckpointWarning = false;
                continue;
            }

            if (!BackupRetentionVerifier.HasRetentionArtifact(defaultPath, g.GameName, subfolderPerGame))
            {
                g.LastBackupCheckpointWarning = false;
                continue;
            }

            var latest = BackupRetentionVerifier.TryGetLatestRetentionRunDirectory(defaultPath, g.GameName, subfolderPerGame);
            if (string.IsNullOrWhiteSpace(latest))
            {
                g.LastBackupCheckpointWarning = false;
                continue;
            }

            var drift = BackupRunManifestStore.HasManifestDrift(latest);
            g.LastBackupCheckpointWarning = drift;
            if (drift && !hadDrift)
            {
                anyNewDrift = true;
            }
        }

        if (!suppressCheckpointDriftToast && anyNewDrift)
        {
            TryNotifyAutoBackupToast(
                "A backup folder still exists, but some files no longer match the last backup snapshot.",
                AutoBackupToastChrome.DismissAndClearHighlights,
                "Last backup times may be wrong until you back up again. You can clear highlights after you have checked the folder.",
                BackupToastSeverity.Warning);
        }
    }

    public void DismissBackupIntegrityStrip()
    {
        BackupIntegrityStripVisible = false;
        BackupIntegrityStripMessage = string.Empty;
    }

    /// <summary>
    /// Sandbox monitor ÔåÆ simulated child IPC: applies the same yellow Last backup emphasis and in-app toast
    /// as real checkpoint drift detection (no catalog mutation).
    /// </summary>
    public void SandboxPreviewYellowLastBackupWarning()
    {
        GameRowViewModel? target = null;
        foreach (var g in Games)
        {
            if (!string.Equals(g.LastBackup, "Not yet backed up", StringComparison.Ordinal))
            {
                target = g;
                break;
            }
        }

        if (target is null)
        {
            TryNotifyAutoBackupToast(
                "Run at least one backup in the simulated window first, then try this preview again.");
            return;
        }

        foreach (var g in Games)
        {
            g.LastBackupCheckpointWarning = false;
        }

        target.LastBackupIntegrityWarning = false;
        target.LastBackupCheckpointWarning = true;

        TryNotifyAutoBackupToast(
            "A backup folder still exists, but some files no longer match the last backup snapshot.",
            AutoBackupToastChrome.DismissAndClearHighlights,
            "Last backup times may be wrong until you back up again. You can clear highlights after you have checked the folder.",
            BackupToastSeverity.Warning);
    }

    /// <summary>
    /// Sandbox monitor ÔåÆ simulated child IPC: applies the same red Last backup emphasis, catalog clear for one row,
    /// and error toast as when retention backups disappear under the default path.
    /// </summary>
    public void SandboxPreviewRedLastBackupWarning()
    {
        GameRowViewModel? target = null;
        foreach (var g in Games)
        {
            if (!_catalogManager.TryGetCatalogEntryInsensitive(g.GameName, out _, out var catRow))
            {
                continue;
            }

            var raw = CatalogUserAdded.CoerceString(catRow.GetValueOrDefault("last_backup"));
            if (!string.IsNullOrWhiteSpace(raw))
            {
                target = g;
                break;
            }
        }

        if (target is null)
        {
            TryNotifyAutoBackupToast(
                "Run at least one backup in the simulated window first, then try this preview again.");
            return;
        }

        var name = target.GameName;
        _catalogManager.ClearLastBackupFieldsForGames([name]);
        RefreshLastBackupDisplays();

        foreach (var g in Games)
        {
            g.LastBackupCheckpointWarning = false;
            g.LastBackupIntegrityWarning = false;
        }

        target.LastBackupIntegrityWarning = true;

        TryNotifyAutoBackupToast(
            "Some game backup folders are no longer under your default backup location.",
            AutoBackupToastChrome.DismissAndClearHighlights,
            "Last-backup dates were cleared from the catalog.",
            BackupToastSeverity.Error);

        _autoBackup.RestartMonitoringIfNeeded();
    }

    private void ShowBackupIntegrityStrip(string message)
    {
        BackupIntegrityStripMessage = message;
        BackupIntegrityStripVisible = true;
    }
    private void RefreshLastBackupDisplays()
    {
        foreach (var g in Games)
        {
            if (!_catalogManager.TryGetCatalogEntryInsensitive(g.GameName, out _, out var row))
            {
                continue;
            }

            var raw = CatalogUserAdded.CoerceString(row.GetValueOrDefault("last_backup"));
            if (string.IsNullOrWhiteSpace(raw))
            {
                g.LastBackup = "Not yet backed up";
            }
            else
            {
                g.LastBackup = FormatLastBackup(raw);
                g.LastBackupIntegrityWarning = false;
            }
        }
    }
}
