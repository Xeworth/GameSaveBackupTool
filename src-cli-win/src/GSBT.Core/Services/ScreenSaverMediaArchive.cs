using SharpSevenZip;

namespace GSBT.Core.Services;

/// <summary>Pack / unpack bundled compression screen saver media (<c>data/screensaver.7z</c>).</summary>
public static class ScreenSaverMediaArchive
{
    public const string ArchiveFileName = "screensaver.7z";

    public static string ResolveBundledArchivePath(string appBaseDirectory) =>
        Path.Combine(appBaseDirectory, "data", ArchiveFileName);

    public static void PackFromAssetsDirectory(string assetsDirectory, string outputArchivePath)
    {
        if (!SevenZipNativeLibrary.IsAvailable)
        {
            throw new InvalidOperationException(SevenZipNativeLibrary.LastError ?? "7z.dll is not loaded.");
        }

        var videoDir = Path.Combine(assetsDirectory, "video");
        var audioDir = Path.Combine(assetsDirectory, "audio");
        if (!Directory.Exists(videoDir) || !Directory.Exists(audioDir))
        {
            throw new DirectoryNotFoundException($"Expected video/ and audio/ under: {assetsDirectory}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputArchivePath)!);

        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sub in new[] { "video", "audio" })
        {
            var dir = Path.Combine(assetsDirectory, sub);
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                entries[$"{sub}/{Path.GetFileName(file)}"] = file;
            }
        }

        if (entries.Count == 0)
        {
            throw new InvalidOperationException($"No screen saver media files found under: {assetsDirectory}");
        }

        var compressor = new SharpSevenZipCompressor
        {
            ArchiveFormat = OutArchiveFormat.SevenZip,
            CompressionLevel = CompressionLevel.None,
        };

        compressor.CompressFileDictionary(entries, outputArchivePath, password: string.Empty);
    }

    public static void ExtractToDirectory(string archivePath, string outputDirectory, bool cleanOutput)
    {
        if (!SevenZipNativeLibrary.IsAvailable)
        {
            throw new InvalidOperationException(SevenZipNativeLibrary.LastError ?? "7z.dll is not loaded.");
        }

        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException("Screen saver archive not found.", archivePath);
        }

        if (cleanOutput && Directory.Exists(outputDirectory))
        {
            Directory.Delete(outputDirectory, recursive: true);
        }

        Directory.CreateDirectory(outputDirectory);

        using var extractor = new SharpSevenZipExtractor(archivePath);
        extractor.ExtractArchive(outputDirectory);
    }
}
