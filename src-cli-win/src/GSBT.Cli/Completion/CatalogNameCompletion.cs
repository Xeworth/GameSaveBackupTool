using System.CommandLine.Completions;
using GSBT.Cli.Catalog;

namespace GSBT.Cli.Completion;

public static class CatalogNameCompletion
{
    public static IEnumerable<CompletionItem> GetCompletions(CliHost host, string prefix)
    {
        var snapshot = CatalogSnapshot.LoadCurrent(host);
        var p = prefix;
        foreach (var name in snapshot.Entries.Select(e => e.GameName))
        {
            if (name.StartsWith(p, StringComparison.OrdinalIgnoreCase)
                || name.Contains(p, StringComparison.OrdinalIgnoreCase))
            {
                yield return new CompletionItem(name);
            }
        }
    }
}
