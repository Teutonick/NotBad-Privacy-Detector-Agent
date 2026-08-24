using System.Windows;
using System.Windows.Threading;

namespace PrivacyAudit;

public enum CleanupChoice { DeleteAll, SecondaryOnly }

public partial class CleanupDialog : System.Windows.Controls.UserControl
{
    readonly DispatcherTimer _timer;
    int _allSeconds = 30;
    int _secondarySeconds = 15;
    public CleanupChoice? Choice { get; private set; }
    public event EventHandler? Completed;

    public CleanupDialog()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += Timer_Tick;
        UpdateButtons();
        _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    void Timer_Tick(object? sender, EventArgs e)
    {
        if (_allSeconds > 0) _allSeconds--;
        if (_secondarySeconds > 0) _secondarySeconds--;
        UpdateButtons();
        if (_allSeconds == 0 && _secondarySeconds == 0) _timer.Stop();
    }

    void UpdateButtons()
    {
        btnDeleteAll.IsEnabled = _allSeconds == 0;
        btnDeleteAll.Content = _allSeconds > 0
            ? string.Format(LocalizationService.Get("CleanupAllCountdown"), _allSeconds)
            : LocalizationService.Get("CleanupAllConfirm");
        btnSecondary.IsEnabled = _secondarySeconds == 0;
        btnSecondary.Content = _secondarySeconds > 0
            ? string.Format(LocalizationService.Get("CleanupSecondaryCountdown"), _secondarySeconds)
            : LocalizationService.Get("CleanupSecondaryConfirm");
    }

    void DeleteAll_Click(object sender, RoutedEventArgs e) { Choice = CleanupChoice.DeleteAll; Completed?.Invoke(this, EventArgs.Empty); }
    void Secondary_Click(object sender, RoutedEventArgs e) { Choice = CleanupChoice.SecondaryOnly; Completed?.Invoke(this, EventArgs.Empty); }
    void Cancel_Click(object sender, RoutedEventArgs e) { Choice = null; Completed?.Invoke(this, EventArgs.Empty); }
}
