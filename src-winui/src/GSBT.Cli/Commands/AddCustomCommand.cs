using System.Text.Json;
using GSBT.Cli.Catalog;
using GSBT.Cli.Output;
using GSBT.Core.Common;
using GSBT.Core.Services;
using Spectre.Console;

namespace GSBT.Cli.Commands;

public static class AddCustomCommand
{
    public static int Run(CliHost host, string gameName, string saveFolder, CliOutputMode mode)
    {
        if (!mode.Json)
        {
            CliConsoleFormatter.WriteCommandStart("gsbt add custom");
        }

        try
        {
            var (ok, message) = CustomGameCatalogService.AddFolderGame(
                host.CatalogManager,
                gameName,
                saveFolder);

            if (!ok)
            {
                if (mode.Json)
                {
                    CliAiContract.WriteError("add custom", message, 1, "invalid_custom_entry");
                }
                else
                {
                    CliConsoleFormatter.WriteError(message);
                }

                return 1;
            }

            if (mode.Json)
            {
                var resolvedFolder = Path.GetFullPath(
                    host.CatalogManager.ResolvePath(saveFolder, null) ?? saveFolder);
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    schemaVersion = CliAiContract.SchemaVersion,
                    command = "add custom",
                    success = true,
                    message,
                    entry = new
                    {
                        name = GameDisplayName.CleanDisplayName(gameName),
                        folder = resolvedFolder,
                        platform = "Custom",
                    },
                    nextActions = new[]
                    {
                        "Run gsbt list found --ai to inspect the registered entry.",
                        "Run gsbt backup <name-or-index> --ai to create its first backup.",
                    },
                }, CliAiContract.JsonOptions));
                return 0;
            }

            AnsiConsole.MarkupLine($"[green]{Markup.Escape(message)}[/]");
            Console.WriteLine("Run gsbt list found to see it in the catalog.");
            return 0;
        }
        finally
        {
            if (!mode.Json)
            {
                CliConsoleFormatter.WriteCommandEnd();
            }
        }
    }
}
