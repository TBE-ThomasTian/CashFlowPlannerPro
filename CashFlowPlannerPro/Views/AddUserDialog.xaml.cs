using System.Windows;
using System.Windows.Input;
using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Services;

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
        CreateBtn.ToolTip = TooltipService.Get("Btn_Create");
        CancelBtn.ToolTip = TooltipService.Get("Btn_Cancel");
        TbUsername.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Username))
        {
            ModernMessageBox.ShowError("Benutzername darf nicht leer sein.", "Fehler");
            return;
        }
        if (string.Equals(Username, "admin", StringComparison.OrdinalIgnoreCase))
        {
            ModernMessageBox.ShowError(
                "Der Benutzername \"admin\" ist für das Systemkonto reserviert. Bitte wählen Sie einen anderen Benutzernamen.",
                "Benutzername reserviert");
            return;
        }
        if (Database.Instance.GetUsernames().Any(existing =>
                string.Equals(existing, Username, StringComparison.OrdinalIgnoreCase)))
        {
            ModernMessageBox.ShowError(
                $"Der Benutzername \"{Username}\" ist bereits vergeben.",
                "Benutzer bereits vorhanden");
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

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) DialogResult = false;
    }
}
