using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Services;
using Microsoft.Win32;
using MySqlConnector;

namespace CashFlowPlannerPro.Views;

public partial class LoginDialog : Window
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CashFlowPlannerPro");
    private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");
    private static readonly string DemoDir = Path.Combine(SettingsDir, "Demo");
    private static readonly string DemoDatabasePath = Path.Combine(DemoDir, "cashflow-demo.db");

    private List<string> _usernames = [];
    private string _lastDatabasePath = string.Empty;
    private DatabaseBackend _selectedBackend = DatabaseBackend.SQLite;

    public string SelectedDatabasePath { get; private set; } = string.Empty;
    public string SelectedUsername { get; private set; } = string.Empty;
    public ConnectionConfig? ActiveConnectionConfig { get; private set; }
    public bool IsDemoSession { get; private set; }
    private bool _isBusy;

    public LoginDialog()
    {
        InitializeComponent();
        LocalizationManager.LanguageChanged += OnLanguageChanged;
        LoadSettings();
        ApplyLocalization();
        UpdateConnectionExpanderState();

        OpenDbButton.ToolTip = TooltipService.Get("Btn_OpenDb");
        NewDbButton.ToolTip = TooltipService.Get("Btn_NewDb");
        TestConnectionBtn.ToolTip = TooltipService.Get("Btn_TestConnection");
        ImportLocalBtn.ToolTip = TooltipService.Get("Btn_ImportLocal");
        ImportServerBtn.ToolTip = TooltipService.Get("Btn_ImportServer");
        LoginButton.ToolTip = TooltipService.Get("Btn_Login");
        DemoButton.ToolTip = LocalizationManager.Get("LoginDemoButton");
        BusyText.Text = LocalizationManager.Get("LoadingPleaseWait");
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"v{version?.Major}.{version?.Minor}.{version?.Build}";
        UsernameTextBox.TextChanged += (_, _) => {
            UsernamePlaceholder.Visibility = string.IsNullOrEmpty(UsernameTextBox.Text)
                ? Visibility.Visible : Visibility.Collapsed;
        };
        UsernameTextBox.GotFocus += (_, _) => {
            if (_usernames.Count > 0) UsernamePopup.IsOpen = true;
        };
        Closed += (_, _) => LocalizationManager.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        ApplyLocalization();
        UpdateDatabasePathDisplay();
        if (_isBusy)
            BusyText.Text = LocalizationManager.Get("LoadingPleaseWait");
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
                if (settings != null)
                    ApplyBasicSettings(settings);
            }

            var secure = SecureConnectionStore.Load();
            if (secure != null && secure.RememberSettings)
            {
                ChkRememberSettings.IsChecked = true;
                ApplySecureSettings(secure);

                if (!string.IsNullOrEmpty(secure.AppUsername))
                    UsernameTextBox.Text = secure.AppUsername;
            }

            BackendToggle_Changed(this, new RoutedEventArgs());
            UpdateDatabasePathDisplay();
        }
        catch { }
    }

    private void ApplyBasicSettings(AppSettings settings)
    {
        if (settings.Backend == "MariaDB")
        {
            _selectedBackend = DatabaseBackend.MariaDB;
            RbMariaDb.IsChecked = true;
            TbMariaHost.Text = settings.MariaDbHost ?? "localhost";
            TbMariaPort.Text = (settings.MariaDbPort > 0 ? settings.MariaDbPort : 3306).ToString();
            TbMariaDatabase.Text = settings.MariaDbDatabase ?? "cashflow";
            TbMariaUser.Text = settings.MariaDbUsername ?? "root";
            return;
        }

        _selectedBackend = DatabaseBackend.SQLite;
        RbSqlite.IsChecked = true;
        if (!string.IsNullOrEmpty(settings.LastDatabasePath) && File.Exists(settings.LastDatabasePath))
        {
            _lastDatabasePath = settings.LastDatabasePath;
            SelectedDatabasePath = _lastDatabasePath;
        }
    }

    private void ApplySecureSettings(SecureConnectionData secure)
    {
        if (secure.Backend == "MariaDB")
        {
            _selectedBackend = DatabaseBackend.MariaDB;
            RbMariaDb.IsChecked = true;
            TbMariaHost.Text = secure.Host ?? TbMariaHost.Text;
            TbMariaPort.Text = (secure.Port > 0 ? secure.Port : 3306).ToString();
            TbMariaDatabase.Text = secure.DatabaseName ?? TbMariaDatabase.Text;
            TbMariaUser.Text = secure.DbUsername ?? TbMariaUser.Text;
            PbMariaPassword.Password = secure.DbPassword ?? "";
            return;
        }

        _selectedBackend = DatabaseBackend.SQLite;
        RbSqlite.IsChecked = true;
        if (!string.IsNullOrEmpty(secure.LastDatabasePath) && File.Exists(secure.LastDatabasePath))
        {
            _lastDatabasePath = secure.LastDatabasePath;
            SelectedDatabasePath = _lastDatabasePath;
        }
    }

    private void SaveSettings()
    {
        if (IsDemoSession)
            return;

        try
        {
            SaveBasicSettings();

            if (ChkRememberSettings.IsChecked == true)
            {
                // Save credentials encrypted via DPAPI. Basic connection metadata
                // is still stored in settings.json so the last backend is restored
                // even if protected credentials cannot be decrypted.
                int.TryParse(TbMariaPort.Text, out var port);
                var secure = new SecureConnectionData
                {
                    Backend = _selectedBackend == DatabaseBackend.MariaDB ? "MariaDB" : "SQLite",
                    RememberSettings = true,
                    AppUsername = UsernameTextBox.Text?.Trim()
                };

                if (_selectedBackend == DatabaseBackend.SQLite)
                {
                    secure.LastDatabasePath = SelectedDatabasePath;
                }
                else
                {
                    secure.Host = TbMariaHost.Text;
                    secure.Port = port > 0 ? port : 3306;
                    secure.DatabaseName = TbMariaDatabase.Text;
                    secure.DbUsername = TbMariaUser.Text;
                    secure.DbPassword = PbMariaPassword.Password;
                }

                SecureConnectionStore.Save(secure);
            }
            else
            {
                SecureConnectionStore.Delete();
            }
        }
        catch { }
    }

    private void SaveBasicSettings()
    {
        Directory.CreateDirectory(SettingsDir);
        int.TryParse(TbMariaPort.Text, out var port);
        var settings = new AppSettings
        {
            Backend = _selectedBackend == DatabaseBackend.MariaDB ? "MariaDB" : "SQLite",
            LastDatabasePath = SelectedDatabasePath,
            MariaDbHost = TbMariaHost.Text,
            MariaDbPort = port > 0 ? port : 3306,
            MariaDbDatabase = TbMariaDatabase.Text,
            MariaDbUsername = TbMariaUser.Text
        };

        var json = JsonSerializer.Serialize(settings);
        File.WriteAllText(SettingsFile, json);
    }

    private void SetDatabasePath(string path)
    {
        SelectedDatabasePath = path;
        UpdateDatabasePathDisplay();
        _ = LoadUsernamesAsync();
    }

    private void ApplyLocalization()
    {
        Title = LocalizationManager.Get("LoginWindowTitle");
        TitleText.Text = LocalizationManager.Get("MainTitle");
        SubtitleText.Text = LocalizationManager.Get("LoginSubtitle");
        DatabaseStepText.Text = LocalizationManager.Get("LoginStepDatabase");
        SignInStepText.Text = LocalizationManager.Get("LoginStepSignIn");
        OpenDbButton.Content = LocalizationManager.Get("LoginOpen");
        NewDbButton.Content = LocalizationManager.Get("LoginNew");
        LoginButton.Content = LocalizationManager.Get("LoginButton");
        DemoButton.Content = LocalizationManager.Get("LoginDemoButton");
        UsernamePlaceholder.Text = LocalizationManager.Get("LoginUsernamePlaceholder");
        BusyText.Text = LocalizationManager.Get("LoadingPleaseWait");
        if (string.IsNullOrWhiteSpace(SelectedDatabasePath))
            UpdateDatabasePathDisplay();
    }

    private void UpdateDatabasePathDisplay()
    {
        if (string.IsNullOrWhiteSpace(SelectedDatabasePath))
        {
            DbPathText.Text = LocalizationManager.Get("LoginNoDatabaseSelected");
            DbPathText.ToolTip = null;
            UpdateConnectionExpanderState();
            return;
        }

        var isLast = !string.IsNullOrWhiteSpace(_lastDatabasePath)
            && string.Equals(SelectedDatabasePath, _lastDatabasePath, StringComparison.OrdinalIgnoreCase);

        DbPathText.Text = isLast
            ? string.Format(LocalizationManager.Get("LoginLastDatabase"), SelectedDatabasePath)
            : SelectedDatabasePath;
        DbPathText.ToolTip = SelectedDatabasePath;
        UpdateConnectionExpanderState();
    }

    private void UpdateConnectionExpanderState()
    {
        if (DatabaseExpander == null)
            return;

        DatabaseExpander.IsExpanded = false;
    }

    private ConnectionConfig BuildConnectionConfig()
    {
        if (_selectedBackend == DatabaseBackend.MariaDB)
        {
            int.TryParse(TbMariaPort.Text, out var port);
            return new ConnectionConfig
            {
                Backend = DatabaseBackend.MariaDB,
                Host = TbMariaHost.Text.Trim(),
                Port = port > 0 ? port : 3306,
                DatabaseName = TbMariaDatabase.Text.Trim(),
                DbUsername = TbMariaUser.Text.Trim(),
                DbPassword = PbMariaPassword.Password
            };
        }
        return new ConnectionConfig
        {
            Backend = DatabaseBackend.SQLite,
            FilePath = SelectedDatabasePath
        };
    }

    private async Task LoadUsernamesAsync()
    {
        if (_isBusy)
            return;

        SetBusy(true);
        try
        {
            var config = BuildConnectionConfig();
            ActiveConnectionConfig = config;
            _usernames = await Task.Run(() =>
            {
                Database.Instance.Open(config);
                Database.Instance.EnsureSchema();
                return Database.Instance.GetUsernames();
            });
            UsernameListBox.Items.Clear();
            foreach (var u in _usernames)
                UsernameListBox.Items.Add(u);
        }
        catch (Exception ex)
        {
            ShowError(string.Format(LocalizationManager.Get("LoginDatabaseError"), FormatDatabaseError(ex)));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    private void OpenDbButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog {
            Title = LocalizationManager.Get("LoginOpenDbTitle"),
            Filter = LocalizationManager.Get("LoginOpenDbFilter"),
            DefaultExt = ".db"
        };
        if (dialog.ShowDialog() == true) { SetDatabasePath(dialog.FileName); ClearError(); }
    }

    private void NewDbButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog {
            Title = LocalizationManager.Get("LoginNewDbTitle"),
            Filter = LocalizationManager.Get("LoginNewDbFilter"),
            DefaultExt = ".db", FileName = "cashflow.db"
        };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                CreateFreshDatabase(dialog.FileName);
                SetDatabasePath(dialog.FileName);
                ClearError();
            }
            catch (Exception ex) { ShowError(string.Format(LocalizationManager.Get("LoginCreateError"), ex.Message)); }
        }
    }

    private static void CreateFreshDatabase(string path)
    {
        Database.Instance.Close();
        DeleteIfExists(path);
        DeleteIfExists(path + "-wal");
        DeleteIfExists(path + "-shm");
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        IsDemoSession = false;
        if (_selectedBackend == DatabaseBackend.SQLite && string.IsNullOrEmpty(SelectedDatabasePath))
        { ShowError(LocalizationManager.Get("LoginSelectDatabaseError")); return; }
        if (_selectedBackend == DatabaseBackend.MariaDB && string.IsNullOrWhiteSpace(TbMariaHost.Text))
        { ShowError("Bitte einen Server-Host eingeben."); return; }
        var username = UsernameTextBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(username))
        { ShowError(LocalizationManager.Get("LoginUsernameRequired")); return; }
        var password = PasswordBox.Password;
        if (string.IsNullOrEmpty(password))
        { ShowError(LocalizationManager.Get("LoginPasswordRequired")); return; }
        try
        {
            var config = BuildConnectionConfig();
            ActiveConnectionConfig = config;
            SetBusy(true);
            var loginResult = await Task.Run(() =>
            {
                Database.Instance.Open(config);
                Database.Instance.EnsureSchema();
                var isValid = Database.Instance.ValidateUser(username, password);
                return new LoginCheckResult
                {
                    IsValid = isValid,
                    RequiresDefaultPasswordChange = isValid && Database.Instance.IsFirstRun && username == "admin" && password == "admin"
                };
            });

            if (loginResult.IsValid)
            {
                SelectedUsername = username;
                SaveSettings();

                if (loginResult.RequiresDefaultPasswordChange)
                {
                    ModernMessageBox.Show(
                        LocalizationManager.Get("FirstRunPasswordPrompt"),
                        LocalizationManager.Get("FirstRunSecurityTitle"));
                    var pwDlg = new InputDialog(
                        LocalizationManager.Get("NewPasswordDialogTitle"),
                        LocalizationManager.Get("NewPasswordDialogLabel"),
                        isPassword: true);
                    if (pwDlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(pwDlg.ResultText))
                    {
                        if (pwDlg.ResultText == "admin")
                        {
                            ShowError(LocalizationManager.Get("PasswordCannotBeAdmin"));
                            return;
                        }

                        SetBusy(true);
                        await Task.Run(() => Database.Instance.ChangePassword(username, pwDlg.ResultText));
                        ModernMessageBox.Show(
                            LocalizationManager.Get("PasswordChangedSuccess"),
                            LocalizationManager.Get("DoneTitle"));
                    }
                    else
                    {
                        ShowError(LocalizationManager.Get("PasswordChangeRequired"));
                        return;
                    }
                }

                DialogResult = true;
            }
            else
            {
                ShowError(LocalizationManager.Get("LoginInvalidCredentials"));
            }
        }
        catch (Exception ex) { ShowError(string.Format(LocalizationManager.Get("LoginError"), FormatDatabaseError(ex))); }
        finally
        {
            SetBusy(false);
        }
    }

    private async void DemoButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var config = new ConnectionConfig
            {
                Backend = DatabaseBackend.SQLite,
                FilePath = DemoDatabasePath
            };

            SetBusy(true);
            await Task.Run(() =>
            {
                Directory.CreateDirectory(DemoDir);
                CreateFreshDatabase(DemoDatabasePath);
                Database.Instance.Open(config);
                Database.Instance.EnsureSchema();
                Database.Instance.SeedDemoData();
            });

            ActiveConnectionConfig = config;
            SelectedDatabasePath = DemoDatabasePath;
            SelectedUsername = "demo";
            IsDemoSession = true;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ShowError(string.Format(LocalizationManager.Get("LoginCreateError"), ex.Message));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void BackendToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (PanelSqlite == null || PanelMariaDb == null) return; // not yet initialized
        if (RbMariaDb.IsChecked == true)
        {
            _selectedBackend = DatabaseBackend.MariaDB;
            PanelSqlite.Visibility = Visibility.Collapsed;
            PanelMariaDb.Visibility = Visibility.Visible;
        }
        else
        {
            _selectedBackend = DatabaseBackend.SQLite;
            PanelSqlite.Visibility = Visibility.Visible;
            PanelMariaDb.Visibility = Visibility.Collapsed;
        }

        UpdateConnectionExpanderState();
    }

    private async void TestMariaDbConnection_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var config = BuildConnectionConfig();
            SetBusy(true);
            _usernames = await Task.Run(() =>
            {
                Database.Instance.Open(config);
                Database.Instance.EnsureSchema();
                return Database.Instance.GetUsernames();
            });
            UsernameListBox.Items.Clear();
            foreach (var u in _usernames)
                UsernameListBox.Items.Add(u);
            TbMariaStatus.Text = "✅ Sichere TLS-Verbindung und Datenbankschema erfolgreich!";
            TbMariaStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x2E, 0xCC, 0x71));
        }
        catch (Exception ex)
        {
            TbMariaStatus.Text = $"❌ Fehler: {FormatDatabaseError(ex)}";
            TbMariaStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ImportFromSqlite_Click(object sender, RoutedEventArgs e)
    {
        // Current connection must be MariaDB
        if (_selectedBackend != DatabaseBackend.MariaDB)
        {
            ShowError("Bitte zuerst mit dem MariaDB-Server verbinden.");
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Lokale SQLite-Datenbank auswählen",
            Filter = "SQLite Datenbank (*.db)|*.db|Alle Dateien (*.*)|*.*",
            DefaultExt = ".db"
        };
        if (dialog.ShowDialog() != true) return;

        if (!ModernMessageBox.ShowConfirm(
            "Alle Daten vom Server werden durch die lokale Datenbank ersetzt.\n\nFortfahren?",
            "Daten importieren"))
            return;

        try
        {
            // Ensure target (MariaDB) is open
            var targetConfig = BuildConnectionConfig();
            Database.Instance.Open(targetConfig);
            Database.Instance.EnsureSchema();

            var sourceConfig = new ConnectionConfig
            {
                Backend = DatabaseBackend.SQLite,
                FilePath = dialog.FileName
            };

            var result = DatabaseMigrator.Migrate(sourceConfig);
            if (result.Success)
                ModernMessageBox.Show($"Import erfolgreich!\n{result.Summary()}", "Migration abgeschlossen");
            else
                ModernMessageBox.ShowError($"Import mit Fehlern:\n{result.Summary()}", "Migration");

            _ = LoadUsernamesAsync();
        }
        catch (Exception ex)
        {
            ShowError($"Import fehlgeschlagen: {FormatDatabaseError(ex)}");
        }
    }

    private void ImportFromMariaDb_Click(object sender, RoutedEventArgs e)
    {
        // Current connection must be SQLite
        if (_selectedBackend != DatabaseBackend.SQLite || string.IsNullOrEmpty(SelectedDatabasePath))
        {
            ShowError("Bitte zuerst eine lokale Datenbank öffnen oder erstellen.");
            return;
        }

        // Ask for MariaDB credentials
        var connDlg = new MariaDbConnectDialog();
        if (connDlg.ShowDialog() != true) return;

        if (!ModernMessageBox.ShowConfirm(
            "Alle lokalen Daten werden durch die Server-Datenbank ersetzt.\n\nFortfahren?",
            "Daten importieren"))
            return;

        try
        {
            // Ensure target (SQLite) is open
            Database.Instance.Open(SelectedDatabasePath);
            Database.Instance.EnsureSchema();

            var result = DatabaseMigrator.Migrate(connDlg.Config);
            if (result.Success)
                ModernMessageBox.Show($"Import erfolgreich!\n{result.Summary()}", "Migration abgeschlossen");
            else
                ModernMessageBox.ShowError($"Import mit Fehlern:\n{result.Summary()}", "Migration");

            _ = LoadUsernamesAsync();
        }
        catch (Exception ex)
        {
            ShowError($"Import fehlgeschlagen: {FormatDatabaseError(ex)}");
        }
    }

    private void ShowError(string msg) { ErrorText.Text = msg; ErrorText.Visibility = Visibility.Visible; }
    private void ClearError() { ErrorText.Text = ""; ErrorText.Visibility = Visibility.Collapsed; }

    private static string FormatDatabaseError(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is MySqlException mySqlException)
            {
                return mySqlException.Number switch
                {
                    1049 => "Die angegebene Datenbank existiert nicht. Bitte die Datenbank auf dem Server vorab anlegen und den Namen prüfen.",
                    1045 => "Zugriff verweigert. Bitte DB-Benutzer, Passwort und die Hostfreigabe dieses Benutzers prüfen.",
                    1044 => "Der DB-Benutzer hat keine Berechtigung für diese Datenbank.",
                    _ when mySqlException.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase)
                        || mySqlException.Message.Contains("TLS", StringComparison.OrdinalIgnoreCase)
                        || mySqlException.InnerException is System.Security.Authentication.AuthenticationException
                        => $"TLS-Prüfung fehlgeschlagen: {mySqlException.Message}",
                    _ => $"MariaDB-Fehler {mySqlException.Number}: {mySqlException.Message}"
                };
            }
        }

        return exception.Message;
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        MainContentGrid.IsEnabled = !isBusy;
        BusyOverlay.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        Mouse.OverrideCursor = isBusy ? Cursors.Wait : null;
    }

    private sealed class LoginCheckResult
    {
        public bool IsValid { get; init; }
        public bool RequiresDefaultPasswordChange { get; init; }
    }

    private class AppSettings
    {
        public string LastDatabasePath { get; set; } = "";
        public string Backend { get; set; } = "SQLite";
        public string? MariaDbHost { get; set; }
        public int MariaDbPort { get; set; } = 3306;
        public string? MariaDbDatabase { get; set; }
        public string? MariaDbUsername { get; set; }
    }
}
