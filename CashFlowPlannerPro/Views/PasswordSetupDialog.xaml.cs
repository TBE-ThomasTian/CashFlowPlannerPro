using System.Windows;
using System.Windows.Input;
using CashFlowPlannerPro.Services;

namespace CashFlowPlannerPro.Views;

/// <summary>
/// Collects a new password twice. Password values are intentionally never
/// trimmed because leading and trailing spaces may be part of a passphrase.
/// </summary>
public partial class PasswordSetupDialog : Window
{
    private readonly string _username;

    public PasswordSetupDialog(string username)
    {
        _username = username;
        InitializeComponent();
        Title = LocalizationManager.Get("NewPasswordDialogTitle");
        TitleText.Text = Title;
        HintText.Text = string.Format(
            LocalizationManager.Get("PasswordPolicyHint"),
            PasswordPolicy.MinimumLength);
        NewPasswordLabel.Text = LocalizationManager.Get("NewPasswordDialogLabel");
        ConfirmPasswordLabel.Text = LocalizationManager.Get("ConfirmPasswordLabel");
        CancelButton.Content = LocalizationManager.Get("Cancel");
        SaveButton.Content = LocalizationManager.Get("Save");
        NewPasswordBox.Focus();
    }

    public string Password => NewPasswordBox.Password;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var password = NewPasswordBox.Password;
        if (!PasswordPolicy.TryValidate(password, _username, out var error))
        {
            ModernMessageBox.ShowError(error, LocalizationManager.Get("PasswordTitle"));
            return;
        }

        if (!string.Equals(password, ConfirmPasswordBox.Password, StringComparison.Ordinal))
        {
            ModernMessageBox.ShowError(
                LocalizationManager.Get("PasswordsDoNotMatch"),
                LocalizationManager.Get("PasswordTitle"));
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            DialogResult = false;
    }
}
