using GSBT.Core.Services;

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: ScreenSaverAssetPacker <assets-dir> <output-screensaver.7z> <7z.dll>");
    return 1;
}

var assetsDir = Path.GetFullPath(args[0]);
var outputArchive = Path.GetFullPath(args[1]);
var sevenZipDll = Path.GetFullPath(args[2]);

if (!Directory.Exists(assetsDir))
{
    Console.Error.WriteLine($"Assets directory not found: {assetsDir}");
    return 1;
}

if (!File.Exists(sevenZipDll))
{
    Console.Error.WriteLine($"7z.dll not found: {sevenZipDll}");
    return 1;
}

if (!SevenZipNativeLibrary.TryInitialize(sevenZipDll))
{
    Console.Error.WriteLine(SevenZipNativeLibrary.LastError ?? "Failed to load 7z.dll.");
    return 1;
}

try
{
    ScreenSaverMediaArchive.PackFromAssetsDirectory(assetsDir, outputArchive);
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

var sizeMb = new FileInfo(outputArchive).Length / (1024.0 * 1024.0);
Console.WriteLine($"Packed screen saver media: {outputArchive} ({sizeMb:F1} MB)");
return 0;
