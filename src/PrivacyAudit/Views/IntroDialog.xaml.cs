using System.Windows;
using System.Windows.Media.Imaging;

namespace PrivacyAudit;

public partial class IntroDialog : System.Windows.Controls.UserControl
{
    int _slide = 1;
    public bool DoNotShow => DoNotShowAgain.IsChecked == true;
    public event EventHandler? Completed;

    public IntroDialog()
    {
        InitializeComponent();
        UpdateSlide();
    }

    void UpdateSlide()
    {
        SlideTitle.Text = LocalizationService.Get($"IntroSlide{_slide}Title");
        SlideBody.Text = LocalizationService.Get($"IntroSlide{_slide}Body");
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri($"pack://application:,,,/Assets/intro-slide-{_slide}.jpg", UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            SlideBackground.Source = image;
        }
        catch
        {
            // A missing optional illustration must never prevent the application from starting.
            SlideBackground.Source = null;
        }
        BackButton.IsEnabled = _slide > 1;
        NextButton.Visibility = _slide < 3 ? Visibility.Visible : Visibility.Collapsed;
        OkButton.Content = LocalizationService.Get(_slide == 3 ? "IntroStart" : "Close");
    }

    void Back_Click(object sender, RoutedEventArgs e) { if (_slide > 1) { _slide--; UpdateSlide(); } }
    void Next_Click(object sender, RoutedEventArgs e) { if (_slide < 3) { _slide++; UpdateSlide(); } }
    void Ok_Click(object sender, RoutedEventArgs e) { Completed?.Invoke(this, EventArgs.Empty); }
}
