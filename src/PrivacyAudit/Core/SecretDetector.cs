using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PrivacyAudit.Core;

public sealed record SecretMatchItem(
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("sample")] string Sample,
    [property: JsonPropertyName("entropy")] double Entropy,
    [property: JsonPropertyName("confidence")] double Confidence
);

public sealed class SecretDetectionResult
{
    [JsonPropertyName("status")] public string Status { get; set; } = "completed";
    [JsonPropertyName("total_matches")] public int TotalMatches { get; set; }
    [JsonPropertyName("categories")] public List<string> Categories { get; set; } = [];
    [JsonPropertyName("matches")] public List<SecretMatchItem> Matches { get; set; } = [];
    [JsonPropertyName("scanned_at_utc")] public DateTime ScannedAtUtc { get; set; } = DateTime.UtcNow;

    public static string Serialize(SecretDetectionResult result) => JsonSerializer.Serialize(result);

    public static bool TryParse(string? json, out SecretDetectionResult? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("secret_scan", out var secretProp)) return false;
            result = JsonSerializer.Deserialize<SecretDetectionResult>(secretProp.GetRawText());
            return result is not null;
        }
        catch
        {
            return false;
        }
    }

    public static string InjectIntoMetadata(string currentJson, SecretDetectionResult secretResult)
    {
        try
        {
            var dict = string.IsNullOrWhiteSpace(currentJson)
                ? new Dictionary<string, object>()
                : JsonSerializer.Deserialize<Dictionary<string, object>>(currentJson) ?? new();
            dict["secret_scan"] = secretResult;
            return JsonSerializer.Serialize(dict);
        }
        catch
        {
            return JsonSerializer.Serialize(new { secret_scan = secretResult });
        }
    }
}

public static class SecretDetector
{
    static readonly Regex OpenAiKeyRegex = new(@"\b(?:sk-[a-zA-Z0-9]{20,}|sk-proj-[a-zA-Z0-9_\-]{20,})\b", RegexOptions.Compiled);
    static readonly Regex GitHubTokenRegex = new(@"\b(?:ghp_[a-zA-Z0-9]{36}|github_pat_[a-zA-Z0-9_]{22,}|gho_[a-zA-Z0-9]{36}|ghs_[a-zA-Z0-9]{36})\b", RegexOptions.Compiled);
    static readonly Regex AwsKeyRegex = new(@"\bAKIA[0-9A-Z]{16}\b", RegexOptions.Compiled);
    static readonly Regex GoogleKeyRegex = new(@"\bAIza[0-9A-Za-z\-_]{35}\b", RegexOptions.Compiled);
    static readonly Regex SlackTokenRegex = new(@"\bxox[baprs]-[0-9a-zA-Z]{10,48}\b", RegexOptions.Compiled);
    static readonly Regex JwtRegex = new(@"\beyJ[a-zA-Z0-9_-]{10,}\.eyJ[a-zA-Z0-9_-]{10,}\.[a-zA-Z0-9_-]+\b", RegexOptions.Compiled);
    static readonly Regex PrivateKeyBlockRegex = new(@"-----BEGIN (?:[A-Z0-9 ]+)?PRIVATE KEY-----", RegexOptions.Compiled);
    static readonly Regex SshKeyRegex = new(@"\bssh-rsa\s+AAAA[0-9A-Za-z+/]+={0,3}\b", RegexOptions.Compiled);
    static readonly Regex DbUriRegex = new(@"(?i)\b(?:postgres|postgresql|mysql|mongodb(?:\+srv)?|redis|jdbc:[a-z0-9_]+):\/\/[^\s""'<>]+", RegexOptions.Compiled);
    static readonly Regex AuthBearerRegex = new(@"(?i)authorization\s*:\s*bearer\s+([A-Za-z0-9_\-.~+/=]{16,})", RegexOptions.Compiled);
    static readonly Regex AssignmentRegex = new(@"(?i)\b(api[_-]?key|secret|token|password|passwd|auth[_-]?token|private[_-]?key|client[_-]?secret|access[_-]?key)\b\s*(?:=|:)\s*[""']?([A-Za-z0-9_\-.~+/=]{10,128})[""']?", RegexOptions.Compiled);

