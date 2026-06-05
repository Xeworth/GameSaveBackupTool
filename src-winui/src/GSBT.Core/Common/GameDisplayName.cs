using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GSBT.Core.Common;

/// <summary>Human-facing game titles for the catalog (not Ludusavi manifest keys).</summary>
public static class GameDisplayName
{
    private static readonly Regex MultiSpace = new(@"\s{2,}", RegexOptions.Compiled);

    private static readonly Regex[] ParentheticalAsciiMarks =
    [
        new(@"\(\s*TM\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\(\s*R\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\(\s*C\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\(\s*REG\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\(\s*COPY\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    /// <summary>Removes common trademark / copyright symbols and collapses whitespace.</summary>
    public static string CleanDisplayName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        // FormC avoids compatibility decomposition (e.g. ™ → "TM") which would corrupt words like "Batman".
        var s = name.Trim().Normalize(NormalizationForm.FormC);
        var sb = new StringBuilder(s.Length);
        foreach (var rune in s.EnumerateRunes())
        {
            if (IsTrademarkOrCopyRune(rune))
            {
                continue;
            }

            sb.Append(rune);
        }

        var t = sb.ToString();
        foreach (var rx in ParentheticalAsciiMarks)
        {
            t = rx.Replace(t, " ");
        }

        return MultiSpace.Replace(t.Trim(), " ");
    }

    private static bool IsTrademarkOrCopyRune(Rune rune) =>
        rune.Value switch
        {
            '\u00AE' or '\u00A9' or '\u2122' or '\u2120' => true,
            _ => false
        };
}
