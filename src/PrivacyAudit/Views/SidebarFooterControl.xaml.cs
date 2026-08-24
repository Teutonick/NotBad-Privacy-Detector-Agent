using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using UserControl = System.Windows.Controls.UserControl;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace PrivacyAudit;

public partial class SidebarFooterControl : UserControl
{
    const string GitHubRepoUrl = "https://github.com/Teutonick/NotBad-Privacy-Detector-Agent";
    DispatcherTimer? _promoTimer;
    int _currentProjectIndex;
    int _currentRecommendationIndex;
    bool _isMouseOverPromo;

    public static event Action<bool, string>? BusyChanged;

    public static void SetGlobalBusy(bool isBusy, string text = "")
    {
        BusyChanged?.Invoke(isBusy, text);
    }

    public SidebarFooterControl()
    {
        InitializeComponent();
    }

    void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        BusyChanged += OnGlobalBusyChanged;
        UpdateAuthorPromoUI();
        UpdateRecommendationUI();
        UpdateLanguageUI();
        StartPromoTimer();
    }

    void UserControl_Unloaded(object sender, RoutedEventArgs e)
    {
        BusyChanged -= OnGlobalBusyChanged;
        _promoTimer?.Stop();
        _promoTimer = null;
    }

    void OnGlobalBusyChanged(bool isBusy, string text)
    {
        Dispatcher.InvokeAsync(() =>
        {
            bdrGlobalBusy.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
            lblGlobalBusyText.Text = string.IsNullOrWhiteSpace(text)
                ? LocalizationService.Get("GlobalProcessRunning")
                : text;
        });
    }

    void StartPromoTimer()
    {
        _promoTimer?.Stop();
        _promoTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(10)
        };
        _promoTimer.Tick += PromoTimer_Tick;
        _promoTimer.Start();
    }

    void PromoTimer_Tick(object? sender, EventArgs e)
    {
        if (Window.GetWindow(this) is not MainWindow window) return;
        if (!_isMouseOverPromo && (window is null || (window.IsActive && window.IsVisible && window.WindowState != WindowState.Minimized)))
        {
            if (AuthorProjectsConfig.Projects.Length > 1)
            {
                _currentProjectIndex = (_currentProjectIndex + 1) % AuthorProjectsConfig.Projects.Length;
                UpdateAuthorPromoUI();
            }
            _currentRecommendationIndex = (_currentRecommendationIndex + 1) % 6;
            UpdateRecommendationUI();
        }
    }

    void UpdateRecommendationUI()
    {
        lblRecommendationText.Text = LocalizationService.Get($"Recommendation{_currentRecommendationIndex + 1}");
    }

    void UpdateLanguageUI()
    {
        btnLanguage.Content = LocalizationService.CurrentLanguageCode;
        btnLanguage.ToolTip = LocalizationService.Get("LanguageSwitchTooltip");
    }

    public void UpdateAuthorPromoUI()
    {
        if (AuthorProjectsConfig.Projects.Length == 0)
        {
            bdrAuthorPromo.Visibility = Visibility.Collapsed;
            return;
        }

        bdrAuthorPromo.Visibility = Visibility.Visible;
        if (_currentProjectIndex < 0 || _currentProjectIndex >= AuthorProjectsConfig.Projects.Length)
            _currentProjectIndex = 0;

        var project = AuthorProjectsConfig.Projects[_currentProjectIndex];
        var isRu = LocalizationService.IsRussian();
        var lang = isRu ? "ru" : "en";

        lblAuthorPromoCounter.Text = $" ({_currentProjectIndex + 1}/{AuthorProjectsConfig.Projects.Length})";
        lblAuthorPromoIcon.Text = project.Icon;
        lblAuthorPromoTitle.Text = project.GetTitle(lang);
        lblAuthorPromoDesc.Text = project.GetDescription(lang);

        var shortLink = project.Url;
        if (shortLink.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) shortLink = shortLink[8..];
        else if (shortLink.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) shortLink = shortLink[7..];
        if (shortLink.EndsWith('/')) shortLink = shortLink[..^1];
        lblAuthorPromoLink.Text = shortLink;

        bdrAuthorPromo.ToolTip = $"{project.GetTitle(lang)}\n{project.Url}\n{LocalizationService.Get("AuthorProjectsTooltip")}";
    }

    void BdrAuthorPromo_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_currentProjectIndex >= 0 && _currentProjectIndex < AuthorProjectsConfig.Projects.Length)
        {
            var project = AuthorProjectsConfig.Projects[_currentProjectIndex];
            if (!string.IsNullOrWhiteSpace(project.Url))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(project.Url) { UseShellExecute = true });
                }
                catch { }
            }
        }
    }

    void BdrAuthorPromo_MouseEnter(object sender, MouseEventArgs e) => _isMouseOverPromo = true;
    void BdrAuthorPromo_MouseLeave(object sender, MouseEventArgs e) => _isMouseOverPromo = false;

    void BtnAbout_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = btnAbout,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Top,
            Focusable = false,
            HasDropShadow = true,
            Padding = new Thickness(6),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(15, 20, 30)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 52, 75)),
            BorderThickness = new Thickness(1)
        };

        if (Window.GetWindow(this) is not MainWindow window) return;

        var itemLegal = CreateMenuItem("AboutLegalTitle", "AboutLegalSub", "⚖️");
        itemLegal.Click += (_, _) => window.ShowDocumentModal("Docs/DISCLAIMER.md", LocalizationService.Get("AboutLegalTitle"), "⚖️");

        var itemPrivacy = CreateMenuItem("AboutPrivacyTitle", "AboutPrivacySub", "🔒");
        itemPrivacy.Click += (_, _) => window.ShowDocumentModal("Docs/PRIVACY.md", LocalizationService.Get("AboutPrivacyTitle"), "🔒");

        var itemThirdParty = CreateMenuItem("AboutThirdPartyTitle", "AboutThirdPartySub", "📜");
        itemThirdParty.Click += (_, _) => window.ShowDocumentModal("Docs/THIRD_PARTY_NOTICES.md", LocalizationService.Get("AboutThirdPartyTitle"), "📜");

        var itemGitHub = CreateMenuItem("AboutGithubTitle", "AboutGithubSub", "🌐");
        itemGitHub.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(GitHubRepoUrl) { UseShellExecute = true }); } catch { }
        };

        menu.Items.Add(itemLegal);
        menu.Items.Add(itemPrivacy);
        menu.Items.Add(itemThirdParty);
        menu.Items.Add(new Separator { Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(36, 255, 255, 255)), Margin = new Thickness(4, 3, 4, 3) });
        menu.Items.Add(itemGitHub);

        menu.IsOpen = true;
    }

    void BtnLanguage_Click(object sender, RoutedEventArgs e)
    {
        var nextLanguage = LocalizationService.CurrentLanguageCode == "RU" ? "EN" : "RU";
        var prompt = string.Format(LocalizationService.Get("LanguageRestartPrompt"), nextLanguage);
        if (System.Windows.MessageBox.Show(
                prompt,
                LocalizationService.Get("AppTitle"),
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question,
                System.Windows.MessageBoxResult.No) != System.Windows.MessageBoxResult.Yes) return;

        if (!LocalizationService.SetLanguage(nextLanguage)) return;
        if (System.Windows.Application.Current is App app)
        {
            app.RequestRestart();
        }
        else
        {
            System.Windows.MessageBox.Show(LocalizationService.Get("LanguageRestartFailed"), LocalizationService.Get("AppTitle"), System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    void BtnCleanup_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is not MainWindow window) return;
        window.ShowCleanupModal();
    }

    static MenuItem CreateMenuItem(string titleKey, string subKey, string icon)
    {
        var title = LocalizationService.Get(titleKey);
        var sub = LocalizationService.Get(subKey);

        var panel = new StackPanel { Margin = new Thickness(4, 2, 8, 2) };
        var titleRow = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        titleRow.Children.Add(new TextBlock { Text = $"{icon} ", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        titleRow.Children.Add(new TextBlock { Text = title, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.White });
        panel.Children.Add(titleRow);
        panel.Children.Add(new TextBlock { Text = sub, FontSize = 9.5, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(130, 142, 160)), Margin = new Thickness(18, 1, 0, 0) });

        return new MenuItem
        {
            Header = panel,
            Padding = new Thickness(8, 6, 8, 6),
            BorderThickness = new Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent,
            FocusVisualStyle = null
        };
    }
}
