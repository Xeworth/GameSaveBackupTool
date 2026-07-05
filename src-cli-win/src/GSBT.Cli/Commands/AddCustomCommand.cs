using GSBT.Cli.Catalog;
using GSBT.Cli.Output;
using GSBT.Core.Services;
using Spectre.Console;

namespace GSBT.Cli.Commands;

public static class AddCustomCommand
{
    public static int Run(CliHost host, string gameName, string saveFolder)
    {
        CliConsoleFormatter.WriteCommandStart("gsbt add custom");
        try
        {
            var (ok, message) = CustomGameCatalogService.AddFolderGame(
                host.CatalogManager,
                gameName,
                saveFolder);

            if (!ok)
            {
                CliConsoleFormatter.WriteError(message);
                return 1;
            }

            AnsiConsole.MarkupLine($"[green]{Markup.Escape(message)}[/]");
            Console.WriteLine("Run gsbt list found to see it in the catalog.");
            return 0;
        }
        finally
        {
            CliConsoleFormatter.WriteCommandEnd();
        }
    }
}
