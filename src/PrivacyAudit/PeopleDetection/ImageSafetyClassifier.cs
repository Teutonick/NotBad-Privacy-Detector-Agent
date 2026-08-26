using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Advanced;

namespace PrivacyAudit.PeopleDetection;

public sealed class ImageSafetyClassifier : IDisposable
{
    readonly InferenceSession _session;
    readonly string _inputName;

    public ImageSafetyClassifier(string modelPath)
    {
        _session = new InferenceSession(modelPath, new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL });
        _inputName = _session.InputMetadata.Keys.Single();
    }

    public ImageSafetyScores Classify(string path, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        using var image = SixLabors.ImageSharp.Image.Load<Rgb24>(path);
        image.Mutate(x => x.Resize(new ResizeOptions { Size = new SixLabors.ImageSharp.Size(224, 224), Mode = ResizeMode.Stretch, Sampler = KnownResamplers.Bicubic }));
        var pixels = new DenseTensor<float>([1, 3, 224, 224]);
        for (var y = 0; y < 224; y++)
        {
            token.ThrowIfCancellationRequested();
            var row = image.Frames.RootFrame.DangerousGetPixelRowMemory(y).Span;
            for (var x = 0; x < 224; x++)
            {
                pixels[0, 0, y, x] = row[x].R;
                pixels[0, 1, y, x] = row[x].G;
                pixels[0, 2, y, x] = row[x].B;
            }
        }
        var input = NamedOnnxValue.CreateFromTensor(_inputName, pixels);
        using var outputs = _session.Run([input]);
        var values = outputs.First().AsEnumerable<float>().Take(3).Select(x => (double)x).ToArray();
        if (values.Length != 3) throw new InvalidDataException("Image Safety model returned an unexpected output shape.");
        return new(values[0], values[1], values[2]);
    }

    public void Dispose() => _session.Dispose();
}

public readonly record struct ImageSafetyScores(double Nsfl, double Nsfw, double Sfw)
{
    public ImageSafetyClass PrimaryClass => Nsfl >= Nsfw && Nsfl >= Sfw ? ImageSafetyClass.NSFL : Nsfw >= Sfw ? ImageSafetyClass.NSFW : ImageSafetyClass.SFW;
}