    public static SecretDetectionResult Scan(string text, string? filePath = null)
    {
        var result = new SecretDetectionResult();
        if (string.IsNullOrWhiteSpace(text)) return result;

        var matches = new List<SecretMatchItem>();
        var foundCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Private Keys
        if (PrivateKeyBlockRegex.IsMatch(text))
        {
            matches.Add(new SecretMatchItem("PrivateKey", "-----BEGIN PRIVATE KEY-----", 4.5, 0.99));
            foundCategories.Add("PrivateKey");
        }
        foreach (Match m in SshKeyRegex.Matches(text))
        {
            matches.Add(new SecretMatchItem("SshKey", Redact(m.Value), CalculateEntropy(m.Value), 0.98));
            foundCategories.Add("SshKey");
            if (matches.Count >= 50) break;
        }

        // 2. Database Connection Strings
        foreach (Match m in DbUriRegex.Matches(text))
        {
            matches.Add(new SecretMatchItem("DatabaseConnection", Redact(m.Value), CalculateEntropy(m.Value), 0.95));
            foundCategories.Add("DatabaseConnection");
            if (matches.Count >= 50) break;
        }

        // 3. Known Prefix Tokens
        foreach (Match m in OpenAiKeyRegex.Matches(text))
        {
            matches.Add(new SecretMatchItem("OpenAI_Key", Redact(m.Value), CalculateEntropy(m.Value), 0.99));
            foundCategories.Add("OpenAI_Key");
            if (matches.Count >= 50) break;
        }
        foreach (Match m in GitHubTokenRegex.Matches(text))
        {
            matches.Add(new SecretMatchItem("GitHub_Token", Redact(m.Value), CalculateEntropy(m.Value), 0.99));
            foundCategories.Add("GitHub_Token");
            if (matches.Count >= 50) break;
        }
        foreach (Match m in AwsKeyRegex.Matches(text))
        {
            matches.Add(new SecretMatchItem("AWS_Key", Redact(m.Value), CalculateEntropy(m.Value), 0.95));
            foundCategories.Add("AWS_Key");
            if (matches.Count >= 50) break;
        }
        foreach (Match m in GoogleKeyRegex.Matches(text))
        {
            matches.Add(new SecretMatchItem("Google_Key", Redact(m.Value), CalculateEntropy(m.Value), 0.95));
            foundCategories.Add("Google_Key");
            if (matches.Count >= 50) break;
        }
        foreach (Match m in SlackTokenRegex.Matches(text))
        {
            matches.Add(new SecretMatchItem("Slack_Token", Redact(m.Value), CalculateEntropy(m.Value), 0.95));
            foundCategories.Add("Slack_Token");
            if (matches.Count >= 50) break;
        }

        // 4. JWT Tokens
        foreach (Match m in JwtRegex.Matches(text))
        {
            matches.Add(new SecretMatchItem("JWT_Token", Redact(m.Value), CalculateEntropy(m.Value), 0.95));
            foundCategories.Add("JWT_Token");
            if (matches.Count >= 50) break;
        }

        // 5. Auth Bearer headers
        foreach (Match m in AuthBearerRegex.Matches(text))
        {
            var token = m.Groups[1].Value;
            matches.Add(new SecretMatchItem("Bearer_Token", Redact(token), CalculateEntropy(token), 0.92));
            foundCategories.Add("Bearer_Token");
            if (matches.Count >= 50) break;
        }

        // 6. Key/Secret Assignments
        foreach (Match m in AssignmentRegex.Matches(text))
        {
            var keyName = m.Groups[1].Value;
            var secretVal = m.Groups[2].Value;
            if (IsLikelySecretValue(secretVal))
            {
                var entropy = CalculateEntropy(secretVal);
                var category = keyName.Contains("pass", StringComparison.OrdinalIgnoreCase) ? "Password" : "ApiKey";
                matches.Add(new SecretMatchItem(category, $"{keyName}={Redact(secretVal)}", entropy, 0.90));
                foundCategories.Add(category);
                if (matches.Count >= 50) break;
            }
        }

        // 7. Shannon Entropy Scan on Suspicious Unstructured Tokens
        if (matches.Count < 50)
        {
            var entropyCandidates = new HashSet<string>(StringComparer.Ordinal);
            var words = text.Split(new[] { ' ', '\t', '\r', '\n', '"', '\'', ';', ',', '<', '>', '{', '}', '(', ')', '[', ']' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in words)
            {
                if (word.Length is >= 24 and <= 80 && !word.Contains('/') && !word.Contains('\\') && !word.Contains('.'))
                {
                    var entropy = CalculateEntropy(word);
                    if (entropy >= 4.5 && IsLikelyEntropyToken(word) && !IsCommonCodeWord(word) && entropyCandidates.Add(word))
                    {
                        matches.Add(new SecretMatchItem("HighEntropy_Secret", Redact(word), entropy, 0.85));
                        foundCategories.Add("HighEntropy_Secret");
                        if (matches.Count >= 50) break;
                    }
                }
            }
        }

        result.Matches = matches;
        result.TotalMatches = matches.Count;
        result.Categories = foundCategories.ToList();
        return result;
    }

    public static double CalculateEntropy(string str)
    {
        if (string.IsNullOrEmpty(str)) return 0.0;
        var map = new Dictionary<char, int>();
        foreach (var c in str)
        {
            map[c] = map.GetValueOrDefault(c, 0) + 1;
        }
        double entropy = 0.0;
        double len = str.Length;
        foreach (var count in map.Values)
        {
            double p = count / len;
            entropy -= p * Math.Log2(p);
        }
        return entropy;
    }

    public static string Redact(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "****";
        if (value.Length <= 8) return "****";
        if (value.Length <= 16) return $"{value[..3]}****{value[^2..]}";
        return $"{value[..5]}****{value[^4..]}";
    }

    static bool IsLikelySecretValue(string val)
    {
        if (val.Length < 8) return false;
        if (val.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            val.Equals("false", StringComparison.OrdinalIgnoreCase) ||
            val.Equals("null", StringComparison.OrdinalIgnoreCase) ||
            val.Equals("undefined", StringComparison.OrdinalIgnoreCase) ||
            val.Equals("changeme", StringComparison.OrdinalIgnoreCase) ||
            val.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            val.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return false;

        // Check if value contains alphanumeric mixture or high entropy
        return val.Any(char.IsDigit) && (val.Any(char.IsLetter) || val.Length >= 16);
    }

    static bool IsCommonCodeWord(string word)
    {
        return word.All(char.IsUpper) || word.All(char.IsLower) || word.Contains("System") || word.Contains("Microsoft");
    }

    static bool IsLikelyEntropyToken(string word)
    {
        // Generic entropy detection deliberately favors precision. Known token formats and
        // named assignments are handled above; this fallback accepts only compact token-like
        // ASCII with mixed case and digits, not prose, Unicode text, code identifiers or labels.
        if (word.Any(c => c > 0x7F || !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '+' or '='))) return false;

        var paddingIndex = word.IndexOf('=');
        if (paddingIndex >= 0 && (word.Length - paddingIndex > 2 || word[paddingIndex..].Any(c => c != '='))) return false;

        var upper = word.Count(c => c is >= 'A' and <= 'Z');
        var lower = word.Count(c => c is >= 'a' and <= 'z');
        var digits = word.Count(c => c is >= '0' and <= '9');
        return upper >= 2 && lower >= 2 && digits >= 2;
    }
}
