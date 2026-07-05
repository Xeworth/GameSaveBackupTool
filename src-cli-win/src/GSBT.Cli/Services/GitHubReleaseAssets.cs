using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GSBT.Cli.Services;

public static class GitHubReleaseAssets
{
    public const string DefaultRepo = "Xeworth/GameSaveBackupTool";

    public const string CliInstallScriptUrl =
        "https://raw.githubusercontent.com/Xeworth/GameSaveBackupTool/main/src-cli-win/scripts/install.ps1";

    public const string GuiInstallScriptUrl =
        "https://raw.githubusercontent.com/Xeworth/GameSaveBackupTool/main/src-winui/scripts/install.ps1";

    private static readonly HttpClient Http = CreateClient();

    public static async Task<string> ResolveGuiInstallerUrlAsync(CancellationToken cancellationToken = default)
    {
        var env = Environment.GetEnvironmentVariable("GSBT_INSTALLER_URL");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env.Trim();
        }

        var repo = Environment.GetEnvironmentVariable("GSBT_REPO")?.Trim();
        if (string.IsNullOrWhiteSpace(repo))
        {
            repo = DefaultRepo;
        }

        var release = await FetchLatestReleaseAsync(repo, cancellationToken).ConfigureAwait(false);
        var asset = release.Assets.FirstOrDefault(IsGuiInstallerAsset)
            ?? throw new InvalidOperationException(
                "No GUI installer found on the latest GitHub release. " +
                "Publish gsbt-setup-*.exe or GSBT_Setup_*.exe, or set GSBT_INSTALLER_URL.");

        return asset.BrowserDownloadUrl;
    }

    public static async Task<(string TagName, string DownloadUrl)> ResolveGuiInstallerAsync(
        CancellationToken cancellationToken = default)
    {
        var env = Environment.GetEnvironmentVariable("GSBT_INSTALLER_URL");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return ("override", env.Trim());
        }

        var repo = Environment.GetEnvironmentVariable("GSBT_REPO")?.Trim();
        if (string.IsNullOrWhiteSpace(repo))
        {
            repo = DefaultRepo;
        }

        var release = await FetchLatestReleaseAsync(repo, cancellationToken).ConfigureAwait(false);
        var asset = release.Assets.FirstOrDefault(IsGuiInstallerAsset)
            ?? throw new InvalidOperationException(
                "No GUI installer found on the latest GitHub release. " +
                "Publish gsbt-setup-*.exe or GSBT_Setup_*.exe, or set GSBT_INSTALLER_URL.");

        return (release.TagName, asset.BrowserDownloadUrl);
    }

    public static async Task<HttpResponseMessage> HttpGetAsync(string url, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("gsbt-cli");
        return await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool IsGuiInstallerAsset(ReleaseAsset asset) =>
        asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        && asset.Name.Contains("setup", StringComparison.OrdinalIgnoreCase);

    private static async Task<ReleaseResponse> FetchLatestReleaseAsync(string repo, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.github.com/repos/{repo}/releases/latest");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("gsbt-cli");

        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var release = await JsonSerializer.DeserializeAsync<ReleaseResponse>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return release ?? throw new InvalidOperationException($"Could not parse GitHub release JSON for {repo}.");
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("gsbt-cli");
        return client;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed class ReleaseResponse
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; init; } = string.Empty;

        [JsonPropertyName("assets")]
        public List<ReleaseAsset> Assets { get; init; } = [];
    }

    private sealed class ReleaseAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; init; } = string.Empty;
    }
}
