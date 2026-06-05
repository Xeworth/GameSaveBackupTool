namespace GSBT.WinUI.Services;

/// <summary>Sanitize and truncate batch test display names for cards and chart checkpoints.</summary>
public static class BatchTestDisplayName
{
    public const int MaxInputLength = 32;
    public const int CardTitleMaxLength = 22;
    public const int CheckpointLabelMaxLength = 14;

    public static string Resolve(string? raw, int index) =>
        string.IsNullOrWhiteSpace(raw) ? $"Test {index + 1}" : raw.Trim();

    public static string TruncateForCard(string name) => TruncateWithEllipsis(name, CardTitleMaxLength);

    public static string TruncateForCheckpoint(string name) => TruncateWithEllipsis(name, CheckpointLabelMaxLength);

    private static string TruncateWithEllipsis(string name, int maxLength)
    {
        if (name.Length <= maxLength)
        {
            return name;
        }

        return maxLength <= 1 ? name[..maxLength] : name[..(maxLength - 1)] + "…";
    }
}
