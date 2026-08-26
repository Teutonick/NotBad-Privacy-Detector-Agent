using System.Text.Json.Serialization;

namespace PrivacyAudit.PeopleDetection;

/// <summary>
/// Describes a single downloadable ONNX model. All fields are source-controlled
/// constants; none may be fetched from a remote API at runtime.
/// </summary>
public sealed record ModelManifest(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("license")] string License,
    [property: JsonPropertyName("file")] string File,
    /// <summary>
    /// Primary download URL. Must be HTTPS. Must point to an immutable asset
    /// (a pinned commit hash or a GitHub Release asset — never a mutable branch
    /// like /main/ or /latest/).
    /// </summary>
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("sha256")] string Sha256,
    /// <summary>Expected byte-accurate size of the ONNX file on disk.</summary>
    [property: JsonPropertyName("expected_size")] long ExpectedSize,
    [property: JsonPropertyName("package_directory")] string PackageDirectory = "YuNet",
    [property: JsonPropertyName("display_name")] string DisplayName = "YuNet",
    [property: JsonPropertyName("source")] string Source = "opencv/opencv_zoo")
{
    public string ModelVersion => $"{Id}-{Version}";

    /// <summary>
    /// Hard cap on bytes accepted from the server: expected size plus a 5 % tolerance.
    /// Downloads that exceed this limit are aborted immediately.
    /// </summary>
    public long MaximumAllowedSize => ExpectedSize + (long)(ExpectedSize * 0.05);

    // ---------------------------------------------------------------------------
    // Supported models — pinned to immutable commit hashes from the project
    // repository so the URL-to-SHA256 binding is verifiable by anyone.
    // ---------------------------------------------------------------------------

    /// <summary>
    /// YuNet 2026-May face-detection model (opencv_zoo).
    /// Served from an immutable commit in Teutonick/InfoSec-AUDIT-LOCAL.
    /// </summary>
    public static ModelManifest YuNet2026May { get; } = new(
        "yunet",
        "2026may",
        "MIT",
        "face_detection_yunet_2026may.onnx",
        // Immutable: pinned to commit 4099a86e4641006f8795be81d2bce4ad93f8bb54
        // (the commit that added this exact model file to the repository)
        "https://raw.githubusercontent.com/Teutonick/InfoSec-AUDIT-LOCAL/4099a86e4641006f8795be81d2bce4ad93f8bb54/third_party/YuNet/face_detection_yunet_2026may.onnx",
        "EBAFCE4E3C118D6554634BE5C27AB333B4C047A9A8C3FAF1D7CF93101C22F0F0",
        229_738L,
        "YuNet", "YuNet", "opencv/opencv_zoo");

    /// <summary>
    /// Image Safety Classifier XS (OwenElliott/image-safety-classifier-xs).
    /// Served from an immutable commit in Teutonick/InfoSec-AUDIT-LOCAL,
    /// identically to YuNet (same commit 4099a86e).
    /// </summary>
    public static ModelManifest ImageSafetyXs { get; } = new(
        "image-safety-classifier-xs",
        "606ad3d",
        "MIT",
        "image-safety-classifier-xs.onnx",
        // Immutable: pinned to commit 4099a86e4641006f8795be81d2bce4ad93f8bb54
        // (the commit that added this exact model file to the repository)
        "https://raw.githubusercontent.com/Teutonick/InfoSec-AUDIT-LOCAL/4099a86e4641006f8795be81d2bce4ad93f8bb54/third_party/ImageSafety/image-safety-classifier-xs.onnx",
        "8C28C49D9075F3AD15EBDC2961F02D5B3F99BE944815B848B49C9F0E6F3FB689",
        13_137_569L,
        "ImageSafety",
        "Image Safety Classifier XS",
        "OwenElliott/image-safety-classifier-xs");
}
