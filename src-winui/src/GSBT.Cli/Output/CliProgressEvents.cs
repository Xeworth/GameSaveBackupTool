using System.Text.Json;

namespace GSBT.Cli.Output;

public static class CliProgressEvents
{
    public static void Write(
        CliOutputMode mode,
        string command,
        string phase,
        string message,
        int? current = null,
        int? total = null,
        int? percent = null,
        bool? heartbeat = null,
        int? elapsedSeconds = null,
        int? plateauSeconds = null,
        string? compressionMode = null,
        int? compressionLevel = null,
        string? agentStatus = null,
        string? knowledgeRef = null,
        string? agentHint = null)
    {
        if (!mode.Ai)
        {
            return;
        }

        var payload = new
        {
            schemaVersion = CliAiContract.SchemaVersion,
            type = "progress",
            command,
            phase,
            message,
            current,
            total,
            percent,
            heartbeat,
            elapsedSeconds,
            plateauSeconds,
            compressionMode,
            compressionLevel,
            agentStatus,
            knowledgeRef,
            agentHint,
            timestampUtc = DateTimeOffset.UtcNow.ToString("O"),
        };
        Console.Error.WriteLine(JsonSerializer.Serialize(payload, CliAiContract.CompactJsonOptions));
    }
}
