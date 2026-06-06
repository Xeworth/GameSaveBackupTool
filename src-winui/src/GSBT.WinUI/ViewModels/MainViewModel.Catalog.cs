namespace GSBT.WinUI.ViewModels;

public sealed partial class MainViewModel
{
    public void CycleFilter()
    {
        FilterMode = FilterMode switch
        {
            GameCatalogFilterMode.All => GameCatalogFilterMode.FoundOnly,
            GameCatalogFilterMode.FoundOnly => GameCatalogFilterMode.NotFoundOnly,
            GameCatalogFilterMode.NotFoundOnly => GameCatalogFilterMode.All,
            _ => GameCatalogFilterMode.All
        };

        UpdateFilterLabel();
        ReapplyFilterFull();
    }

    private void UpdateFilterLabel()
    {
        FilterButtonText = FilterMode switch
        {
            GameCatalogFilterMode.All => "Filter: All",
            GameCatalogFilterMode.FoundOnly => "Filter: Found",
            GameCatalogFilterMode.NotFoundOnly => "Filter: Not found",
            _ => "Filter"
        };
    }

    private bool MatchesFilter(GameRowViewModel g) =>
        GameCatalogFilter.IncludeRow(FilterMode, g.HasSaveLocation);

    private string? _displaySortColumnId;
    private bool _displaySortAscending = true;

    public string? DisplaySortColumnId => _displaySortColumnId;

    public bool DisplaySortAscending => _displaySortAscending;

    /// <summary>Header click: sort visible rows by column (toggle direction on repeat).</summary>
    /// <returns>True when row order changed (caller may play a sort animation).</returns>
    public bool SortDisplayedGamesByColumn(string columnId)
    {
        if (string.IsNullOrWhiteSpace(columnId))
        {
            return false;
        }

        if (string.Equals(_displaySortColumnId, columnId, StringComparison.OrdinalIgnoreCase))
        {
            _displaySortAscending = !_displaySortAscending;
        }
        else
        {
            _displaySortColumnId = columnId;
            _displaySortAscending = true;
        }

        OnPropertyChanged(nameof(DisplaySortColumnId));
        OnPropertyChanged(nameof(DisplaySortAscending));

        DisplayedGamesRebuildStarting?.Invoke(this, EventArgs.Empty);
        try
        {
            return ResortDisplayedGamesInPlace();
        }
        finally
        {
            DisplayedListRebuilt?.Invoke(this, EventArgs.Empty);
        }
    }

    private IComparable SortKeyForColumn(GameTableColumn def, GameRowViewModel row) =>
        def.GetSortKey?.Invoke(row) ?? def.GetText(row);

