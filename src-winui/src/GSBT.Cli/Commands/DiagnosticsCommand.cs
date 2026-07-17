using System.Text.Json;
using GSBT.Cli.Output;
using GSBT.Core.Common;
using GSBT.Core.Services;

namespace GSBT.Cli.Commands;

public static class DiagnosticsCommand
{
    public static int Run(CliHost host, string? outputPath, CliOutputMode mode)
    {
        if (!mode.Json)
        {
            CliConsoleFormatter.WriteCommandStart("gsbt diagnostics");
        }

        try
        {
            var destination = string.IsNullOrWhiteSpace(outputPath)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    $"GSBT-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.json")
                : Path.GetFullPath(outputPath);
            var written = OperationHistoryStore.ExportRedacted(
                destination,
                host.ScanService.GetManifestProvenance());
            if (mode.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    schemaVersion = CliAiContract.SchemaVersion,
                    command = "diagnostics",
                    success = true,
                    version = AppVersionInfo.DisplayVersion,
                    outputPath = written,
                    pathsRedacted = true,
                }, CliAiContract.JsonOptions));
            }
            else
            {
                Console.WriteLine($"Diagnostics exported to {written}");
                Console.WriteLine("Save and backup paths inside the report are redacted.");
            }

            return 0;
        }
        catch (Exception ex)
        {
            if (mode.Ai)
            {
                CliAiContract.WriteError("diagnostics", ex.Message, 2, "diagnostics_export_failed");
            }
            else
            {
                CliConsoleFormatter.WriteError(ex.Message);
            }

            return 2;
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
