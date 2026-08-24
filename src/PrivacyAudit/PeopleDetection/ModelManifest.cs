using System.Text.Json.Serialization;

namespace PrivacyAudit.PeopleDetection;

public sealed record ModelManifest(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("license")] string License,
    [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("license_url")] string LicenseUrl)
{
    public string ModelVersion => $"{Id}-{Version}";

    // The URL and digest are intentionally source-controlled values. They must not
    // be obtained from a remote API at runtime.
    public static ModelManifest YuNet2026May { get; } = new(
        "yunet",
        "2026may",
        "MIT",
        "face_detection_yunet_2026may.onnx",
        "https://github.com/opencv/opencv_zoo/raw/refs/heads/main/models/face_detection_yunet/face_detection_yunet_2026may.onnx",
        "EBAFCE4E3C118D6554634BE5C27AB333B4C047A9A8C3FAF1D7CF93101C22F0F0",
        "https://github.com/opencv/opencv_zoo/raw/refs/heads/main/models/face_detection_yunet/LICENSE");
}