    private IEnumerable<GameRowViewModel> OrderForDisplay(IEnumerable<GameRowViewModel> source)
    {
        if (string.IsNullOrWhiteSpace(_displaySortColumnId))
        {
            return source;
        }

        var def = GameTableColumns.Definitions.FirstOrDefault(d =>
            string.Equals(d.Id, _displaySortColumnId, StringComparison.OrdinalIgnoreCase));
        if (def is null)
        {
            return source;
        }

        return _displaySortAscending
            ? source
                .OrderBy(r => SortKeyForColumn(def, r), Comparer<IComparable>.Default)
                .ThenBy(r => r.GameName, StringComparer.OrdinalIgnoreCase)
            : source
                .OrderByDescending(r => SortKeyForColumn(def, r), Comparer<IComparable>.Default)
                .ThenBy(r => r.GameName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reorder visible rows without clearing the list. Caller must raise
    /// <see cref="DisplayedGamesRebuildStarting"/> / <see cref="DisplayedListRebuilt"/>
    /// so transient empty ListView selection during moves does not wipe logical selection.
    /// </summary>
    private bool ResortDisplayedGamesInPlace()
    {
        if (DisplayedGames.Count < 2 || string.IsNullOrWhiteSpace(_displaySortColumnId))
        {
            return false;
        }

        var sorted = OrderForDisplay(DisplayedGames).ToList();
        if (IsSameRowOrder(DisplayedGames, sorted))
        {
            return false;
        }

        for (var target = 0; target < sorted.Count; target++)
        {
            var item = sorted[target];
            var current = DisplayedGames.IndexOf(item);
            if (current >= 0 && current != target)
            {
                DisplayedGames.RemoveAt(current);
                DisplayedGames.Insert(target, item);
            }
        }

        return true;
    }

    private static bool IsSameRowOrder(IReadOnlyList<GameRowViewModel> current, IReadOnlyList<GameRowViewModel> sorted)
    {
        if (current.Count != sorted.Count)
        {
            return false;
        }

        for (var i = 0; i < current.Count; i++)
        {
            if (!ReferenceEquals(current[i], sorted[i]))
            {
                return false;
            }
        }

        return true;
    }

    private void ReapplyFilterFull()
    {
        DisplayedGamesRebuildStarting?.Invoke(this, EventArgs.Empty);
        DisplayedGames.Clear();
        foreach (var g in OrderForDisplay(Games.Where(MatchesFilter)))
        {
            DisplayedGames.Add(g);
        }

        DisplayedListRebuilt?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Keeps selection for rows that are still in the catalog but temporarily hidden by the filter.</summary>
    public void SyncLogicalSelectionFromVisibleGrid(IEnumerable selectedItems)
    {
        var visibleSelected = new HashSet<GameRowViewModel>();
        var n = 0;
        foreach (var o in selectedItems)
        {
            n++;
            if (o is GameRowViewModel vm)
            {
                visibleSelected.Add(vm);
            }
        }

        if (n == 0)
        {
            _logicalSelection.Clear();
            return;
        }

        var visibleSet = new HashSet<GameRowViewModel>(DisplayedGames);

        foreach (var vm in visibleSet)
        {
            if (!visibleSelected.Contains(vm))
            {
                _logicalSelection.Remove(vm);
            }
        }

        foreach (var vm in visibleSelected)
        {
            _logicalSelection.Add(vm);
        }
    }

    /// <summary>Whether this row stays selected when filtered out of the list.</summary>
    public bool IsLogicallySelected(GameRowViewModel row) => _logicalSelection.Contains(row);

    /// <summary>Replace logical selection (used by Ctrl+A on the filtered view).</summary>
    public void AlignLogicalSelectionTo(IEnumerable<GameRowViewModel> rows)
    {
        _logicalSelection.Clear();
        foreach (var r in rows)
        {
            _logicalSelection.Add(r);
        }
    }

    /// <summary>Current explicitly selected rows; empty means no rows picked (backup treats empty as ÔÇ£all with savesÔÇØ).</summary>
    public List<GameRowViewModel> SnapshotLogicalSelection() => [.._logicalSelection];

    /// <summary>Find a grid row by catalog/game name (e.g. estimate dialog removal).</summary>
    public GameRowViewModel? FindGameRow(string gameName)
    {
        if (string.IsNullOrWhiteSpace(gameName))
        {
            return null;
        }

        return Games.FirstOrDefault(g => string.Equals(g.GameName, gameName, StringComparison.OrdinalIgnoreCase));
    }

    private void SyncRowInDisplayed(GameRowViewModel row)
    {
        if (_displaySortColumnId is not null)
        {
            var show = MatchesFilter(row);
            var idx = DisplayedGames.IndexOf(row);
            if ((show && idx < 0) || (!show && idx >= 0))
            {
                ReapplyFilterFull();
            }

            return;
        }

        var visible = MatchesFilter(row);
        var at = DisplayedGames.IndexOf(row);
        if (visible && at < 0)
        {
            DisplayedGames.Add(row);
        }
        else if (!visible && at >= 0)
        {
            DisplayedGames.RemoveAt(at);
        }
    }

    public void RemoveRows(IReadOnlyList<GameRowViewModel> selected)
    {
        if (selected.Count == 0)
        {
            return;
        }

        _undoDelete.Push([.. selected]);
        foreach (var item in selected)
        {
            UntrustLargeSavePath(item.GameName);
            _logicalSelection.Remove(item);
            Games.Remove(item);
            DisplayedGames.Remove(item);
            _catalogManager.DeleteGames([item.GameName]);
        }

        StatusText = $"Deleted {selected.Count} game(s). Press Ctrl+Z to undo.";
    }

    public void UndoDelete()
    {
        if (_undoDelete.Count == 0)
        {
            return;
        }

        var rows = _undoDelete.Pop();
        foreach (var row in rows)
        {
            if (!Games.Any(g => string.Equals(g.GameName, row.GameName, StringComparison.OrdinalIgnoreCase)))
            {
                Games.Add(row);
            _catalogManager.AddOrUpdate(row.GameName, BuildCatalogRestorePayload(row));
                SyncRowInDisplayed(row);
            }
        }

        _catalogManager.Flush();
        StatusText = $"Restored {rows.Count} game(s).";
    }

    public async Task<(bool Ok, string Message)> TryAddCustomGameAsync(string rawName, string saveFolderRaw)
    {
        await Task.CompletedTask;
        var name = (rawName ?? string.Empty).Trim();
        if (!GameNameInputValidation.IsValidGameNameForStorage(name, out var nameErr))
        {
            return (false, nameErr ?? "Enter a game name.");
        }

        var folderInput = (saveFolderRaw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(folderInput))
        {
            return (false, "Choose a save folder.");
        }

        string resolved;
        try
        {
            resolved = Path.GetFullPath(_catalogManager.ResolvePath(folderInput, null) ?? folderInput);
        }
        catch
        {
            return (false, "That folder path is not valid.");
        }

        if (!Directory.Exists(resolved))
        {
            return (false, "That folder does not exist or is not reachable.");
        }

        var displayName = GameDisplayName.CleanDisplayName(name);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return (false, "Enter a printable game name.");
        }

        if (!GameNameInputValidation.IsValidGameNameForStorage(displayName, out var displayErr))
        {
            return (false, displayErr ?? "Invalid game name.");
        }

        var payload = new Dictionary<string, object?>
        {
            ["scan_outcome"] = "SAVE_ON_DISK",
            ["save_path"] = folderInput,
            [CatalogUserAdded.JsonPropertyName] = true
        };

        _catalogManager.AddOrUpdate(displayName, payload);
        _catalogManager.Flush();

        var row = new GameRowViewModel
        {
            GameName = displayName,
            Platform = "Custom",
            SaveStatus = GsbtUiText.SaveStatusFound,
            LastBackup = "Not yet backed up",
            SavePathRaw = folderInput,
            SavePathResolved = resolved,
            SaveInRegistryOnly = false,
            SaveRegistryHive = null,
            SaveRegistrySubkey = null,
            IsUserAdded = true
        };

        var existing = Games.FirstOrDefault(g => string.Equals(g.GameName, displayName, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            Games.Add(row);
            SyncRowInDisplayed(row);
        }
        else
        {
            existing.Platform = row.Platform;
            existing.SaveStatus = row.SaveStatus;
            existing.SavePathRaw = row.SavePathRaw;
            existing.SavePathResolved = row.SavePathResolved;
            existing.SaveInRegistryOnly = false;
            existing.SaveRegistryHive = null;
            existing.SaveRegistrySubkey = null;
            existing.IsUserAdded = true;
            SyncRowInDisplayed(existing);
        }

        StatusText = $"Added custom game: {displayName}";
        _sandboxLog.Log("info", $"Custom game catalogued: {displayName} ÔåÆ {resolved}");
        MarkSavedGameListEstablished();
        _autoBackup.RestartMonitoringIfNeeded();
        if (Games.Count > 0 && !IsTeachingTipMarkedShown("backup_bulk_teaching_tip_shown"))
        {
            TeachingTipBackupBulkRequested?.Invoke(this, EventArgs.Empty);
        }

        return (true, $"Added “{displayName}”.");
    }

    public async Task<(bool Ok, string Message)> TryAddCustomGameWithRegistryAsync(string rawName, string registryRaw)
    {
        await Task.CompletedTask;
        var name = (rawName ?? string.Empty).Trim();
        if (!GameNameInputValidation.IsValidGameNameForStorage(name, out var nameErr))
        {
            return (false, nameErr ?? "Enter a game name.");
        }

        var text = (registryRaw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return (false, "Enter a registry path or paste from Regedit.");
        }

        if (RegistrySaveResolver.LooksLikeFilesystemPath(text))
        {
            return (false, "That looks like a file or folder path, not a registry key. Copy the path from Regedit’s address bar (e.g. HKCU\\Software\\…).");
        }

        var displayName = GameDisplayName.CleanDisplayName(name);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return (false, "Enter a printable game name.");
        }

        if (!GameNameInputValidation.IsValidGameNameForStorage(displayName, out var displayErr))
        {
            return (false, displayErr ?? "Invalid game name.");
        }

        if (Games.Any(g => string.Equals(g.GameName, displayName, StringComparison.OrdinalIgnoreCase)))
        {
            return (false, $"A game named “{displayName}” is already in the list.");
        }

        var hints = RegistrySaveResolver.ExtractRegistryHints(text).ToList();
        if (hints.Count == 0 && RegistrySaveResolver.LooksLikeRegistryHiveLine(text))
        {
            hints.Add(RegistrySaveResolver.NormalizeRegistryPastedPath(text));
        }

        if (hints.Count == 0)
        {
            return (false, "Could not parse a registry path. Example: HKCU\\Software\\…");
        }

        RegistrySaveResolver.RegistrySaveValidation? lastFailure = null;
        foreach (var hint in hints)
        {
            var validation = _registryResolver.ValidateRegistrySaveHint(hint);
            if (!validation.IsSuccess)
            {
                lastFailure = validation;
                continue;
            }

            if (validation.Kind == RegistrySaveResolver.RegistrySaveValidationKind.ValidFolder
                && !string.IsNullOrWhiteSpace(validation.FolderRaw)
                && !string.IsNullOrWhiteSpace(validation.FolderResolved))
            {
                var folder = validation.FolderRaw;
                var resolved = Path.GetFullPath(_catalogManager.ResolvePath(folder, null) ?? validation.FolderResolved);
                var payload = new Dictionary<string, object?>
                {
                    ["scan_outcome"] = "SAVE_ON_DISK",
                    ["save_path"] = folder,
                    [CatalogUserAdded.JsonPropertyName] = true
                };
                return FinishAddCustomGame(displayName, payload, folder, resolved, regOnly: false, null, null);
            }

            if (validation.Kind == RegistrySaveResolver.RegistrySaveValidationKind.ValidInKey
                && !string.IsNullOrWhiteSpace(validation.Hive)
                && !string.IsNullOrWhiteSpace(validation.Subkey))
            {
                var display = RegistrySaveResolver.FormatRegistrySaveDisplay(validation.Hive, validation.Subkey);
                var payload = new Dictionary<string, object?>
                {
                    ["save_in_registry_only"] = true,
                    ["save_registry_hive"] = validation.Hive,
                    ["save_registry_subkey"] = validation.Subkey,
                    ["save_path"] = display,
                    ["scan_outcome"] = "REGISTRY_IN_KEY",
                    [CatalogUserAdded.JsonPropertyName] = true
                };
                return FinishAddCustomGame(displayName, payload, display, null, regOnly: true, validation.Hive, validation.Subkey);
            }
        }

        if (lastFailure is { } fail && !string.IsNullOrWhiteSpace(fail.Message))
        {
            return (false, fail.Message);
        }

        return (false, "Could not resolve that registry path to a folder or valid in-key save.");
    }

    private (bool Ok, string Message) FinishAddCustomGame(
        string displayName,
        Dictionary<string, object?> payload,
        string savePathRaw,
        string? savePathResolved,
        bool regOnly,
        string? hive,
        string? subkey)
    {
        _catalogManager.AddOrUpdate(displayName, payload);
        _catalogManager.Flush();

        var row = new GameRowViewModel
        {
            GameName = displayName,
            Platform = "Custom",
            SaveStatus = GsbtUiText.SaveStatusFound,
            LastBackup = "Not yet backed up",
            SavePathRaw = savePathRaw,
            SavePathResolved = savePathResolved,
            SaveInRegistryOnly = regOnly,
            SaveRegistryHive = hive,
            SaveRegistrySubkey = subkey,
            IsUserAdded = true
        };

        Games.Add(row);
        SyncRowInDisplayed(row);
        StatusText = $"Added custom game: {displayName}";
        _sandboxLog.Log("info", regOnly
            ? $"Custom game (registry) catalogued: {displayName} → {savePathRaw}"
            : $"Custom game catalogued: {displayName} → {savePathResolved}");
        MarkSavedGameListEstablished();
        _autoBackup.RestartMonitoringIfNeeded();
        if (Games.Count > 0 && !IsTeachingTipMarkedShown("backup_bulk_teaching_tip_shown"))
        {
            TeachingTipBackupBulkRequested?.Invoke(this, EventArgs.Empty);
        }

        return (true, $"Added “{displayName}”.");
    }

    /// <summary>Rename a custom-added catalog entry (updates saved JSON key).</summary>
    public async Task<(bool Ok, string Message)> TryRenameUserAddedGameAsync(GameRowViewModel row, string rawNewName)
    {
        await Task.CompletedTask;
        if (!row.IsUserAdded)
        {
            return (false, "Only games you added with ÔÇ£Add custom gameÔÇØ can be renamed here.");
        }

        var name = (rawNewName ?? string.Empty).Trim();
        if (!GameNameInputValidation.IsValidGameNameForStorage(name, out var nameErr))
        {
            return (false, nameErr ?? "Enter a game name.");
        }

        var displayName = GameDisplayName.CleanDisplayName(name);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return (false, "Enter a printable game name.");
        }

        if (!GameNameInputValidation.IsValidGameNameForStorage(displayName, out var displayErr))
        {
            return (false, displayErr ?? "Invalid game name.");
        }

        if (string.Equals(row.GameName, displayName, StringComparison.OrdinalIgnoreCase))
        {
            return (true, "Name unchanged.");
        }

        if (Games.Any(g => !ReferenceEquals(g, row) && string.Equals(g.GameName, displayName, StringComparison.OrdinalIgnoreCase)))
        {
            return (false, "Another game in your list already uses that name.");
        }

        if (!_catalogManager.Catalog.TryGetValue(row.GameName, out var payload))
        {
            return (false, "Catalog entry missing.");
        }

        var copy = new Dictionary<string, object?>(payload);
        _catalogManager.DeleteGames([row.GameName]);
        _catalogManager.AddOrUpdate(displayName, copy);
        _catalogManager.Flush();

        Games.Remove(row);
        row.GameName = displayName;
        Games.Insert(GetSortedInsertIndexForGameName(displayName), row);

        ReapplyFilterFull();
        StatusText = $"Renamed to ÔÇ£{displayName}ÔÇØ.";
        _sandboxLog.Log("info", $"Renamed custom game to {displayName}");
        return (true, $"Renamed to ÔÇ£{displayName}ÔÇØ.");
    }

    /// <summary>Set a disk save folder for a row that has no usable save location (e.g. scan showed Not found).</summary>
    public async Task<(bool Ok, string Message)> TryAssignSaveFolderForRowAsync(GameRowViewModel row, string folderRaw)
    {
        await Task.CompletedTask;
        if (row.HasSaveLocation)
        {
            return (false, "This entry already has a save location.");
        }

        var folderInput = (folderRaw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(folderInput))
        {
            return (false, "Choose a folder.");
        }

        string resolved;
        try
        {
            resolved = Path.GetFullPath(_catalogManager.ResolvePath(folderInput, null) ?? folderInput);
        }
        catch
        {
            return (false, "That folder path is not valid.");
        }

        if (!Directory.Exists(resolved))
        {
            return (false, "That folder does not exist or is not reachable.");
        }

        if (!_catalogManager.TryGetCatalogEntryInsensitive(row.GameName, out var catalogKey, out var cat))
        {
            return (false, "Catalog entry missing.");
        }

        var copy = new Dictionary<string, object?>(cat);
        copy["save_path"] = folderInput;
        copy["scan_outcome"] = "SAVE_ON_DISK";
        copy.Remove("save_in_registry_only");
        copy.Remove("save_registry_hive");
        copy.Remove("save_registry_subkey");
        _catalogManager.AddOrUpdate(catalogKey, copy);
        _catalogManager.Flush();

        row.SavePathRaw = folderInput;
        row.SavePathResolved = resolved;
        row.SaveInRegistryOnly = false;
        row.SaveRegistryHive = null;
        row.SaveRegistrySubkey = null;
        row.SaveStatus = GsbtUiText.SaveStatusFound;

        ReapplyFilterFull();
        StatusText = $"Save folder set for ÔÇ£{row.GameName}ÔÇØ.";
        _sandboxLog.Log("info", $"Manual save path for {row.GameName}: {resolved}");
        _autoBackup.RestartMonitoringIfNeeded();
        return (true, "Save folder updated.");
    }

    /// <summary>Assign a registry path for a row with no save location (resolves to a folder or in-key save entry).</summary>
    public async Task<(bool Ok, string Message)> TryAssignRegistrySaveForRowAsync(GameRowViewModel row, string rawInput)
    {
        await Task.CompletedTask;
        if (row.HasSaveLocation)
        {
            return (false, "This entry already has a save location.");
        }

        var text = (rawInput ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return (false, "Enter a registry path or paste from Regedit.");
        }

        if (RegistrySaveResolver.LooksLikeFilesystemPath(text))
        {
            return (false, "That looks like a file or folder path, not a registry key. Copy the path from RegeditÔÇÖs address bar (e.g. HKCU\\Software\\ÔÇª).");
        }

        if (!_catalogManager.TryGetCatalogEntryInsensitive(row.GameName, out var catalogKey, out var cat))
        {
            return (false, "Catalog entry missing.");
        }

        var hints = RegistrySaveResolver.ExtractRegistryHints(text).ToList();
        if (hints.Count == 0 && RegistrySaveResolver.LooksLikeRegistryHiveLine(text))
        {
            hints.Add(RegistrySaveResolver.NormalizeRegistryPastedPath(text));
        }

        if (hints.Count == 0)
        {
            return (false, "Could not parse a registry path. Example: HKCU\\Software\\ÔÇª");
        }

        RegistrySaveResolver.RegistrySaveValidation? lastFailure = null;
        foreach (var hint in hints)
        {
            var validation = _registryResolver.ValidateRegistrySaveHint(hint);
            if (!validation.IsSuccess)
            {
                lastFailure = validation;
                continue;
            }

            if (validation.Kind == RegistrySaveResolver.RegistrySaveValidationKind.ValidFolder
                && !string.IsNullOrWhiteSpace(validation.FolderRaw)
                && !string.IsNullOrWhiteSpace(validation.FolderResolved))
            {
                var folder = validation.FolderRaw;
                var resolved = Path.GetFullPath(_catalogManager.ResolvePath(folder, null) ?? validation.FolderResolved);
                var copy = new Dictionary<string, object?>(cat);
                copy["save_path"] = folder;
                copy["scan_outcome"] = "SAVE_ON_DISK";
                copy.Remove("save_in_registry_only");
                copy.Remove("save_registry_hive");
                copy.Remove("save_registry_subkey");
                _catalogManager.AddOrUpdate(catalogKey, copy);
                _catalogManager.Flush();

                row.SavePathRaw = folder;
                row.SavePathResolved = resolved;
                row.SaveInRegistryOnly = false;
                row.SaveRegistryHive = null;
                row.SaveRegistrySubkey = null;
                row.SaveStatus = GsbtUiText.SaveStatusFound;

                ReapplyFilterFull();
                _sandboxLog.Log("info", $"Manual registryÔåÆfolder for {row.GameName}: {resolved}");
                _autoBackup.RestartMonitoringIfNeeded();
                return (true, "Save folder resolved from registry.");
            }

            if (validation.Kind == RegistrySaveResolver.RegistrySaveValidationKind.ValidInKey
                && !string.IsNullOrWhiteSpace(validation.Hive)
                && !string.IsNullOrWhiteSpace(validation.Subkey))
            {
                var display = RegistrySaveResolver.FormatRegistrySaveDisplay(validation.Hive, validation.Subkey);
                var copy = new Dictionary<string, object?>(cat)
                {
                    ["save_in_registry_only"] = true,
                    ["save_registry_hive"] = validation.Hive,
                    ["save_registry_subkey"] = validation.Subkey,
                    ["save_path"] = display,
                    ["scan_outcome"] = "REGISTRY_IN_KEY"
                };
                _catalogManager.AddOrUpdate(catalogKey, copy);
                _catalogManager.Flush();

                row.SavePathRaw = display;
                row.SavePathResolved = null;
                row.SaveInRegistryOnly = true;
                row.SaveRegistryHive = validation.Hive;
                row.SaveRegistrySubkey = validation.Subkey;
                row.SaveStatus = GsbtUiText.SaveStatusFound;

                ReapplyFilterFull();
                _sandboxLog.Log("info", $"Manual in-key registry save for {row.GameName}: {display}");
                _autoBackup.RestartMonitoringIfNeeded();
                return (true, "Registry save location saved.");
            }
        }

        if (lastFailure is { } fail && !string.IsNullOrWhiteSpace(fail.Message))
        {
            return (false, fail.Message);
        }

        return (false, "Could not resolve that registry path to a folder or valid in-key save.");
    }

    private int GetSortedInsertIndexForGameName(string name)
    {
        var c = StringComparer.OrdinalIgnoreCase;
        for (var i = 0; i < Games.Count; i++)
        {
            if (c.Compare(Games[i].GameName, name) > 0)
            {
                return i;
            }
        }

        return Games.Count;
    }
    private void MarkSavedGameListEstablished() => _settings.Set(SavedGameListEstablishedKey, true);

    public bool HasSavedGameListEstablished() => _settings.Get(SavedGameListEstablishedKey, false);

    /// <summary>Return visit: hydrate the grid from the persisted catalog (scan + custom rows).</summary>
    private void RestoreSavedGameListFromCatalog()
    {
        foreach (var kv in _catalogManager.Catalog)
        {
            if (!CatalogUserAdded.IsUserAddedEntry(kv.Value))
            {
                continue;
            }

            if (Games.Any(g => string.Equals(g.GameName, kv.Key, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (TryCreateRowFromCatalog(kv.Key, kv.Value) is { } customRow)
            {
                Games.Add(customRow);
                SyncRowInDisplayed(customRow);
            }
        }

        var scanResults = new List<SaveScanResult>();
        foreach (var kv in _catalogManager.Catalog)
        {
            if (CatalogUserAdded.IsUserAddedEntry(kv.Value))
            {
                continue;
            }

            if (TryBuildScanResultFromCatalog(kv.Key, kv.Value) is { } scanRow)
            {
                scanResults.Add(scanRow);
            }
        }

        var toRestore = scanResults;
        if (!_settings.Get("show_duplicate_save_titles", false) && scanResults.Count > 1)
        {
            var (kept, dropped) = GameScanPostProcessor.DeduplicateBySharedSaveRoot(scanResults);
            if (dropped.Count > 0)
            {
                _catalogManager.DeleteGames(dropped);
                _catalogManager.Flush();
            }

            toRestore = kept.ToList();
        }

        foreach (var result in toRestore)
        {
            if (Games.Any(g => string.Equals(g.GameName, result.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (!_catalogManager.TryGetCatalogEntryInsensitive(result.Name, out var catalogKey, out var row))
            {
                continue;
            }

            if (TryCreateRowFromCatalog(catalogKey, row) is { } vm)
            {
                Games.Add(vm);
                SyncRowInDisplayed(vm);
            }
        }

        if (Games.Count == 0)
        {
            return;
        }

        StatusText = $"Loaded {Games.Count} game(s). Click 'Scan' to refresh installs and save paths.";
        ReconcileLastBackupDiskIntegrity();
        RefreshLastBackupDisplays();
        ReapplyFilterFull();
    }

    private SaveScanResult? TryBuildScanResultFromCatalog(string gameName, Dictionary<string, object?> row)
    {
        if (string.IsNullOrWhiteSpace(gameName))
        {
            return null;
        }

        var regOnly = CatalogUserAdded.CoerceBool(row.GetValueOrDefault("save_in_registry_only"));
        var rawPath = CatalogUserAdded.CoerceString(row.GetValueOrDefault("save_path"));
        var appId = CatalogUserAdded.CoerceString(row.GetValueOrDefault("steam_app_id"));
        var platform = CatalogUserAdded.CoerceString(row.GetValueOrDefault("platform"))
            ?? (!string.IsNullOrWhiteSpace(appId) ? "Steam" : "Unknown");

        string? resolved = null;
        if (!regOnly && !string.IsNullOrWhiteSpace(rawPath))
        {
            resolved = _catalogManager.ResolvePath(rawPath, null);
        }

        return new SaveScanResult
        {
            RowId = gameName,
            Name = gameName,
            AppId = appId,
            InstallPath = null,
            Platform = platform,
            SavePathRaw = rawPath,
            SavePathResolved = resolved,
            SaveInRegistryOnly = regOnly,
            SaveRegistryHive = CatalogUserAdded.CoerceString(row.GetValueOrDefault("save_registry_hive")),
            SaveRegistrySubkey = CatalogUserAdded.CoerceString(row.GetValueOrDefault("save_registry_subkey")),
            Source = "Cached",
            WallSec = 0,
            ScanOutcome = CatalogUserAdded.CoerceString(row.GetValueOrDefault("scan_outcome")) ?? "NO_MANIFEST_PATHS",
        };
    }

    private void TryRestoreSavedGameListOnStartup()
    {
        if (!HasSavedGameListEstablished())
        {
            if (_catalogManager.Catalog.Count == 0)
            {
                return;
            }

            // Migrated catalog from an older layout — treat as an existing saved list.
            MarkSavedGameListEstablished();
        }

        RestoreSavedGameListFromCatalog();
    }

    private void StartWithEmptyGameListUi()
    {
        DisplayedGamesRebuildStarting?.Invoke(this, EventArgs.Empty);
        Games.Clear();
        DisplayedGames.Clear();
        _logicalSelection.Clear();
        StatusText = "Ready. Click 'Scan for games'.";
    }

    /// <summary>Removes install-scan rows so a new scan rebuilds the list (custom games are kept).</summary>
    private void ClearScanDerivedGameRows()
    {
        var toRemove = Games.Where(g => !g.IsUserAdded).ToList();
        foreach (var row in toRemove)
        {
            _logicalSelection.Remove(row);
            Games.Remove(row);
            DisplayedGames.Remove(row);
        }
    }

    /// <summary>After same-save-folder dedupe, drop merged-away titles from the grid.</summary>
    private void RemoveScanRowsByName(IReadOnlyList<string> names)
    {
        if (names.Count == 0)
        {
            return;
        }

        var drop = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        foreach (var row in Games.Where(g => !g.IsUserAdded && drop.Contains(g.GameName)).ToList())
        {
            _logicalSelection.Remove(row);
            Games.Remove(row);
            DisplayedGames.Remove(row);
        }
    }

    private void MergeUserAddedCatalogRowsIntoGames()
    {
        foreach (var kv in _catalogManager.Catalog)
        {
            if (!CatalogUserAdded.IsUserAddedEntry(kv.Value))
            {
                continue;
            }

            if (Games.Any(g => string.Equals(g.GameName, kv.Key, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (TryCreateRowFromCatalog(kv.Key, kv.Value) is { } row)
            {
                Games.Add(row);
                SyncRowInDisplayed(row);
            }
        }
    }

    /// <summary>
    /// Re-adds installed titles skipped by save lookup (already catalogued with no usable save path).
    /// </summary>
    private void MergeSkippedDetectedCatalogRows(IReadOnlyList<GameRecord> detected, IReadOnlyList<GameRecord> scannedForSaveLookup)
    {
        var scannedKeys = new HashSet<string>(
            scannedForSaveLookup.Select(CatalogGameKeys.FromDetectedGame),
            StringComparer.OrdinalIgnoreCase);

        foreach (var g in detected)
        {
            var key = CatalogGameKeys.FromDetectedGame(g);
            if (scannedKeys.Contains(key))
            {
                continue;
            }

            if (!_catalogManager.TryGetCatalogEntryInsensitive(key, out var catalogKey, out var row))
            {
                continue;
            }

            if (Games.Any(x => string.Equals(x.GameName, catalogKey, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (TryCreateRowFromCatalog(catalogKey, row) is { } vm)
            {
                Games.Add(vm);
                SyncRowInDisplayed(vm);
            }
        }
    }

    private void FinalizeScanIntoGameList(IReadOnlyList<GameRecord> detected, IReadOnlyList<GameRecord> scannedForSaveLookup)
    {
        MergeSkippedDetectedCatalogRows(detected, scannedForSaveLookup);
        MergeUserAddedCatalogRowsIntoGames();
        ReapplyFilterFull();
    }

    private GameRowViewModel? TryCreateRowFromCatalog(string gameName, Dictionary<string, object?> row)
    {
        if (string.IsNullOrWhiteSpace(gameName))
        {
            return null;
        }

        var regOnly = CatalogUserAdded.CoerceBool(row.GetValueOrDefault("save_in_registry_only"));
        var hive = CatalogUserAdded.CoerceString(row.GetValueOrDefault("save_registry_hive"));
        var sub = CatalogUserAdded.CoerceString(row.GetValueOrDefault("save_registry_subkey"));
        var rawPath = CatalogUserAdded.CoerceString(row.GetValueOrDefault("save_path"));
        var isUser = CatalogUserAdded.IsUserAddedEntry(row);
        var steamAppId = CatalogUserAdded.CoerceString(row.GetValueOrDefault("steam_app_id"));
        var platform = CatalogUserAdded.CoerceString(row.GetValueOrDefault("platform"))
            ?? (isUser ? "Custom" : !string.IsNullOrWhiteSpace(steamAppId) ? "Steam" : "Unknown");

        string? resolved = null;
        if (!regOnly && !string.IsNullOrWhiteSpace(rawPath))
        {
            resolved = _catalogManager.ResolvePath(rawPath, null);
        }

        var hasLoc = GameCatalogFilter.HasSaveLocation(resolved, regOnly);
        var status = hasLoc ? GsbtUiText.SaveStatusFound : GsbtUiText.SaveStatusNotFound;
        var lastBkRaw = CatalogUserAdded.CoerceString(row.GetValueOrDefault("last_backup"));
        var lastBkDisplay = string.IsNullOrWhiteSpace(lastBkRaw) ? "Not yet backed up" : FormatLastBackup(lastBkRaw);

        return new GameRowViewModel
        {
            GameName = gameName,
            Platform = platform,
            SaveStatus = status,
            LastBackup = lastBkDisplay,
            SavePathRaw = rawPath,
            SavePathResolved = resolved,
            SaveInRegistryOnly = regOnly,
            SaveRegistryHive = hive,
            SaveRegistrySubkey = sub,
            IsUserAdded = isUser
        };
    }

    private static Dictionary<string, object?> BuildCatalogRestorePayload(GameRowViewModel row)
    {
        var d = new Dictionary<string, object?>
        {
            ["save_path"] = row.SavePathRaw ?? string.Empty,
            ["scan_outcome"] = row.SaveInRegistryOnly
                ? "REGISTRY_IN_KEY"
                : (!string.IsNullOrWhiteSpace(row.SavePathResolved) ? "SAVE_ON_DISK" : "NO_MANIFEST_PATHS"),
        };

        if (row.IsUserAdded)
        {
            d[CatalogUserAdded.JsonPropertyName] = true;
        }

        if (row.SaveInRegistryOnly)
        {
            d["save_in_registry_only"] = true;
            if (!string.IsNullOrWhiteSpace(row.SaveRegistryHive))
            {
                d["save_registry_hive"] = row.SaveRegistryHive;
            }

            if (!string.IsNullOrWhiteSpace(row.SaveRegistrySubkey))
            {
                d["save_registry_subkey"] = row.SaveRegistrySubkey;
            }
        }

        return d;
    }

    private void UpsertFromResult(SaveScanResult result)
    {
        var existing = Games.FirstOrDefault(g => string.Equals(g.GameName, result.Name, StringComparison.OrdinalIgnoreCase));
        var status = (!string.IsNullOrWhiteSpace(result.SavePathResolved) || result.SaveInRegistryOnly)
            ? GsbtUiText.SaveStatusFound
            : GsbtUiText.SaveStatusNotFound;
        if (existing is null)
        {
            var row = new GameRowViewModel
            {
                GameName = result.Name,
                Platform = result.Platform,
                SaveStatus = status,
                LastBackup = "Not yet backed up",
                SavePathRaw = result.SavePathRaw,
                SavePathResolved = result.SavePathResolved,
                SaveInRegistryOnly = result.SaveInRegistryOnly,
                SaveRegistryHive = result.SaveRegistryHive,
                SaveRegistrySubkey = result.SaveRegistrySubkey,
                IsUserAdded = false
            };
            ApplyLastBackupDisplayFromCatalog(row);
            Games.Add(row);
            SyncRowInDisplayed(row);
            return;
        }

        if (existing.IsUserAdded)
        {
            return;
        }

        existing.Platform = result.Platform;
        existing.SaveStatus = status;
        existing.SavePathRaw = result.SavePathRaw;
        existing.SavePathResolved = result.SavePathResolved;
        existing.SaveInRegistryOnly = result.SaveInRegistryOnly;
        existing.SaveRegistryHive = result.SaveRegistryHive;
        existing.SaveRegistrySubkey = result.SaveRegistrySubkey;
        existing.IsUserAdded = false;
        ApplyLastBackupDisplayFromCatalog(existing);
        SyncRowInDisplayed(existing);
    }

    private void ApplyLastBackupDisplayFromCatalog(GameRowViewModel row)
    {
        if (!_catalogManager.TryGetCatalogEntryInsensitive(row.GameName, out _, out var cat))
        {
            return;
        }

        var raw = CatalogUserAdded.CoerceString(cat.GetValueOrDefault("last_backup"));
        if (string.IsNullOrWhiteSpace(raw))
        {
            row.LastBackup = "Not yet backed up";
        }
        else
        {
            row.LastBackup = FormatLastBackup(raw);
            row.LastBackupIntegrityWarning = false;
        }
    }
    /// <summary>Open the on-disk save folder (not registry-only).</summary>
    public (bool Ok, string? Path, string? UserMessage) TryGetOpenSaveFolderPath(GameRowViewModel row)
    {
        if (row.SaveInRegistryOnly)
        {
            return (false, null, "This gameÔÇÖs save is registry-only ÔÇö there is no folder to open.");
        }

        var path = row.SavePathResolved;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return (false, null, "No reachable save folder on disk for this game.");
        }

        return (true, path, null);
    }

    /// <summary>Folder under the configured default backup path for this game (or the shared root).</summary>
    public (bool Ok, string? Path, string? HintMessage) TryGetOpenGameBackupsFolderPath(GameRowViewModel row)
    {
        var defaultPath = (_settings.Get("default_backup_path", string.Empty) ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(defaultPath))
        {
            return (false, null, "Set a default backup folder in Settings first.");
        }

        if (!Directory.Exists(defaultPath))
        {
            return (false, null, "The default backup folder is not available.");
        }

        var subfolder = _settings.Get("backup_subfolder_per_game", true);
        if (subfolder)
        {
            var safe = GameNameInputValidation.SanitizeForWindowsPathSegment(
                string.IsNullOrWhiteSpace(row.GameName) ? "Game" : row.GameName);
            var gameDir = Path.Combine(defaultPath, safe);
            if (!Directory.Exists(gameDir))
            {
                return (false, null, "No backup folder for this game yet. Run a backup first.");
            }

            return (true, gameDir, null);
        }

        return (true, defaultPath, "Backups for all games use this shared folder.");
    }
}
