namespace GSBT.Core.Models;

/// <summary>
/// Describes how Ludusavi save paths were chosen for lookup (Steam id keyed vs title index).
/// </summary>
public enum LudusaviMatchKind
{
    /// <summary>No paths from manifest (Steam id omitted and no name hit, or omitted when strict forbids).</summary>
    None,
    /// <summary>The Steam app id is present under <c>steam_index</c> with a path array (possibly empty).</summary>
    SteamId,
    /// <summary>Paths resolved from normalized title <c>name_index</c> only.</summary>
    NameIndex,
}
