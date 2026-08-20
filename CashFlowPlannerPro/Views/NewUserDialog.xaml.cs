using System.Windows;
using System.Windows.Input;
using CashFlowPlannerPro.Services;

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
        CreateBtn.ToolTip = TooltipService.Get("Btn_Create");
        CancelBtn.ToolTip = TooltipService.Get("Btn_Cancel");
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TbUsername.Text))
        {
            ModernMessageBox.ShowError("Bitte geben Sie einen Benutzernamen ein.", "Pflichtfeld");
            return;
        }
        if (string.Equals(Username, "admin", StringComparison.OrdinalIgnoreCase))
        {
            ModernMessageBox.ShowError(
                LocalizationManager.Get("ReservedAdminUsername"),
                LocalizationManager.Get("AppErrorTitle"));
            return;
        }
        if (!PasswordPolicy.TryValidate(PbPassword.Password, Username, out var passwordError))
        {
            ModernMessageBox.ShowError(passwordError, LocalizationManager.Get("PasswordTitle"));
            return;
        }
        if (!string.Equals(PbPassword.Password, PbConfirm.Password, StringComparison.Ordinal))
        {
            ModernMessageBox.ShowError(
                LocalizationManager.Get("PasswordsDoNotMatch"),
                LocalizationManager.Get("PasswordTitle"));
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
