using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Services;
using Microsoft.Win32;

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
        LoadAvatar();
        ApplyLocalization();
        Owner = Application.Current.MainWindow?.IsVisible == true ? Application.Current.MainWindow : null;
        SaveButton.ToolTip = TooltipService.Get("Btn_Save");
        CancelButton.ToolTip = TooltipService.Get("Btn_Cancel");
        ChangeAvatarButton.ToolTip = TooltipService.Get("Btn_ChangeAvatar");
        RemoveAvatarButton.ToolTip = TooltipService.Get("Btn_RemoveAvatar");
    }

    private void ApplyLocalization()
    {
        Title = LocalizationManager.Get("ProfileTitle");
        TitleText.Text = LocalizationManager.Get("ProfileTitle");
        LoggedInAsLabel.Text = LocalizationManager.Get("ProfileLoggedInAs");
        DisplayNameLabel.Text = LocalizationManager.Get("ProfileDisplayName");
        ChangePasswordLabel.Text = LocalizationManager.Get("ProfileChangePassword");
        CurrentPasswordLabel.Text = LocalizationManager.Get("ProfileCurrentPassword");
        NewPasswordLabel.Text = LocalizationManager.Get("ProfileNewPassword");
        ConfirmPasswordLabel.Text = LocalizationManager.Get("ProfileConfirmPassword");
        CancelButton.Content = LocalizationManager.Get("Cancel");
        SaveButton.Content = LocalizationManager.Get("Save");
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!PermissionGuard.EnsureSessionValid("profile.update"))
            return;

        if (!string.Equals(_currentUser, App.CurrentUsername, StringComparison.Ordinal))
        {
            AppLogger.Audit("profile.update.denied", _currentUser, success: false);
            ModernMessageBox.ShowError(
                LocalizationManager.Get("ProfileSessionMismatch"),
                LocalizationManager.Get("AppErrorTitle"));
            return;
        }

        var fullName = TbFullName.Text.Trim();
        var oldPw = PbOldPassword.Password;
        var newPw = PbNewPassword.Password;
        var confirmPw = PbConfirmPassword.Password;
        var changePassword = !string.IsNullOrEmpty(newPw)
            || !string.IsNullOrEmpty(oldPw)
            || !string.IsNullOrEmpty(confirmPw);

        // Validate every input before the first requested write. In particular,
        // a rejected password change must not silently persist the display name.
        if (changePassword)
        {
            if (string.IsNullOrEmpty(oldPw))
            {
                ModernMessageBox.ShowError(
                    LocalizationManager.Get("CurrentPasswordRequired"),
                    LocalizationManager.Get("PasswordTitle"));
                return;
            }
            if (string.IsNullOrEmpty(newPw))
            {
                ModernMessageBox.ShowError(
                    LocalizationManager.Get("NewPasswordRequired"),
                    LocalizationManager.Get("PasswordTitle"));
                return;
            }
            if (newPw != confirmPw)
            {
                ModernMessageBox.ShowError(
                    LocalizationManager.Get("PasswordsDoNotMatch"),
                    LocalizationManager.Get("PasswordTitle"));
                return;
            }
            if (!PasswordPolicy.TryValidate(newPw, _currentUser, out var passwordError))
            {
                ModernMessageBox.ShowError(passwordError, LocalizationManager.Get("PasswordTitle"));
                return;
            }
        }

        try
        {
            if (changePassword)
            {
                var refreshedSession = Database.Instance.ChangePassword(
                    App.CurrentUserId,
                    App.CurrentSecurityStamp,
                    oldPw,
                    newPw);
                App.ApplySessionState(refreshedSession);
                AppLogger.Audit("password.changed", _currentUser, success: true);
            }

            if (!string.IsNullOrEmpty(fullName))
                Database.Instance.UpdateUserFullName(_currentUser, fullName);

            if (changePassword)
            {
                ModernMessageBox.ShowSuccess(
                    LocalizationManager.Get("PasswordChanged"),
                    LocalizationManager.Get("PasswordTitle"));
            }
        }
        catch (UnauthorizedAccessException)
        {
            if (!PermissionGuard.EnsureSessionValid("profile.password.change.denied"))
                return;

            ModernMessageBox.ShowError(
                LocalizationManager.Get("CurrentPasswordWrong"),
                LocalizationManager.Get("PasswordTitle"));
            return;
        }
        catch (Exception ex)
        {
            var reference = AppLogger.LogException("profile.update.failed", ex);
            ModernMessageBox.ShowError(
                string.Format(LocalizationManager.Get("OperationFailedWithReference"), reference),
                LocalizationManager.Get("AppErrorTitle"));
            return;
        }
        Close();
    }

    private void LoadAvatar()
    {
        var base64 = Database.Instance.GetUserAvatar(_currentUser);
        var img = AvatarHelper.Base64ToImage(base64);
        AvatarImage.Source = img ?? AvatarHelper.GetDefaultAvatar(TbFullName.Text.Length > 0 ? TbFullName.Text : _currentUser);
    }

    private void Avatar_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => ChangeAvatar();
    private void ChangeAvatar_Click(object sender, RoutedEventArgs e) => ChangeAvatar();

    private void ChangeAvatar()
    {
        if (!PermissionGuard.EnsureSessionValid("profile.avatar.update"))
            return;

        var dlg = new OpenFileDialog
        {
            Filter = "Bilder|*.jpg;*.jpeg;*.png|Alle Dateien|*.*",
            Title = "Profilbild auswählen"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            if (!PermissionGuard.EnsureSessionValid("profile.avatar.update.confirmed"))
                return;
            var base64 = AvatarHelper.LoadAndValidateImage(dlg.FileName);
            if (!PermissionGuard.EnsureSessionValid("profile.avatar.update.persist"))
                return;
            Database.Instance.SaveUserAvatar(_currentUser, base64);
            LoadAvatar();
        }
        catch (Exception ex)
        {
            var reference = AppLogger.LogException("profile.avatar.update_failed", ex);
            ModernMessageBox.ShowError(
                string.Format(LocalizationManager.Get("OperationFailedWithReference"), reference),
                LocalizationManager.Get("AppErrorTitle"));
        }
    }

    private void RemoveAvatar_Click(object sender, RoutedEventArgs e)
    {
        if (!PermissionGuard.EnsureSessionValid("profile.avatar.remove"))
            return;

        Database.Instance.SaveUserAvatar(_currentUser, null);
        LoadAvatar();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    private void Chrome_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            FindParent<TextBoxBase>(source) == null &&
            FindParent<PasswordBox>(source) == null &&
            FindParent<Button>(source) == null &&
            FindParent<ComboBox>(source) == null &&
            FindParent<ScrollBar>(source) == null)
        {
            try { DragMove(); } catch { }
        }
    }

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T match) return match;
            child = System.Windows.Media.VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    public static void Show(string currentUser)
    {
        new UserSettingsDialog(currentUser).ShowDialog();
    }
}
