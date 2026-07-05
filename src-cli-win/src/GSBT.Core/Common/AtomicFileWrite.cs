namespace GSBT.Core.Common;

/// <summary>Write text atomically (temp + replace) to avoid corrupt JSON on crash.</summary>
public static class AtomicFileWrite
{
    public static void WriteAllText(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }
}
