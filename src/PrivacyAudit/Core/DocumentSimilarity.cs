using System.Text.RegularExpressions;

namespace PrivacyAudit.Core;

public sealed class SimilarityMatch
{
    public required Finding Finding { get; init; }
    public required double Score { get; init; }
    public int ScorePercentage => (int)Math.Round(Score * 100);
    public string Details { get; set; } = "";
}

public static class DocumentSimilarity
{
    static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // RU common stop words
        "и", "в", "во", "не", "что", "он", "на", "я", "с", "со", "как", "а", "то", "все", "она",
        "так", "его", "но", "да", "ты", "к", "у", "же", "вы", "за", "бы", "по", "только", "ее",
        "мне", "было", "вот", "от", "меня", "еще", "нет", "о", "из", "ему", "теперь", "когда",
        "даже", "ну", "вдруг", "ли", "если", "уже", "или", "ни", "быть", "был", "него", "до",
        "вас", "нибудь", "опять", "уж", "вам", "ведь", "там", "потом", "себя", "ничего", "ей",
        "может", "они", "тут", "где", "есть", "надо", "ней", "для", "мы", "тебя", "их", "чем",
        "была", "сам", "чтоб", "без", "будет", "про", "всего", "человек", "года", "которой",
        // EN common stop words
        "a", "about", "above", "after", "again", "against", "all", "am", "an", "and", "any", "are",
        "as", "at", "be", "because", "been", "before", "being", "below", "between", "both", "but",
        "by", "can", "could", "did", "do", "does", "doing", "down", "during", "each", "few", "for",
        "from", "further", "had", "has", "have", "having", "he", "her", "here", "hers", "herself",
        "him", "himself", "his", "how", "if", "in", "into", "is", "it", "its", "itself", "just",
        "me", "more", "most", "my", "myself", "no", "nor", "not", "now", "of", "off", "on", "once",
        "only", "or", "other", "our", "ours", "ourselves", "out", "over", "own", "same", "should",
        "so", "some", "such", "than", "that", "the", "their", "theirs", "them", "themselves", "then",
        "there", "these", "they", "this", "those", "through", "to", "too", "under", "until", "up",
        "very", "was", "we", "were", "what", "when", "where", "which", "while", "who", "whom", "why",
        "will", "with", "would", "you", "your", "yours", "yourself", "yourselves"
    };

    static readonly Regex WordSplitRegex = new(@"[\p{L}\p{N}_]{2,}", RegexOptions.Compiled);

    public static List<SimilarityMatch> FindSimilar(
        Finding queryFinding,
        IEnumerable<Finding> candidateFindings,
        CancellationToken token = default,
        IProgress<(int current, int total)>? progress = null)
    {
        var results = new List<SimilarityMatch>();
        if (!File.Exists(queryFinding.Path)) return results;

        var queryText = TextExtractor.ExtractText(queryFinding.Path);
        if (string.IsNullOrWhiteSpace(queryText)) return results;

        var queryTokens = Tokenize(queryText);
        if (queryTokens.Count == 0) return results;

        var candidates = candidateFindings
            .Where(f => !f.Ignored && f.Path != queryFinding.Path && TextExtractor.IsSupported(f.Path) && File.Exists(f.Path))
            .ToArray();

        if (candidates.Length == 0) return results;

        // 1. Tokenize all candidate documents
        var docTokens = new List<(Finding Finding, Dictionary<string, int> TermCounts, int TotalTokens)>();
        for (int i = 0; i < candidates.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var c = candidates[i];
            var text = TextExtractor.ExtractText(c.Path);
            if (!string.IsNullOrWhiteSpace(text))
            {
                var tokens = Tokenize(text);
                if (tokens.Count > 0)
                {
                    docTokens.Add((c, tokens, tokens.Values.Sum()));
                }
            }

            if ((i + 1) % 5 == 0 || i == candidates.Length - 1)
            {
                progress?.Report((i + 1, candidates.Length));
            }
        }

        if (docTokens.Count == 0) return results;

        // 2. Compute Document Frequency (DF) across the mini corpus
        var totalDocs = docTokens.Count + 1;
        var docFreq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var term in queryTokens.Keys)
        {
            docFreq[term] = 1;
        }

        foreach (var doc in docTokens)
        {
            foreach (var term in doc.TermCounts.Keys)
            {
                docFreq[term] = docFreq.GetValueOrDefault(term, 0) + 1;
            }
        }

        // 3. Compute TF-IDF vectors
        var queryVector = BuildTfIdfVector(queryTokens, queryTokens.Values.Sum(), docFreq, totalDocs);
        var queryNorm = ComputeVectorNorm(queryVector);
        if (queryNorm == 0) return results;

        foreach (var doc in docTokens)
        {
            token.ThrowIfCancellationRequested();
            var docVector = BuildTfIdfVector(doc.TermCounts, doc.TotalTokens, docFreq, totalDocs);
            var docNorm = ComputeVectorNorm(docVector);
            if (docNorm == 0) continue;

            var similarity = ComputeCosineSimilarity(queryVector, queryNorm, docVector, docNorm);
            if (similarity >= 0.15) // Keep matches with at least 15% similarity
            {
                results.Add(new SimilarityMatch
                {
                    Finding = doc.Finding,
                    Score = Math.Round(Math.Min(1.0, similarity), 3),
                    Details = $"TF-IDF Cosine Similarity: {similarity:P0}"
                });
            }
        }

        return results.OrderByDescending(x => x.Score).Take(50).ToList();
    }

    public static Dictionary<string, int> Tokenize(string text)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text)) return counts;

        // Process first 200,000 characters for instant performance
        var slice = text.Length > 200000 ? text[..200000] : text;
        var matches = WordSplitRegex.Matches(slice);

        foreach (Match m in matches)
        {
            var word = m.Value.ToLowerInvariant();
            if (word.Length < 2 || word.Length > 40 || StopWords.Contains(word)) continue;
            counts[word] = counts.GetValueOrDefault(word, 0) + 1;
        }

        return counts;
    }

    static Dictionary<string, double> BuildTfIdfVector(
        Dictionary<string, int> termCounts,
        int totalTokens,
        Dictionary<string, int> docFreq,
        int totalDocs)
    {
        var vector = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (totalTokens == 0) return vector;

        foreach (var (term, count) in termCounts)
        {
            // Augmented term frequency
            double tf = 0.5 + (0.5 * count / totalTokens);
            int df = docFreq.GetValueOrDefault(term, 1);
            // Smooth inverse document frequency
            double idf = Math.Log((double)(1 + totalDocs) / (1 + df)) + 1.0;

            vector[term] = tf * idf;
        }

        return vector;
    }

    static double ComputeVectorNorm(Dictionary<string, double> vector)
    {
        double sum = 0;
        foreach (var val in vector.Values)
        {
            sum += val * val;
        }
        return Math.Sqrt(sum);
    }

    static double ComputeCosineSimilarity(
        Dictionary<string, double> v1,
        double norm1,
        Dictionary<string, double> v2,
        double norm2)
    {
        double dotProduct = 0;
        // Iterate over the smaller vector
        var (smaller, larger) = v1.Count <= v2.Count ? (v1, v2) : (v2, v1);

        foreach (var (term, val1) in smaller)
        {
            if (larger.TryGetValue(term, out var val2))
            {
                dotProduct += val1 * val2;
            }
        }

        return dotProduct / (norm1 * norm2);
    }
}
