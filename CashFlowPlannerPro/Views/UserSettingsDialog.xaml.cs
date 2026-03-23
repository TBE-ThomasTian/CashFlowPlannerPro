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

        // Show admin panel only for admin user
        if (currentUser.ToLower() == "admin")
        {
            AdminPanel.Visibility = Visibility.Visible;
            RefreshUserList();
        }

        Owner = Application.Current.MainWindow?.IsVisible == true ? Application.Current.MainWindow : null;
    }

    private void RefreshUserList()
    {
        UserList.Items.Clear();
        foreach (var user in Database.Instance.GetUsernames())
            UserList.Items.Add(user);
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

    private void AddUser_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new NewUserDialog();
        dlg.Owner = this;
        dlg.ShowDialog();
        if (dlg.Saved)
        {
            try
            {
                Database.Instance.AddUser(dlg.Username, dlg.Password, dlg.FullName);
                RefreshUserList();
            }
            catch (Exception ex)
            {
                ModernMessageBox.ShowError($"Benutzer konnte nicht erstellt werden:\n{ex.Message}", "Fehler");
            }
        }
    }

    private void DeleteUser_Click(object sender, RoutedEventArgs e)
    {
        var selected = UserList.SelectedItem as string;
        if (string.IsNullOrEmpty(selected))
        {
            ModernMessageBox.ShowError("Bitte wählen Sie einen Benutzer aus.", "Löschen");
            return;
        }
        if (selected == "admin")
        {
            ModernMessageBox.ShowError("Der Admin-Benutzer kann nicht gelöscht werden.", "Löschen");
            return;
        }
        if (ModernMessageBox.ShowConfirm($"Benutzer \"{selected}\" wirklich löschen?", "Benutzer löschen"))
        {
            Database.Instance.DeleteUser(selected);
            RefreshUserList();
        }
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
