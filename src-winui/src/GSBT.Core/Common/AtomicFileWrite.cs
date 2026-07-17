namespace GSBT.Core.Common;

/// <summary>Write text atomically (temp + replace) to avoid corrupt JSON on crash.</summary>
public static class AtomicFileWrite
{
    public static void WriteAllText(string path, string content)
    {
        using var processLock = CrossProcessLock.Acquire("file:" + Path.GetFullPath(path));
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tmp = path + "." + Environment.ProcessId + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                var backup = path + ".bak";
                try
                {
                    File.Replace(tmp, path, backup, ignoreMetadataErrors: true);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Copy(path, backup, overwrite: true);
                    File.Move(tmp, path, overwrite: true);
                }
            }
            else
            {
                File.Move(tmp, path);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tmp))
                {
                    File.Delete(tmp);
                }
            }
            catch
            {
                // A stale unique temp file is harmless and can be cleaned on a later start.
            }
        }
    }
}
