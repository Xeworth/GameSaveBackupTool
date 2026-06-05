namespace GSBT.Core.Models;

public sealed record GameRecord(
    string Name,
    string? AppId,
    string? InstallPath,
    string Platform = "Unknown",
    /// <summary>Shown in the UI / catalog; when null, <see cref="Name"/> is used. <see cref="Name"/> stays as reported by the store for Ludusavi matching.</summary>
    string? CatalogDisplayName = null);
