using System.Reflection;
using System.Text.Json;

namespace GSBT.Cli.Output;

public static class CliAgentNotebook
{
    public const string ChunkyPlateauKnowledgeId = "compression.chunky-large-batch-plateau";
    public const string FinalizationKnowledgeId = "compression.archive-finalization";
    public const string GenericPlateauKnowledgeId = "compression.progress-plateau";
    public const string CustomFolderKnowledgeId = "product.custom-folder-backups";
    public const string MissingDataDiscoveryKnowledgeId = "discovery.missing-save-or-content";
    public const string WarcraftCustomMapsExampleId = "example.warcraft3-custom-content";

    private const string ResourceName = "GSBT.Cli.AgentNotebook.json";
    private static readonly Lazy<JsonElement> Notebook = new(LoadNotebook);

    public static JsonElement Content => Notebook.Value;

    public static string GetKnowledgeId(int percent, bool chunky) =>
        percent >= 99
            ? FinalizationKnowledgeId
            : chunky
                ? ChunkyPlateauKnowledgeId
                : GenericPlateauKnowledgeId;

    public static string GetLiveHint(int percent, bool chunky) =>
        percent >= 99
            ? "Known behavior: archive finalization can remain at 99%. Heartbeats confirm GSBT is still working; wait for the final result."
            : chunky
                ? "Known behavior: chunky compression can remain at one percentage on a large or mixed batch while 7-Zip processes a coarse phase. This heartbeat confirms GSBT is still working."
                : "Compression has not advanced for 15 seconds, but this heartbeat confirms GSBT is still working. Continue monitoring for progress or a final result.";

    public static bool ShouldIncludeLiveHint(bool heartbeat, int plateauSeconds) =>
        heartbeat &&
        plateauSeconds < DefaultLiveHintRepeatBoundarySeconds;

    private static int DefaultLiveHintRepeatBoundarySeconds =>
        (int)CliProgressEventThrottle.DefaultHeartbeatInterval.TotalSeconds * 2;

    private static JsonElement LoadNotebook()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException($"Embedded resource {ResourceName} was not found.");
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.Clone();
        }
        catch
        {
            return JsonSerializer.SerializeToElement(new
            {
                schemaVersion = 1,
                audience = "ai-agent",
                visibility = "machine-facing",
                loadStatus = "fallback",
                purpose = "Use GSBT progress and final-result contracts without inventing undocumented product facts.",
            });
        }
    }
}
