namespace PrivacyAudit.Tests;

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
                window.Show(); historyList.UpdateLayout(); window.Close();
                app.Shutdown();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "WPF startup test did not terminate.");
        Assert.Null(failure);
    }

}
