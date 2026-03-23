using System.Windows;

namespace CashFlowPlannerPro.Views;

public partial class AddUserDialog : Window
{
    public string Username => TbUsername.Text.Trim();
    public string FullName => TbFullName.Text.Trim();
    public string Password => PbPassword.Password;

    public AddUserDialog()
    {
        InitializeComponent();
        if (Application.Current?.MainWindow != null) Owner = Application.Current.MainWindow;
        TbUsername.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Username))
        {
            ModernMessageBox.ShowError("Benutzername darf nicht leer sein.", "Fehler");
            return;
        }
        if (string.IsNullOrWhiteSpace(Password))
        {
            ModernMessageBox.ShowError("Passwort darf nicht leer sein.", "Fehler");
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
