using GSBT.Core.Catalog;
using GSBT.WinUI.Common;

namespace GSBT.WinUI.ViewModels;

public sealed partial class GameRowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _gameName = string.Empty;

    [ObservableProperty]
    private string _platform = "Unknown";

    [ObservableProperty]
    private string _saveStatus = GsbtUiText.SaveStatusNotFound;

    [ObservableProperty]
    private string _lastBackup = "Not yet backed up";

    /// <summary>Formatted total size of retention backup folders on disk; em dash when none.</summary>
    [ObservableProperty]
    private string _backupSizeDisplay = GsbtUiText.EmDash;

    /// <summary>True when last-backup text was cleared because backup folders under the default backup path went missing (integrity reconcile).</summary>
    [ObservableProperty]
    private bool _lastBackupIntegrityWarning;

    /// <summary>True when the AppData checkpoint no longer matches files under the latest retention backup run (yellow Last backup).</summary>
    [ObservableProperty]
    private bool _lastBackupCheckpointWarning;

    [ObservableProperty]
    private string? _savePathRaw;

    [ObservableProperty]
    private string? _savePathResolved;

    [ObservableProperty]
    private bool _saveInRegistryOnly;

    [ObservableProperty]
    private string? _saveRegistryHive;

    [ObservableProperty]
    private string? _saveRegistrySubkey;

    /// <summary>True when the row was added with &quot;Add custom game&quot; (not from install scan).</summary>
    public bool IsUserAdded { get; set; }

    /// <summary>Used by filters; matches Python semantics (path on disk or registry-only).</summary>
    public bool HasSaveLocation =>
        GameCatalogFilter.HasSaveLocation(SavePathResolved, SaveInRegistryOnly);
}
