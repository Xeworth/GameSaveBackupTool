using GSBT.Core.Catalog;

namespace GSBT.Cli.Catalog;

public static class CatalogListFilter
{
    public const string FoundToken = "found";
    public const string NotFoundToken = "not-found";
    public const string AllToken = "all";

    public static GameCatalogFilterMode DefaultMode => GameCatalogFilterMode.FoundOnly;

    public static bool TryParse(string? token, out GameCatalogFilterMode mode)
    {
        mode = DefaultMode;
        if (string.IsNullOrWhiteSpace(token))
        {
            return true;
        }

        switch (token.Trim().ToLowerInvariant().Replace('_', '-'))
        {
            case FoundToken:
                mode = GameCatalogFilterMode.FoundOnly;
                return true;
            case NotFoundToken:
            case "notfound":
                mode = GameCatalogFilterMode.NotFoundOnly;
                return true;
            case AllToken:
                mode = GameCatalogFilterMode.All;
                return true;
            default:
                return false;
        }
    }

    public static string ToToken(GameCatalogFilterMode mode) =>
        mode switch
        {
            GameCatalogFilterMode.FoundOnly => FoundToken,
            GameCatalogFilterMode.NotFoundOnly => NotFoundToken,
            _ => AllToken,
        };
}
