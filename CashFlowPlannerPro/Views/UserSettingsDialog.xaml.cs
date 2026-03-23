using System.Windows;
using System.Windows.Input;
using CashFlowPlannerPro.Data;

namespace CashFlowPlannerPro.Views;

public partial class UserSettingsDialog : Window
{
    private readonly string _currentUser;

    public UserSettingsDialog(string currentUser)
    {
        InitializeComponent();
        _currentUser = currentUser;
        TbCurrentUser.Text = currentUser;
        TbFullName.Text = Database.Instance.GetFullName(currentUser) ?? "";
        Owner = Application.Current.MainWindow?.IsVisible == true ? Application.Current.MainWindow : null;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Save full name
        var fullName = TbFullName.Text.Trim();
        if (!string.IsNullOrEmpty(fullName))
            Database.Instance.UpdateUserFullName(_currentUser, fullName);

        // Change password if fields are filled
        var oldPw = PbOldPassword.Password;
        var newPw = PbNewPassword.Password;
        var confirmPw = PbConfirmPassword.Password;

        if (!string.IsNullOrEmpty(newPw) || !string.IsNullOrEmpty(oldPw))
        {
            if (string.IsNullOrEmpty(oldPw))
            {
                ModernMessageBox.ShowError("Bitte geben Sie Ihr aktuelles Passwort ein.", "Passwort");
                return;
            }
            if (!Database.Instance.ValidateUser(_currentUser, oldPw))
            {
                ModernMessageBox.ShowError("Das aktuelle Passwort ist falsch.", "Passwort");
                return;
            }
            if (string.IsNullOrEmpty(newPw))
            {
                ModernMessageBox.ShowError("Bitte geben Sie ein neues Passwort ein.", "Passwort");
                return;
            }
            if (newPw != confirmPw)
            {
                ModernMessageBox.ShowError("Die neuen Passwörter stimmen nicht überein.", "Passwort");
                return;
            }
            Database.Instance.ChangePassword(_currentUser, newPw);
            ModernMessageBox.ShowSuccess("Passwort wurde erfolgreich geändert.", "Passwort");
        }
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    public static void Show(string currentUser)
    {
        new UserSettingsDialog(currentUser).ShowDialog();
    }
}
