using System.Windows;
using System.Windows.Input;

namespace CashFlowPlannerPro.Views;

public partial class NewUserDialog : Window
{
    public bool Saved { get; private set; }
    public string Username => TbUsername.Text.Trim();
    public string FullName => TbFullName.Text.Trim();
    public string Password => PbPassword.Password;

    public NewUserDialog()
    {
        InitializeComponent();
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TbUsername.Text))
        {
            ModernMessageBox.ShowError("Bitte geben Sie einen Benutzernamen ein.", "Pflichtfeld");
            return;
        }
        if (string.IsNullOrEmpty(PbPassword.Password))
        {
            ModernMessageBox.ShowError("Bitte geben Sie ein Passwort ein.", "Pflichtfeld");
            return;
        }
        if (PbPassword.Password != PbConfirm.Password)
        {
            ModernMessageBox.ShowError("Die Passwörter stimmen nicht überein.", "Passwort");
            return;
        }
        Saved = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
        if (e.Key == Key.Enter) Create_Click(sender, e);
    }
}
