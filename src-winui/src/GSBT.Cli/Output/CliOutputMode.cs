namespace GSBT.Cli.Output;

/// <summary>Derived output flags from <c>--json</c> and <c>--ai</c>; AI keeps stdout JSON and streams progress to stderr.</summary>
public readonly record struct CliOutputMode(bool JsonFlag, bool Ai)
{
    public bool Json => JsonFlag || Ai;

    public bool ShowLive => !Json;

    public bool NonInteractive => Ai;

    public static CliOutputMode From(bool json, bool ai) => new(json, ai);
}
