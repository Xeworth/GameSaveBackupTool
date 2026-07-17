using GSBT.Core.Catalog;
using GSBT.WinUI.Common;

namespace GSBT.WinUI.ViewModels;

public sealed partial class GameRowViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string GameName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Platform { get; set; } = "Unknown";

    [ObservableProperty]
    public partial string SaveStatus { get; set; } = GsbtUiText.SaveStatusNotFound;

    [ObservableProperty]
    public partial string LastBackup { get; set; } = "Not yet backed up";

    /// <summary>Formatted total size of retention backup folders on disk; em dash when none.</summary>
    [ObservableProperty]
    public partial string BackupSizeDisplay { get; set; } = GsbtUiText.EmDash;

    /// <summary>Raw backup bytes for sorting; 0 when <see cref="BackupSizeDisplay"/> is em dash.</summary>
    [ObservableProperty]
    public partial long BackupSizeBytes { get; set; }

    /// <summary>True when last-backup text was cleared because backup folders under the default backup path went missing (integrity reconcile).</summary>
    [ObservableProperty]
    public partial bool LastBackupIntegrityWarning { get; set; }

    /// <summary>True when the AppData checkpoint no longer matches files under the latest retention backup run (yellow Last backup).</summary>
    [ObservableProperty]
    public partial bool LastBackupCheckpointWarning { get; set; }

    [ObservableProperty]
    public partial string? SavePathRaw { get; set; }

    [ObservableProperty]
    public partial string? SavePathResolved { get; set; }

    [ObservableProperty]
    public partial bool SaveInRegistryOnly { get; set; }

    [ObservableProperty]
    public partial string? SaveRegistryHive { get; set; }

    [ObservableProperty]
    public partial string? SaveRegistrySubkey { get; set; }

    /// <summary>True when the row was added with &quot;Add custom game&quot; (not from install scan).</summary>
    public bool IsUserAdded { get; set; }

    /// <summary>Used by filters; matches Python semantics (path on disk or registry-only).</summary>
    public bool HasSaveLocation =>
        GameCatalogFilter.HasSaveLocation(SavePathResolved, SaveInRegistryOnly);
}
