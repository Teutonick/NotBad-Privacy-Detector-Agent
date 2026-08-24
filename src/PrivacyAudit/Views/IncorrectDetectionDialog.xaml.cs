using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using PrivacyAudit.Core;
using UserControl = System.Windows.Controls.UserControl;

namespace PrivacyAudit;

public partial class IncorrectDetectionDialog : UserControl
{
    const string NewIssueUrl = "https://github.com/Teutonick/NotBad-Privacy-Detector-Agent/issues/new";
    readonly Finding _finding;
    DiagnosticIssueReport? _report;
    public event EventHandler? Completed;

    public IncorrectDetectionDialog(Finding finding)
    {
        _finding = finding;
        InitializeComponent();
    }

    void Prepare_Click(object sender, RoutedEventArgs e)
    {
        var correction = CorrectionBox.SelectedIndex switch
        {
            0 => "Wrong finding",
            1 => "Wrong file origin",
            2 => "Wrong risk level",
            _ => "Other inaccuracy"
        };
        var explanation = UserExplanationBox.Text.Trim();
        if (explanation.Length is < DiagnosticReportBuilder.MinUserExplanationLength or > DiagnosticReportBuilder.MaxUserExplanationLength)
        {
            UserExplanationValidationText.Visibility = Visibility.Visible;
            UserExplanationBox.Focus();
            return;
        }
        UserExplanationValidationText.Visibility = Visibility.Collapsed;
        _report = DiagnosticReportBuilder.Build(_finding, correction, userExplanation: explanation);
        PreviewText.Text = BuildPreview(_report);
        IntroPanel.Visibility = Visibility.Collapsed;
        PreviewPanel.Visibility = Visibility.Visible;
    }

    static string BuildPreview(DiagnosticIssueReport report) =>
        $"{report.Body}\n" +
        "Included:\n✓ sanitized path shape\n✓ filename extension\n✓ directory structure\n✓ categorical scanner results\n✓ provenance category\n✓ scanner / rule IDs\n✓ application metadata\n✓ Windows version\n\n" +
        "Not included:\n✗ file contents\n✗ exact path, size, or timestamps\n✗ detected passwords or tokens\n✗ PII values\n✗ GPS coordinates\n✗ Windows username\n✗ hostname\n✗ raw scanner metadata";

    void OpenGithub_Click(object sender, RoutedEventArgs e)
    {
        if (_report is null) return;
        var labels = string.Join(",", _report.Labels);
        var url = $"{NewIssueUrl}?title={Uri.EscapeDataString(_report.Title)}&body={Uri.EscapeDataString(_report.Body)}&labels={Uri.EscapeDataString(labels)}";
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { return; }
        Completed?.Invoke(this, EventArgs.Empty);
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => Completed?.Invoke(this, EventArgs.Empty);
}
