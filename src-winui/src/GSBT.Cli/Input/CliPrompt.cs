using System.Text;
using GSBT.Cli.Output;
using Spectre.Console;

namespace GSBT.Cli.Input;

public enum CliPromptOutcome
{
    Submitted,
    Canceled,
    EndOfInput,
}

public enum CliConfirmation
{
    Accepted,
    Declined,
    Canceled,
}

public readonly record struct CliLineResult(CliPromptOutcome Outcome, string Value)
{
    public bool IsCanceled => Outcome is CliPromptOutcome.Canceled or CliPromptOutcome.EndOfInput;
}

public sealed class CliChoice<T>
{
    public CliChoice(T value, string preferredText, params string[] aliases)
    {
        Value = value;
        PreferredText = preferredText;
        Aliases = aliases.Prepend(preferredText).ToArray();
    }

    public T Value { get; }

    public string PreferredText { get; }

    public IReadOnlyList<string> Aliases { get; }
}

/// <summary>Shared, forgiving input behavior for human-facing CLI prompts.</summary>
public static class CliPrompt
{
    private static readonly CliChoice<CliConfirmation>[] ConfirmationChoices =
    [
        new(CliConfirmation.Accepted, "yes", "y", "yeah", "yep", "ok", "okay", "sure", "confirm", "proceed", "go ahead"),
        new(CliConfirmation.Declined, "no", "n", "nope", "decline", "skip", "not now"),
        new(CliConfirmation.Canceled, "cancel", "c", "back", "exit", "quit", "stop", "never mind", "nevermind"),
    ];

    public static CliLineResult ReadLine(string prompt)
    {
        AnsiConsole.Markup(prompt);

        if (Console.IsInputRedirected)
        {
            var redirected = Console.ReadLine();
            return redirected is null
                ? new CliLineResult(CliPromptOutcome.EndOfInput, string.Empty)
                : new CliLineResult(CliPromptOutcome.Submitted, redirected.Trim());
        }

        var input = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Escape)
            {
                Console.WriteLine();
                return new CliLineResult(CliPromptOutcome.Canceled, string.Empty);
            }

            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return new CliLineResult(CliPromptOutcome.Submitted, input.ToString().Trim());
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (input.Length > 0)
                {
                    input.Length--;
                    Console.Write("\b \b");
                }

                continue;
            }

            if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.U)
            {
                while (input.Length > 0)
                {
                    input.Length--;
                    Console.Write("\b \b");
                }

                continue;
            }

            if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.W)
            {
                while (input.Length > 0 && char.IsWhiteSpace(input[^1]))
                {
                    input.Length--;
                    Console.Write("\b \b");
                }

                while (input.Length > 0 && !char.IsWhiteSpace(input[^1]))
                {
                    input.Length--;
                    Console.Write("\b \b");
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                input.Append(key.KeyChar);
                Console.Write(key.KeyChar);
            }
        }
    }

    public static CliConfirmation Confirm(string prompt, bool defaultValue)
    {
        var marker = defaultValue ? "[green][[Y]][/]/n" : "y/[green][[N]][/]";
        while (true)
        {
            var input = ReadLine($"{prompt} {marker} [dim](Esc cancels)[/]: ");
            if (input.IsCanceled)
            {
                return CliConfirmation.Canceled;
            }

            if (string.IsNullOrWhiteSpace(input.Value))
            {
                return defaultValue ? CliConfirmation.Accepted : CliConfirmation.Declined;
            }

            if (TryMatch(input.Value, ConfirmationChoices, out var choice, out var suggestion))
            {
                return choice;
            }

            var guidance = suggestion is null
                ? "Please enter yes or no, or press Esc to cancel."
                : $"I did not recognize that. Did you mean '{suggestion}'?";
            CliConsoleFormatter.WriteWarning(guidance);
        }
    }

    public static bool IsCancellationText(string value) =>
        TryMatch(value, ConfirmationChoices, out var choice, out _)
        && choice == CliConfirmation.Canceled;

    public static bool TryMatch<T>(
        string input,
        IReadOnlyList<CliChoice<T>> choices,
        out T value,
        out string? suggestion)
    {
        value = default!;
        suggestion = null;
        var normalized = Normalize(input);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        for (var index = 0; index < choices.Count; index++)
        {
            if (choices[index].Aliases.Any(alias => Normalize(alias) == normalized))
            {
                value = choices[index].Value;
                return true;
            }
        }

        if (normalized.Length >= 3)
        {
            var prefixMatches = Enumerable.Range(0, choices.Count)
                .Where(index => choices[index].Aliases.Any(alias => Normalize(alias).StartsWith(normalized, StringComparison.Ordinal)))
                .ToArray();
            if (prefixMatches.Length == 1)
            {
                value = choices[prefixMatches[0]].Value;
                return true;
            }
        }

        var nearest = choices
            .SelectMany((choice, index) => choice.Aliases.Select(alias => new
            {
                ChoiceIndex = index,
                Distance = EditDistance(normalized, Normalize(alias)),
            }))
            .OrderBy(candidate => candidate.Distance)
            .FirstOrDefault();
        if (nearest is not null && nearest.Distance <= Math.Max(1, normalized.Length / 4))
        {
            suggestion = choices[nearest.ChoiceIndex].PreferredText;
        }

        return false;
    }

    private static string Normalize(string value)
    {
        var normalized = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                if (pendingSpace && normalized.Length > 0)
                {
                    normalized.Append(' ');
                }

                normalized.Append(character);
                pendingSpace = false;
            }
            else
            {
                pendingSpace = true;
            }
        }

        return normalized.ToString();
    }

    private static int EditDistance(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            current[0] = leftIndex;
            for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                var substitution = left[leftIndex - 1] == right[rightIndex - 1] ? 0 : 1;
                current[rightIndex] = Math.Min(
                    Math.Min(current[rightIndex - 1] + 1, previous[rightIndex] + 1),
                    previous[rightIndex - 1] + substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }
}
