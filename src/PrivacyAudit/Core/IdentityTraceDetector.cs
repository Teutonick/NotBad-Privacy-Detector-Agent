using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PrivacyAudit.Core;

public sealed class IdentityTraceResult
{
    [JsonPropertyName("has_identity_trace")] public bool HasIdentityTrace { get; set; }
    [JsonPropertyName("total_mentions")] public int TotalMentions { get; set; }
    [JsonPropertyName("matched_terms")] public Dictionary<string, string> MatchedTerms { get; set; } = [];
    [JsonPropertyName("summary")] public string Summary { get; set; } = "";

    public static string Serialize(IdentityTraceResult result) => JsonSerializer.Serialize(result);

    public static bool TryParse(string? json, out IdentityTraceResult? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("identity_trace", out var prop)) return false;
            result = JsonSerializer.Deserialize<IdentityTraceResult>(prop.GetRawText());
            if (result is not null)
            {
                var legacyProfileTerms = result.MatchedTerms.Where(x => x.Value.StartsWith("User Profile Directory", StringComparison.OrdinalIgnoreCase)).Select(x => x.Key).ToArray();
                foreach (var term in legacyProfileTerms) result.MatchedTerms.Remove(term);
                if (legacyProfileTerms.Length > 0)
                {
                    result.TotalMentions = result.MatchedTerms.Values.Sum(MentionCount);
                    result.HasIdentityTrace = result.TotalMentions > 0;
                    result.Summary = result.HasIdentityTrace ? string.Join(", ", result.MatchedTerms.Select(kv => $"{kv.Value}: {kv.Key}")) : "";
                }
            }
            return result is not null;
        }
        catch
        {
            return false;
        }
    }

    public static string InjectIntoMetadata(string currentJson, IdentityTraceResult result)
    {
        try
        {
            var dict = string.IsNullOrWhiteSpace(currentJson)
                ? new Dictionary<string, object>()
                : JsonSerializer.Deserialize<Dictionary<string, object>>(currentJson) ?? new();
            dict["identity_trace"] = result;
            return JsonSerializer.Serialize(dict);
        }
        catch
        {
            return JsonSerializer.Serialize(new { identity_trace = result });
        }
    }

    static int MentionCount(string value)
    {
        var match = Regex.Match(value, @"\((\d+)x\)$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return match.Success && int.TryParse(match.Groups[1].Value, out var count) ? count : 1;
    }
}

public sealed class UserIdentityProfile
{
    public Dictionary<string, string> TermsToCategory { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static UserIdentityProfile Collect()
    {
        var profile = new UserIdentityProfile();

        void AddTerm(string? term, string category)
        {
            if (string.IsNullOrWhiteSpace(term)) return;
            var clean = term.Trim();
            if (clean.Length < 3) return;

            // Exclude trivial generic words
            if (clean.Equals("user", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("admin", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("administrator", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("desktop", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("default", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("public", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("system", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("windows", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            profile.TermsToCategory[clean] = category;
        }

        try
        {
            AddTerm(Environment.UserName, "Windows Account");
            AddTerm(Environment.MachineName, "Hostname / PC Name");

            var userProfileDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // Read ~/.gitconfig
            var gitConfigPath = Path.Combine(userProfileDir, ".gitconfig");
            if (File.Exists(gitConfigPath))
            {
                var lines = File.ReadAllLines(gitConfigPath);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("name", StringComparison.OrdinalIgnoreCase) && trimmed.Contains('='))
                    {
                        var parts = trimmed.Split('=', 2);
                        if (parts.Length > 1) AddTerm(parts[1].Trim(), "Git Username");
                    }
                    else if (trimmed.StartsWith("email", StringComparison.OrdinalIgnoreCase) && trimmed.Contains('='))
                    {
                        var parts = trimmed.Split('=', 2);
                        if (parts.Length > 1) AddTerm(parts[1].Trim(), "Git Email");
                    }
                }
            }
        }
        catch
        {
            // Graceful fallback
        }

        return profile;
    }
}

public static class IdentityTraceDetector
{
    static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(400);

    public static IdentityTraceResult Analyze(string filePath, UserIdentityProfile profile, string? textContent = null)
    {
        var result = new IdentityTraceResult();
        if (string.IsNullOrWhiteSpace(filePath) || profile.TermsToCategory.Count == 0) return result;

        var text = textContent;
        if (text is null && File.Exists(filePath) && TextExtractor.IsSupported(filePath))
        {
            try
            {
                var fi = new FileInfo(filePath);
                if (fi.Length <= 1024 * 1024)
                {
                    text = TextExtractor.ExtractText(filePath);
                }
            }
            catch { }
        }

        var combinedContent = $"{Path.GetFileName(filePath)}\n{text ?? ""}";
        int mentions = 0;

        foreach (var (term, category) in profile.TermsToCategory)
        {
            try
            {
                // Word-boundary pattern or literal match for email
                var pattern = term.Contains('@') || term.Contains('.')
                    ? Regex.Escape(term)
                    : $@"\b{Regex.Escape(term)}\b";

                var matches = Regex.Matches(combinedContent, pattern, RegexOptions.IgnoreCase, RegexTimeout);
                if (matches.Count > 0)
                {
                    result.MatchedTerms[term] = $"{category} ({matches.Count}x)";
                    mentions += matches.Count;
                }
            }
            catch (RegexMatchTimeoutException) { }
        }

        if (mentions > 0)
        {
            result.HasIdentityTrace = true;
            result.TotalMentions = mentions;
            result.Summary = $"{string.Join(", ", result.MatchedTerms.Select(kv => $"{kv.Value}: {kv.Key}"))}";
        }

        return result;
    }
}
