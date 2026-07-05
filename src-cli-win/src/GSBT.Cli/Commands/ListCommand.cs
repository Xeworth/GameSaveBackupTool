using GSBT.Cli.Catalog;
using GSBT.Cli.Output;
using GSBT.Core.Catalog;
using GSBT.Core.Services;

namespace GSBT.Cli.Commands;

public static class ListCommand
{
    public static int Run(CliHost host, string? filterToken, CliOutputMode output)
    {
        if (!CatalogListFilter.TryParse(filterToken, out var filterMode))
        {
            var message = $"Unknown filter \"{filterToken}\". Use found, not-found, or all.";
            if (output.Ai)
            {
                CliAiContract.WriteError("list", message, 1, "invalid_filter");
            }
            else
            {
                CliConsoleFormatter.WriteError(message);
            }

            return 1;
        }

        if (!output.Json)
        {
            CliConsoleFormatter.WriteCommandStart("gsbt list");
        }

        try
        {
            var snapshot = CatalogSnapshot.Build(host, filterMode);
            var entries = snapshot.Entries;
            if (!output.Json)
            {
                entries = CatalogSaveSizeEnricher.WithSaveSizes(
                    entries,
                    msg => Console.Error.WriteLine(msg));
            }

            if (output.Json)
            {
                CliConsoleFormatter.WriteJsonList(entries, CatalogListFilter.ToToken(filterMode), output.Ai);
            }
            else
            {
                CliConsoleFormatter.WriteListTable(entries, filterMode);
            }
        }
        finally
        {
            if (!output.Json)
            {
                CliConsoleFormatter.WriteCommandEnd();
            }
        }

        return 0;
    }
}
