using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PrivacyAudit.PeopleDetection;

public sealed record FaceDetectionResult(int FaceCount, double MaxConfidence);

public sealed class YuNetDetector : IDisposable
{
    readonly InferenceSession _session;
    readonly string _inputName;
    readonly int _inputWidth;
    readonly int _inputHeight;
    readonly object _gate = new();
    bool _disposed;

    public YuNetDetector(string modelPath)
    {
        // Load the small model into memory before constructing the session. This
        // prevents ONNX Runtime from keeping the model file mapped/locked, so the
        // user can remove the optional model after a scan has finished.
        var modelBytes = File.ReadAllBytes(modelPath);
        _session = new InferenceSession(modelBytes, new SessionOptions { IntraOpNumThreads = 1, InterOpNumThreads = 1, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL });
        _inputName = _session.InputMetadata.Keys.Single();
        var dimensions = _session.InputMetadata[_inputName].Dimensions.ToArray();
        _inputHeight = dimensions.Length >= 4 && dimensions[dimensions.Length - 2] > 0 ? dimensions[dimensions.Length - 2] : 320;
        _inputWidth = dimensions.Length >= 4 && dimensions[dimensions.Length - 1] > 0 ? dimensions[dimensions.Length - 1] : 320;
    }

    public FaceDetectionResult Detect(string imagePath, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        using var image = SixLabors.ImageSharp.Image.Load<Rgb24>(new DecoderOptions { MaxFrames = 1 }, imagePath);
        image.Mutate(context => context.Resize(_inputWidth, _inputHeight));
        var tensor = ToBgrTensor(image, cancellationToken);
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_inputName, tensor)]);
            var outputs = results.ToDictionary(x => x.Name, x => x.AsTensor<float>(), StringComparer.OrdinalIgnoreCase);
            return DecodeYuNetOutputs(outputs);
        }
    }

    FaceDetectionResult DecodeYuNetOutputs(IReadOnlyDictionary<string, Microsoft.ML.OnnxRuntime.Tensors.Tensor<float>> outputs)
    {
        var candidates = new List<DetectionBox>();
        foreach (var stride in new[] { 8, 16, 32 })
        {
            if (!outputs.TryGetValue($"cls_{stride}", out var cls) || !outputs.TryGetValue($"obj_{stride}", out var obj) || !outputs.TryGetValue($"bbox_{stride}", out var bbox))
                throw new InvalidDataException("The YuNet model is missing a detection output.");
            var count = Math.Min(cls.Length, obj.Length);
            count = Math.Min(count, bbox.Length / 4);
            var columns = Math.Max(1, _inputWidth / stride);
            for (var index = 0; index < count; index++)
            {
                var score = Math.Sqrt(Math.Clamp(cls.GetValue(index), 0f, 1f) * Math.Clamp(obj.GetValue(index), 0f, 1f));
                if (score < 0.6) continue;
                var row = index / columns;
                var column = index % columns;
                var centerX = (column + bbox.GetValue(index * 4)) * stride;
                var centerY = (row + bbox.GetValue(index * 4 + 1)) * stride;
                var width = MathF.Exp(bbox.GetValue(index * 4 + 2)) * stride;
                var height = MathF.Exp(bbox.GetValue(index * 4 + 3)) * stride;
                candidates.Add(new(centerX - width / 2, centerY - height / 2, width, height, score));
            }
        }

        var kept = new List<DetectionBox>();
        foreach (var candidate in candidates.OrderByDescending(x => x.Score).Take(5000))
        {
            if (kept.All(existing => IoU(existing, candidate) < 0.3)) kept.Add(candidate);
        }
        return new(kept.Count, kept.Count == 0 ? 0 : kept.Max(x => x.Score));
    }

    static double IoU(DetectionBox left, DetectionBox right)
    {
        var x1 = Math.Max(left.X, right.X);
        var y1 = Math.Max(left.Y, right.Y);
        var x2 = Math.Min(left.X + left.Width, right.X + right.Width);
        var y2 = Math.Min(left.Y + left.Height, right.Y + right.Height);
        var intersection = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
        var union = left.Width * left.Height + right.Width * right.Height - intersection;
        return union <= 0 ? 0 : intersection / union;
    }

    readonly record struct DetectionBox(double X, double Y, double Width, double Height, double Score);

    static DenseTensor<float> ToBgrTensor(SixLabors.ImageSharp.Image<Rgb24> image, CancellationToken cancellationToken)
    {
        var tensor = new DenseTensor<float>([1, 3, image.Height, image.Width]);
        for (var y = 0; y < image.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = image.Frames.RootFrame.DangerousGetPixelRowMemory(y).Span;
            for (var x = 0; x < image.Width; x++)
            {
                tensor[0, 0, y, x] = row[x].B;
                tensor[0, 1, y, x] = row[x].G;
                tensor[0, 2, y, x] = row[x].R;
            }
        }
        return tensor;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.Dispose();
    }
}
