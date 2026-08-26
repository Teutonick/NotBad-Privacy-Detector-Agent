using System.Text.Json.Serialization;

namespace PrivacyAudit.PeopleDetection;

public sealed record ModelManifest(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("license")] string License,
    [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("license_url")] string LicenseUrl,
    [property: JsonPropertyName("package_directory")] string PackageDirectory = "YuNet",
    [property: JsonPropertyName("display_name")] string DisplayName = "YuNet",
    [property: JsonPropertyName("source")] string Source = "opencv/opencv_zoo",
    [property: JsonPropertyName("mirror_url")] string MirrorUrl = "")
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
        "https://github.com/opencv/opencv_zoo/raw/refs/heads/main/models/face_detection_yunet/LICENSE",
        "YuNet", "YuNet", "opencv/opencv_zoo",
        "https://raw.githubusercontent.com/Teutonick/InfoSec-AUDIT-LOCAL/main/third_party/YuNet/face_detection_yunet_2026may.onnx");

    public static ModelManifest ImageSafetyXs { get; } = new(
        "image-safety-classifier-xs",
        "606ad3d",
        "MIT",
        "image-safety-classifier-xs.onnx",
        "https://huggingface.co/OwenElliott/image-safety-classifier-xs/resolve/606ad3dfd6a023215e3ab0797040437cc365977b/onnx/image-safety-classifier-xs.onnx",
        "8C28C49D9075F3AD15EBDC2961F02D5B3F99BE944815B848B49C9F0E6F3FB689",
        "",
        "ImageSafety",
        "Image Safety Classifier XS",
        "OwenElliott/image-safety-classifier-xs",
        "https://raw.githubusercontent.com/Teutonick/InfoSec-AUDIT-LOCAL/main/third_party/ImageSafety/image-safety-classifier-xs.onnx");
}
