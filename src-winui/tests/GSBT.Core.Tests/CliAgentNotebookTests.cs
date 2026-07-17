using GSBT.Cli.Output;

namespace GSBT.Core.Tests;

public sealed class CliAgentNotebookTests
{
    [Theory]
    [InlineData(75, true, CliAgentNotebook.ChunkyPlateauKnowledgeId)]
    [InlineData(75, false, CliAgentNotebook.GenericPlateauKnowledgeId)]
    [InlineData(99, true, CliAgentNotebook.FinalizationKnowledgeId)]
    public void Plateau_context_maps_to_stable_knowledge_id(
        int percent,
        bool chunky,
        string expectedId)
    {
        Assert.Equal(expectedId, CliAgentNotebook.GetKnowledgeId(percent, chunky));
    }

    [Fact]
    public void Full_hint_is_limited_to_first_heartbeat_of_plateau()
    {
        Assert.True(CliAgentNotebook.ShouldIncludeLiveHint(heartbeat: true, plateauSeconds: 15));
        Assert.False(CliAgentNotebook.ShouldIncludeLiveHint(heartbeat: true, plateauSeconds: 30));
        Assert.False(CliAgentNotebook.ShouldIncludeLiveHint(heartbeat: false, plateauSeconds: 15));
    }

    [Fact]
    public void Chunky_hint_explains_known_behavior_without_claiming_success()
    {
        var hint = CliAgentNotebook.GetLiveHint(percent: 75, chunky: true);

        Assert.Contains("Known behavior", hint, StringComparison.Ordinal);
        Assert.Contains("still working", hint, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("success", hint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Embedded_notebook_supports_custom_folders_and_safe_missing_data_discovery()
    {
        var topics = CliAgentNotebook.Content.GetProperty("topics");
        var productScope = topics.GetProperty("productScope");
        var discovery = topics.GetProperty("missingDataDiscovery");

        Assert.Equal(
            CliAgentNotebook.CustomFolderKnowledgeId,
            productScope.GetProperty("id").GetString());
        Assert.Equal(
            CliAgentNotebook.MissingDataDiscoveryKnowledgeId,
            discovery.GetProperty("id").GetString());
        Assert.Equal(
            "PCGamingWiki",
            discovery.GetProperty("researchOrder")[0].GetProperty("source").GetString());

        var safetyRules = discovery.GetProperty("safetyRules")
            .EnumerateArray()
            .Select(rule => rule.GetString() ?? string.Empty)
            .ToArray();
        Assert.Contains(safetyRules, rule => rule.Contains("Never invent a path", StringComparison.Ordinal));
        Assert.Contains(safetyRules, rule => rule.Contains("online-focused game", StringComparison.Ordinal));

        var example = topics.GetProperty("examples").GetProperty("warcraft3CustomContent");
        Assert.Equal(
            CliAgentNotebook.WarcraftCustomMapsExampleId,
            example.GetProperty("id").GetString());
        Assert.Contains(
            example.GetProperty("sources").EnumerateArray(),
            source => source.GetProperty("type").GetString() == "official");
    }
}
