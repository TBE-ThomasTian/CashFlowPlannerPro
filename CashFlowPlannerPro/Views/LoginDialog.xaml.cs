using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CashFlowPlannerPro.Data;
using Microsoft.Win32;

namespace CashFlowPlannerPro.Views;

public partial class LoginDialog : Window
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CashFlowPlannerPro");
    private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");

    private List<string> _usernames = [];

    public string SelectedDatabasePath { get; private set; } = string.Empty;
    public string SelectedUsername { get; private set; } = string.Empty;

    public LoginDialog()
    {
        InitializeComponent();
        LoadSettings();
        UsernameTextBox.TextChanged += (_, _) => {
            UsernamePlaceholder.Visibility = string.IsNullOrEmpty(UsernameTextBox.Text)
                ? Visibility.Visible : Visibility.Collapsed;
        };
        UsernameTextBox.GotFocus += (_, _) => {
            if (_usernames.Count > 0) UsernamePopup.IsOpen = true;
        };
    }

    private void UsernameListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UsernameListBox.SelectedItem is string selected)
        {
            UsernameTextBox.Text = selected;
            UsernamePopup.IsOpen = false;
            PasswordBox.Focus();
        }
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null && !string.IsNullOrEmpty(settings.LastDatabasePath)
                    && File.Exists(settings.LastDatabasePath))
                    SetDatabasePath(settings.LastDatabasePath);
            }
        }
        catch { }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(new AppSettings { LastDatabasePath = SelectedDatabasePath });
            File.WriteAllText(SettingsFile, json);
        }
        catch { }
    }

    private void SetDatabasePath(string path)
    {
        SelectedDatabasePath = path;
        DbPathText.Text = path;
        DbPathText.ToolTip = path;
        LoadUsernames();
    }

    private void LoadUsernames()
    {
        try
        {
            Database.Instance.Open(SelectedDatabasePath);
            Database.Instance.EnsureSchema();
            _usernames = Database.Instance.GetUsernames();
            UsernameListBox.Items.Clear();
            foreach (var u in _usernames)
                UsernameListBox.Items.Add(u);
        }
        catch (Exception ex)
        {
            ShowError($"Datenbankfehler: {ex.Message}");
        }
    }

    private void OpenDbButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog {
            Title = "Datenbank öffnen",
            Filter = "SQLite Datenbank (*.db)|*.db|Alle Dateien (*.*)|*.*",
            DefaultExt = ".db"
        };
        if (dialog.ShowDialog() == true) { SetDatabasePath(dialog.FileName); ClearError(); }
    }

    private void NewDbButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog {
            Title = "Neue Datenbank erstellen",
            Filter = "SQLite Datenbank (*.db)|*.db",
            DefaultExt = ".db", FileName = "cashflow.db"
        };
        if (dialog.ShowDialog() == true)
        {
            try { SetDatabasePath(dialog.FileName); ClearError(); }
            catch (Exception ex) { ShowError($"Fehler beim Erstellen: {ex.Message}"); }
        }
    }

    private void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(SelectedDatabasePath))
        { ShowError("Bitte wählen Sie zuerst eine Datenbank aus."); return; }
        var username = UsernameTextBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(username))
        { ShowError("Bitte geben Sie einen Benutzernamen ein."); return; }
        var password = PasswordBox.Password;
        if (string.IsNullOrEmpty(password))
        { ShowError("Bitte geben Sie ein Passwort ein."); return; }
        try
        {
            if (Database.Instance.ValidateUser(username, password))
            { SelectedUsername = username; SaveSettings(); DialogResult = true; }
            else
                ShowError("Ungültiger Benutzername oder Passwort.");
        }
        catch (Exception ex) { ShowError($"Anmeldefehler: {ex.Message}"); }
    }

    private void ShowError(string msg) { ErrorText.Text = msg; ErrorText.Visibility = Visibility.Visible; }
    private void ClearError() { ErrorText.Text = ""; ErrorText.Visibility = Visibility.Collapsed; }

    private class AppSettings { public string LastDatabasePath { get; set; } = ""; }
}
