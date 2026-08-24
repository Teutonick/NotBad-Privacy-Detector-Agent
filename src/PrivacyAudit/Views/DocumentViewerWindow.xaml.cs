using System.IO;
using System.Reflection;
using System.Windows;

namespace PrivacyAudit;

/// <summary>
/// Lightweight dark-themed read-only document viewer for offline embedded documents.
/// </summary>
public partial class DocumentViewerWindow : System.Windows.Controls.UserControl
{
    public DocumentViewerWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Opens a modal dialog with the specified embedded document.
    /// </summary>
    public void LoadDocument(string resourceKey, string title, string icon, string subtitle = "NotBad Privacy Detector Agent")
    {
        lblDocTitle.Text = title;
        lblDocIcon.Text = icon;
        lblDocSubtitle.Text = subtitle;
        txtContent.Text = LoadEmbeddedText(resourceKey);
    }

    public static string LoadEmbeddedText(string resourceKey)
    {
        var assembly = typeof(DocumentViewerWindow).Assembly;
        var normalizedKey = resourceKey.Replace('/', '.').Replace('\\', '.');
        using var stream = assembly.GetManifestResourceStream(normalizedKey);
        if (stream is null)
        {
            var names = assembly.GetManifestResourceNames();
            var match = names.FirstOrDefault(n =>
                n.Equals(normalizedKey, StringComparison.OrdinalIgnoreCase) ||
                n.EndsWith(normalizedKey, StringComparison.OrdinalIgnoreCase) ||
                n.EndsWith(Path.GetFileName(resourceKey), StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                using var fallbackStream = assembly.GetManifestResourceStream(match);
                if (fallbackStream is not null)
                {
                    using var fallbackReader = new StreamReader(fallbackStream, System.Text.Encoding.UTF8);
                    return fallbackReader.ReadToEnd();
                }
            }
            throw new InvalidOperationException($"Embedded resource not found: {resourceKey}. Available: {string.Join(", ", names)}");
        }

        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public event EventHandler? Closed;
    void BtnClose_Click(object sender, RoutedEventArgs e) => Closed?.Invoke(this, EventArgs.Empty);
}
