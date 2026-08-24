using SixLabors.ImageSharp;

namespace PrivacyAudit.Core;

public readonly record struct MediaImageDimensions(int Width, int Height)
{
    public long PixelCount => (long)Width * Height;
}

public static class MediaImageInfo
{
    public static bool TryReadDimensions(string path, out MediaImageDimensions dimensions)
    {
        dimensions = default;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;

        try
        {
            var info = SixLabors.ImageSharp.Image.Identify(path);
            if (info is null || info.Width <= 0 || info.Height <= 0) return false;
            dimensions = new(info.Width, info.Height);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
