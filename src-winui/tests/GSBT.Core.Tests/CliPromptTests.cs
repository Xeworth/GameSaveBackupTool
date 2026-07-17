using GSBT.Cli.Input;

namespace GSBT.Core.Tests;

public sealed class CliPromptTests
{
    public enum DestinationChoice
    {
        Suggested,
        Select,
        Cancel,
    }

    private static readonly CliChoice<DestinationChoice>[] Choices =
    [
        new(DestinationChoice.Suggested, "use suggested path", "1", "suggested", "use suggested", "default"),
        new(DestinationChoice.Select, "select path", "2", "select", "choose folder", "browse"),
        new(DestinationChoice.Cancel, "cancel", "3", "back", "quit", "never mind"),
    ];

    [Theory]
    [InlineData("1", DestinationChoice.Suggested)]
    [InlineData("suggested", DestinationChoice.Suggested)]
    [InlineData("use-suggested", DestinationChoice.Suggested)]
    [InlineData("USE   SUGGESTED PATH", DestinationChoice.Suggested)]
    [InlineData("2", DestinationChoice.Select)]
    [InlineData("select", DestinationChoice.Select)]
    [InlineData("choose folder", DestinationChoice.Select)]
    [InlineData("3", DestinationChoice.Cancel)]
    [InlineData("cancel", DestinationChoice.Cancel)]
    [InlineData("never_mind", DestinationChoice.Cancel)]
    public void Choice_aliases_accept_natural_input(string input, DestinationChoice expected)
    {
        Assert.True(CliPrompt.TryMatch(input, Choices, out var actual, out _));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("sug", DestinationChoice.Suggested)]
    [InlineData("sel", DestinationChoice.Select)]
    [InlineData("can", DestinationChoice.Cancel)]
    public void Unique_prefixes_are_accepted(string input, DestinationChoice expected)
    {
        Assert.True(CliPrompt.TryMatch(input, Choices, out var actual, out _));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Ambiguous_short_input_is_not_guessed()
    {
        Assert.False(CliPrompt.TryMatch("s", Choices, out _, out _));
    }

    [Fact]
    public void Close_typo_gets_guidance_without_auto_selecting()
    {
        Assert.False(CliPrompt.TryMatch("sugested", Choices, out _, out var suggestion));
        Assert.Equal("use suggested path", suggestion);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("c")]
    [InlineData("back")]
    [InlineData("never mind")]
    public void Cancellation_words_are_shared_across_prompts(string input)
    {
        Assert.True(CliPrompt.IsCancellationText(input));
    }
}
