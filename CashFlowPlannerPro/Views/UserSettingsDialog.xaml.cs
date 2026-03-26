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
    private readonly List<LanguageOption> _languages = [];

    public UserSettingsDialog(string currentUser)
    {
        InitializeComponent();
        _currentUser = currentUser;
        LocalizationManager.LanguageChanged += OnLanguageChanged;
        TbCurrentUser.Text = currentUser;
        TbFullName.Text = Database.Instance.GetFullName(currentUser) ?? "";
        LoadAvatar();
        BuildLanguageOptions();
        ApplyLocalization();
        Owner = Application.Current.MainWindow?.IsVisible == true ? Application.Current.MainWindow : null;
        SaveButton.ToolTip = TooltipService.Get("Btn_Save");
        CancelButton.ToolTip = TooltipService.Get("Btn_Cancel");
        ChangeAvatarButton.ToolTip = TooltipService.Get("Btn_ChangeAvatar");
        RemoveAvatarButton.ToolTip = TooltipService.Get("Btn_RemoveAvatar");
        Closed += (_, _) => LocalizationManager.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        ApplyLocalization();
    }

    private void BuildLanguageOptions()
    {
        _languages.Clear();
        _languages.Add(new LanguageOption("de", LocalizationManager.Get("LanguageGerman")));
        _languages.Add(new LanguageOption("en", LocalizationManager.Get("LanguageEnglish")));
        LanguageCombo.ItemsSource = null;
        LanguageCombo.ItemsSource = _languages;
        LanguageCombo.DisplayMemberPath = nameof(LanguageOption.Label);
        LanguageCombo.SelectedValuePath = nameof(LanguageOption.Code);
        LanguageCombo.SelectedValue = LocalizationManager.CurrentLanguageCode;
    }

    private void ApplyLocalization()
    {
        Title = LocalizationManager.Get("ProfileTitle");
        TitleText.Text = LocalizationManager.Get("ProfileTitle");
        LoggedInAsLabel.Text = LocalizationManager.Get("ProfileLoggedInAs");
        DisplayNameLabel.Text = LocalizationManager.Get("ProfileDisplayName");
        LanguageLabel.Text = LocalizationManager.Get("ProfileLanguage");
        CompanyProfileButton.Content = LocalizationManager.Get("CompanyProfileButton");
        ChangePasswordLabel.Text = LocalizationManager.Get("ProfileChangePassword");
        CurrentPasswordLabel.Text = LocalizationManager.Get("ProfileCurrentPassword");
        NewPasswordLabel.Text = LocalizationManager.Get("ProfileNewPassword");
        ConfirmPasswordLabel.Text = LocalizationManager.Get("ProfileConfirmPassword");
        CancelButton.Content = LocalizationManager.Get("Cancel");
        SaveButton.Content = LocalizationManager.Get("Save");
        BuildLanguageOptions();
    }

    private void CompanyProfile_Click(object sender, RoutedEventArgs e)
    {
        CompanyProfileDialog.ShowDialogWindow();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Save full name
        var fullName = TbFullName.Text.Trim();
        if (!string.IsNullOrEmpty(fullName))
            Database.Instance.UpdateUserFullName(_currentUser, fullName);

        if (LanguageCombo.SelectedValue is string languageCode && !string.IsNullOrWhiteSpace(languageCode))
            LocalizationManager.SetLanguage(languageCode);

        // Change password if fields are filled
        var oldPw = PbOldPassword.Password;
        var newPw = PbNewPassword.Password;
        var confirmPw = PbConfirmPassword.Password;

        if (!string.IsNullOrEmpty(newPw) || !string.IsNullOrEmpty(oldPw))
        {
            if (string.IsNullOrEmpty(oldPw))
            {
                ModernMessageBox.ShowError(
                    LocalizationManager.Get("CurrentPasswordRequired"),
                    LocalizationManager.Get("PasswordTitle"));
                return;
            }
            if (!Database.Instance.ValidateUser(_currentUser, oldPw))
            {
                ModernMessageBox.ShowError(
                    LocalizationManager.Get("CurrentPasswordWrong"),
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
            Database.Instance.ChangePassword(_currentUser, newPw);
            ModernMessageBox.ShowSuccess(
                LocalizationManager.Get("PasswordChanged"),
                LocalizationManager.Get("PasswordTitle"));
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
        var dlg = new OpenFileDialog
        {
            Filter = "Bilder|*.jpg;*.jpeg;*.png|Alle Dateien|*.*",
            Title = "Profilbild auswählen"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var base64 = AvatarHelper.LoadAndValidateImage(dlg.FileName);
            Database.Instance.SaveUserAvatar(_currentUser, base64);
            LoadAvatar();
        }
        catch (Exception ex)
        {
            ModernMessageBox.ShowError(ex.Message, "Profilbild");
        }
    }

    private void RemoveAvatar_Click(object sender, RoutedEventArgs e)
    {
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

    private sealed class LanguageOption(string code, string label)
    {
        public string Code { get; } = code;
        public string Label { get; } = label;
    }
}
