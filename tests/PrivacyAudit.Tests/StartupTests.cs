namespace PrivacyAudit.Tests;

using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using PrivacyAudit.Core;

public sealed class StartupTests
{
    [Fact]
    public void OversizedWindowStartsAtWorkAreaTopLeft()
    {
        var result = WindowPlacement.InitialPosition(1380, 860, new WindowArea(0, 0, 1280, 720));
        Assert.Equal(new WindowPosition(0, 0), result);
    }

    [Fact]
    public void WindowThatFitsStartsCenteredInsideOffsetWorkArea()
    {
        var result = WindowPlacement.InitialPosition(1000, 700, new WindowArea(100, 40, 1600, 900));
        Assert.Equal(new WindowPosition(400, 140), result);
    }

    [Fact]
    public void StartupCheckExplainsMissingMediaFoundation()
    {
        var issues = StartupPrerequisiteChecker.CheckLibraries(name => name != "mfreadwrite.dll");
        Assert.Contains(issues, x => x.Code == "media_foundation_missing");
    }

    [Fact]
    public void MainWindow_LoadsAndClosesWithoutUnhandledException()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App(); app.InitializeComponent();
                var window = new MainWindow(true);
                var mediaFilter = Assert.IsType<ComboBox>(window.FindName("MediaPeopleFilter"));
                Assert.Equal("AllMedia", Assert.IsType<ComboBoxItem>(mediaFilter.Items[0]).Tag);
                Assert.Equal(0, mediaFilter.SelectedIndex);
                Assert.True(Assert.IsType<CheckBox>(window.FindName("MediaImagesFilterCheckBox")).IsChecked);
                Assert.True(Assert.IsType<CheckBox>(window.FindName("MediaVideosFilterCheckBox")).IsChecked);
                Assert.True(Assert.IsType<CheckBox>(window.FindName("PeopleImagesScopeCheckBox")).IsChecked);
                Assert.False(Assert.IsType<CheckBox>(window.FindName("PeopleVideosScopeCheckBox")).IsChecked);
                var historyList = Assert.IsType<System.Windows.Controls.ItemsControl>(window.FindName("ApplicationHistoryList"));
                historyList.ItemsSource = new[]
                {
                    new PrivacyAudit.Core.ApplicationHistoryApplication(
                        new("9839aec31243a928", "Microsoft Excel", PrivacyAudit.Core.ApplicationIdentityConfidence.Known),
                        [new(@"C:\Documents\clients.xlsx", DateTime.Now, 7, false, true, "test", "Automatic")], 1, 0)
                };
                window.Show(); historyList.UpdateLayout();

                var setRestoreReadOnly = typeof(MainWindow).GetMethod("SetRestoreReadOnly", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var setPriorityInteractions = typeof(MainWindow).GetMethod("SetPriorityAuditInteractions", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var startOver = Assert.IsType<Button>(window.FindName("StartOverButton"));
                var priorityStart = Assert.IsType<Button>(window.FindName("PriorityWizardStartButton"));
                var priorityPrepare = Assert.IsType<Button>(window.FindName("PriorityWizardPrepareButton"));
                var priorityCancelPreparation = Assert.IsType<Button>(window.FindName("PriorityWizardCancelPreparationButton"));
                Assert.NotNull(priorityPrepare.ToolTip);
                Assert.NotNull(priorityCancelPreparation.ToolTip);
                typeof(MainWindow).GetMethod("ShowPriorityWizardPreparingState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                Assert.Equal(Visibility.Visible, priorityCancelPreparation.Visibility);
                Assert.Equal(Visibility.Collapsed, priorityStart.Visibility);
                typeof(MainWindow).GetMethod("ShowPriorityWizardPreparationAvailableState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [true]);
                Assert.Equal(Visibility.Visible, priorityPrepare.Visibility);
                Assert.Equal(Visibility.Collapsed, priorityCancelPreparation.Visibility);
                setRestoreReadOnly.Invoke(window, [true]);
                setRestoreReadOnly.Invoke(window, [false]);
                setPriorityInteractions.Invoke(window, [false]);
                setPriorityInteractions.Invoke(window, [true]);
                Assert.True(startOver.IsHitTestVisible);
                Assert.True(priorityStart.IsHitTestVisible);

                var findingsField = typeof(MainWindow).GetField("_findings", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var findings = Assert.IsType<List<Finding>>(findingsField.GetValue(window));
                findings.Add(new Finding { Id = Guid.NewGuid(), Category = "Images", Path = @"C:\audit\photo.jpg", DisplayName = "photo.jpg" });
                typeof(MainWindow).GetMethod("RebuildCategories", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                typeof(MainWindow).GetMethod("BuildDashboard", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                var dashboard = Assert.IsType<WrapPanel>(window.FindName("DashboardPanel"));
                Assert.NotEmpty(dashboard.Children);
                Assert.IsType<Button>(dashboard.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(Assert.IsType<TabItem>(window.FindName("TabFindings")).IsSelected);
                Assert.Equal("Images", (Assert.IsType<ComboBoxItem>(Assert.IsType<ComboBox>(window.FindName("CategoryFilter")).SelectedItem)).Tag);

                window.Close();
                app.Shutdown();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "WPF startup test did not terminate.");
        Assert.Null(failure);
    }

}
