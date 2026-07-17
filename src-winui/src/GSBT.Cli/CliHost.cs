using GSBT.Core.Services;
using GSBT.Cli.Settings;

namespace GSBT.Cli;

public sealed class CliHost
{
    public WinUiSettingsStore Settings { get; }

    public SaveCatalogManager CatalogManager { get; }

    public ScanService ScanService { get; }

    public SaveFolderBackupService FolderBackup { get; } = new();

    public RegistrySaveBackupService RegistryBackup { get; } = new();

    public BackupCompressionService Compression { get; } = new();

    public CliHost()
    {
        Settings = new WinUiSettingsStore();
        CatalogManager = new SaveCatalogManager();
        var bundled = Path.Combine(AppContext.BaseDirectory, "data", "ludusavi-save-manifest.json");
        var provider = new LudusaviManifestProvider(
            bundledManifestPath: File.Exists(bundled) ? bundled : null);
        var registry = new RegistrySaveResolver();
        var detector = new WindowsGameDetector();
        ScanService = new ScanService(detector, CatalogManager, provider, registry);
        EnsureSevenZip();
    }

    public void EnsureSevenZip()
    {
        if (SevenZipNativeLibrary.IsAvailable)
        {
            return;
        }

        var dll = Path.Combine(AppContext.BaseDirectory, "7z.dll");
        SevenZipNativeLibrary.TryInitialize(dll);
    }
}
