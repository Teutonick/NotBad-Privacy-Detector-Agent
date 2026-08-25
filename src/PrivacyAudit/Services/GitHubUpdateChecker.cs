using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace PrivacyAudit;

public sealed record UpdateCheckResult(Version CurrentVersion, Version LatestVersion, string LatestTag, string ReleaseUrl)
{
    public bool IsUpdateAvailable => LatestVersion > CurrentVersion;
}

public static class GitHubUpdateChecker
{
    public const string ReleasesUrl = "https://github.com/Teutonick/NotBad-Privacy-Detector-Agent/releases";
    const string LatestReleaseApiUrl = "https://api.github.com/repos/Teutonick/NotBad-Privacy-Detector-Agent/releases/latest";

    static readonly HttpClient Client = CreateClient();

    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken token = default)
    {
        using var response = await Client.GetAsync(LatestReleaseApiUrl, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? throw new InvalidDataException("GitHub release tag is missing.");
        var releaseUrl = root.TryGetProperty("html_url", out var urlElement) ? urlElement.GetString() : null;
        if (!TryParseVersion(tag, out var latest)) throw new InvalidDataException($"Unsupported GitHub release version: {tag}");
        return new(CurrentVersion(), latest, tag, string.IsNullOrWhiteSpace(releaseUrl) ? ReleasesUrl : releaseUrl);
    }

    public static Version CurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(GitHubUpdateChecker).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (TryParseVersion(informational, out var version)) return version;
        return assembly.GetName().Version ?? new Version(0, 0, 0);
    }

    public static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = value.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V')) normalized = normalized[1..];
        var suffix = normalized.IndexOfAny(['-', '+']);
        if (suffix >= 0) normalized = normalized[..suffix];
        if (!Version.TryParse(normalized, out var parsed)) return false;
        version = new Version(parsed.Major, Math.Max(0, parsed.Minor), Math.Max(0, parsed.Build), Math.Max(0, parsed.Revision));
        return true;
    }

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("NotBadPrivacyDetectorAgent", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}
