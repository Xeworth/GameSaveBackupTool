namespace GSBT.Core.Common;

public static class PathUtils
{
    public static string? PathToDirectoryOnly(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        var input = path.Trim();
        if (input.EndsWith('\\') || input.EndsWith('/'))
        {
            return input;
        }

        var slashPos = Math.Max(input.LastIndexOf('\\'), input.LastIndexOf('/'));
        if (slashPos < 0)
        {
            return input;
        }

        var tail = input[(slashPos + 1)..];
        if (tail.Contains('*') || tail.Contains('?'))
        {
            return input[..(slashPos + 1)];
        }

        var dot = tail.LastIndexOf('.');
        if (dot > 0 && dot < tail.Length - 1)
        {
            var ext = tail[(dot + 1)..];
            if (ext.All(char.IsLetterOrDigit) && ext.Length <= 8)
            {
                return input[..(slashPos + 1)];
            }
        }

        return input;
    }
}
