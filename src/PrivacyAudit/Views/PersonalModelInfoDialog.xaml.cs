using System.Windows;

namespace PrivacyAudit;

public partial class PersonalModelInfoDialog : System.Windows.Controls.UserControl
{
    public PersonalModelInfoDialog()
    {
        InitializeComponent();
    }

    public event EventHandler? Completed;
    void Close_Click(object sender, RoutedEventArgs e) => Completed?.Invoke(this, EventArgs.Empty);
}
