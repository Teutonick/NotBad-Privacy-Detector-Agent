using System.Text.Json;
using System.Text.Json.Nodes;

namespace PrivacyAudit.Core;

public sealed record SavedSimilarityMatch(string Path, double Score, string Details);

public sealed class SimilarityAnalysisResult
{
    public const string MetadataKey = "similarityAnalysis";
    public string Kind { get; set; } = "";
    public DateTime CompletedAtUtc { get; set; }
    public List<SavedSimilarityMatch> Matches { get; set; } = [];

    public static bool TryParse(string? metadataJson, out SimilarityAnalysisResult? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(metadataJson)) return false;
        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            if (!document.RootElement.TryGetProperty(MetadataKey, out var value)) return false;
            result = value.Deserialize<SimilarityAnalysisResult>();
            return result is not null;
        }
        catch { return false; }
    }

    public static string InjectIntoMetadata(string? metadataJson, SimilarityAnalysisResult result)
    {
        JsonObject root;
        try { root = JsonNode.Parse(string.IsNullOrWhiteSpace(metadataJson) ? "{}" : metadataJson) as JsonObject ?? []; }
        catch { root = []; }
        root[MetadataKey] = JsonSerializer.SerializeToNode(result);
        return root.ToJsonString();
    }
}
