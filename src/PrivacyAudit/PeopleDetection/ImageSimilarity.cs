using System.Numerics;
using PrivacyAudit.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PrivacyAudit.PeopleDetection;

public readonly record struct PerceptualImageHash(ulong DHash, ulong AHash);

public static class ImageSimilarity
{
    public static PerceptualImageHash? ComputeHash(string imagePath)
    {
        if (!File.Exists(imagePath)) return null;

        try
        {
            using var image = SixLabors.ImageSharp.Image.Load<L8>(imagePath);

            // 1. dHash (Difference Hash) - 9x8 resize
            using var dImage = image.Clone(x => x.Resize(new ResizeOptions
            {
                Size = new SixLabors.ImageSharp.Size(9, 8),
                Mode = ResizeMode.Stretch
            }));

            ulong dHash = 0;
            int bitIndex = 0;
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    if (dImage[x, y].PackedValue > dImage[x + 1, y].PackedValue)
                    {
                        dHash |= (1UL << bitIndex);
                    }
                    bitIndex++;
                }
            }

            // 2. aHash (Average Hash) - 8x8 resize
            using var aImage = image.Clone(x => x.Resize(new ResizeOptions
            {
                Size = new SixLabors.ImageSharp.Size(8, 8),
                Mode = ResizeMode.Stretch
            }));

            long sum = 0;
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    sum += aImage[x, y].PackedValue;
                }
            }
            double avg = sum / 64.0;

            ulong aHash = 0;
            bitIndex = 0;
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    if (aImage[x, y].PackedValue >= avg)
                    {
                        aHash |= (1UL << bitIndex);
                    }
                    bitIndex++;
                }
            }

            return new PerceptualImageHash(dHash, aHash);
        }
        catch
        {
            return null;
        }
    }

    public static double CompareHashes(PerceptualImageHash h1, PerceptualImageHash h2)
    {
        int dDist = BitOperations.PopCount(h1.DHash ^ h2.DHash);
        int aDist = BitOperations.PopCount(h1.AHash ^ h2.AHash);

        double dSim = 1.0 - (dDist / 64.0);
        double aSim = 1.0 - (aDist / 64.0);

        // dHash is stronger for structure; aHash captures overall brightness distribution
        return (dSim * 0.70) + (aSim * 0.30);
    }

    public static List<SimilarityMatch> FindSimilar(
        Finding queryFinding,
        IEnumerable<Finding> candidateFindings,
        CancellationToken token = default,
        IProgress<(int current, int total)>? progress = null,
        ManualResetEventSlim? pauseGate = null)
    {
        var results = new List<SimilarityMatch>();
        var queryHash = ComputeHash(queryFinding.Path);
        if (queryHash is null) return results;

        var candidates = candidateFindings
            .Where(f => !f.Ignored && f.Path != queryFinding.Path && File.Exists(f.Path))
            .ToArray();

        for (int i = 0; i < candidates.Length; i++)
        {
            pauseGate?.Wait(token);
            token.ThrowIfCancellationRequested();
            var candidate = candidates[i];
            var targetHash = ComputeHash(candidate.Path);
            if (targetHash is not null)
            {
                var similarity = CompareHashes(queryHash.Value, targetHash.Value);
                if (similarity >= 0.70) // Match threshold for resized/compressed/saved copies
                {
                    results.Add(new SimilarityMatch
                    {
                        Finding = candidate,
                        Score = Math.Round(similarity, 3),
                        Details = $"Perceptual dHash/aHash Match: {similarity:P0}"
                    });
                }
            }

            if ((i + 1) % 5 == 0 || i == candidates.Length - 1)
            {
                progress?.Report((i + 1, candidates.Length));
            }
        }

        return results.OrderByDescending(x => x.Score).Take(50).ToList();
    }
}
