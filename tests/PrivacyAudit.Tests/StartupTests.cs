namespace PrivacyAudit.Tests;

using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using PrivacyAudit.Core;

public sealed class StartupTests
{
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
