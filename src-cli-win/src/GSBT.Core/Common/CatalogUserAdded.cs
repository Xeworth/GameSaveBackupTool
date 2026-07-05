using System.Text.Json;

namespace GSBT.Core.Common;

/// <summary>
/// Identifies rows the user added manually (not from the install scan). Persisted in <c>game_save_data.json</c>.
/// </summary>
public static class CatalogUserAdded
{
    public const string JsonPropertyName = "is_custom_game";

    public static bool IsUserAddedEntry(IReadOnlyDictionary<string, object?> row)
    {
        if (row.TryGetValue(JsonPropertyName, out var raw) && CoerceBool(raw))
        {
            return true;
        }

        // Legacy C# custom rows: scan persisted only scan_outcome + save_path (scanner always writes steam_app_id).
        if (row.ContainsKey("steam_app_id"))
        {
            return false;
        }

        var outcome = CoerceString(row.GetValueOrDefault("scan_outcome"));
        if (!string.Equals(outcome, "SAVE_ON_DISK", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var path = CoerceString(row.GetValueOrDefault("save_path"));
        return !string.IsNullOrWhiteSpace(path);
    }

    public static string? CoerceString(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is string s)
        {
            return s;
        }

        if (value is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.String => je.GetString(),
                JsonValueKind.Number => je.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => null,
                _ => je.ToString()
            };
        }

        return Convert.ToString(value);
    }

    public static bool CoerceBool(object? value)
    {
        if (value is null)
        {
            return false;
        }

        if (value is bool b)
        {
            return b;
        }

        if (value is JsonElement je)
        {
            return je.ValueKind == JsonValueKind.True;
        }

        var s = CoerceString(value);
        return string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) || s == "1";
    }
}
