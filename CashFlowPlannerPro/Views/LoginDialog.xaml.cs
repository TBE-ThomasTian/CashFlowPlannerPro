using System.ComponentModel;
using System.Data.Common;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Models;
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
    private readonly ConnectionConfig? _migrationAuthorizationConnection;
    private readonly string _migrationAuthorizationUsername = string.Empty;
    private readonly long _migrationAuthorizationUserId;
    private readonly string _migrationAuthorizationSecurityStamp = string.Empty;
    private readonly bool _migrationControlsEnabled;
    private bool _allowClose;

    public string SelectedDatabasePath { get; private set; } = string.Empty;
    public string SelectedUsername { get; private set; } = string.Empty;
    public UserSessionState? AuthenticatedSession { get; private set; }
    public ConnectionConfig? ActiveConnectionConfig { get; private set; }
    public bool IsDemoSession { get; private set; }
    public bool RequiresFreshAuthenticationAfterMigration { get; private set; }
    private bool _isBusy;

    public LoginDialog() : this(null, null, 0, null)
    {
    }

    internal LoginDialog(
        ConnectionConfig? migrationAuthorizationConnection,
        string? migrationAuthorizationUsername,
        long migrationAuthorizationUserId,
        string? migrationAuthorizationSecurityStamp)
    {
        _migrationAuthorizationConnection = migrationAuthorizationConnection?.Clone();
        _migrationAuthorizationUsername = migrationAuthorizationUsername?.Trim() ?? string.Empty;
        _migrationAuthorizationUserId = migrationAuthorizationUserId;
        _migrationAuthorizationSecurityStamp = migrationAuthorizationSecurityStamp?.Trim() ?? string.Empty;
        _migrationControlsEnabled = _migrationAuthorizationConnection != null
            && !string.IsNullOrWhiteSpace(_migrationAuthorizationUsername)
            && _migrationAuthorizationUserId > 0
            && !string.IsNullOrWhiteSpace(_migrationAuthorizationSecurityStamp)
            && !App.IsDemoMode
            && App.CanEdit(PageKeys.Admin);

        InitializeComponent();
        LocalizationManager.LanguageChanged += OnLanguageChanged;
        LoadSettings();
        ApplyLocalization();
        UpdateConnectionExpanderState();
        ApplyMigrationAccess();

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
        Closing += LoginDialog_Closing;
        Closed += (_, _) => LocalizationManager.LanguageChanged -= OnLanguageChanged;
    }

    private void ApplyMigrationAccess()
    {
        var visibility = _migrationControlsEnabled ? Visibility.Visible : Visibility.Collapsed;
        ImportLocalBtn.Visibility = visibility;
        ImportServerBtn.Visibility = visibility;
    }

    private void LoginDialog_Closing(object? sender, CancelEventArgs e)
    {
        if (_isBusy && !_allowClose)
            e.Cancel = true;
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
        catch (Exception ex)
        {
            AppLogger.LogException("connection.settings.load_failed", ex);
        }
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
            TbMariaUser.Text = settings.MariaDbUsername ?? string.Empty;
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

                if (!SecureConnectionStore.Save(secure))
                {
                    AppLogger.Info(
                        "connection.settings.save_failed",
                        "Encrypted connection settings could not be persisted.");
                    ModernMessageBox.ShowError(
                        LocalizationManager.Get("ConnectionSettingsSaveFailed"),
                        LocalizationManager.Get("ConnectionSettingsSaveWarningTitle"));
                }
            }
            else
            {
                SecureConnectionStore.Delete();
            }
        }
        catch (Exception ex)
        {
            var reference = AppLogger.LogException("connection.settings.save_failed", ex);
            ModernMessageBox.ShowError(
                string.Format(LocalizationManager.Get("ConnectionSettingsSaveFailedWithReference"), reference),
                LocalizationManager.Get("ConnectionSettingsSaveWarningTitle"));
        }
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
        ImportServerBtn.Content = LocalizationManager.Get("MigrationImportFromServerButton");
        ImportLocalBtn.Content = LocalizationManager.Get("MigrationImportLocalButton");
        TbMariaUser.ToolTip = LocalizationManager.Get("MariaDbDedicatedUserHint");
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
            ShowError(string.Format(
                LocalizationManager.Get("LoginDatabaseError"),
                FormatDatabaseError(ex, "database.usernames_load_failed")));
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

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isBusy)
            Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        if (_isBusy)
        {
            e.Handled = true;
            return;
        }

        Close();
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
            DefaultExt = ".db", FileName = "cashflow.db",
            OverwritePrompt = false
        };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                var candidate = new ConnectionConfig
                {
                    Backend = DatabaseBackend.SQLite,
                    FilePath = dialog.FileName
                };
                if (IsSameDatabase(candidate, _migrationAuthorizationConnection))
                {
                    ShowError(LocalizationManager.Get("SwitchDatabaseCannotRecreateActive"));
                    return;
                }

                CreateFreshDatabase(dialog.FileName, allowReplaceExisting: false);
                SetDatabasePath(dialog.FileName);
                ClearError();
            }
            catch (DatabaseAlreadyExistsException ex)
            {
                ShowError(string.Format(LocalizationManager.Get("LoginCreateError"), ex.Message));
            }
            catch (Exception ex)
            {
                var reference = AppLogger.LogException("database.create_failed", ex);
                ShowError(string.Format(
                    LocalizationManager.Get("LoginCreateError"),
                    string.Format(LocalizationManager.Get("ErrorReferenceValue"), reference)));
            }
        }
    }

    private static void CreateFreshDatabase(string path, bool allowReplaceExisting)
    {
        if (!allowReplaceExisting && File.Exists(path))
            throw new DatabaseAlreadyExistsException();
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

        var config = BuildConnectionConfig();
        var throttleKey = BuildLoginThrottleKey(config, username);
        var globalThrottleKey = BuildLoginThrottleKey(config, "*");
        var remainingDelay = new[]
        {
            LoginAttemptThrottle.GetRemainingDelay(throttleKey),
            LoginAttemptThrottle.GetRemainingDelay(globalThrottleKey)
        }.Max();
        if (remainingDelay > TimeSpan.Zero)
        {
            var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(remainingDelay.TotalSeconds));
            AppLogger.Audit(
                "login.throttled",
                username,
                success: false,
                new { retryAfterSeconds, backend = config.Backend.ToString() });
            ShowError(string.Format(
                LocalizationManager.Get("LoginRetryAfter"),
                retryAfterSeconds));
            return;
        }

        try
        {
            ActiveConnectionConfig = config;
            SetBusy(true);
            var loginResult = await Task.Run(() =>
            {
                Database.Instance.Open(config);
                Database.Instance.EnsureSchema();
                var session = Database.Instance.AuthenticateUser(username, password);
                return new LoginCheckResult
                {
                    Session = session,
                    RequiresDefaultPasswordChange = session != null && Database.Instance.IsFirstRun && username == "admin" && password == "admin"
                };
            });

            if (loginResult.Session != null)
            {
                LoginAttemptThrottle.RegisterSuccess(throttleKey);
                LoginAttemptThrottle.RegisterSuccess(globalThrottleKey);
                SelectedUsername = username;

                if (loginResult.RequiresDefaultPasswordChange)
                {
                    ModernMessageBox.Show(
                        LocalizationManager.Get("FirstRunPasswordPrompt"),
                        LocalizationManager.Get("FirstRunSecurityTitle"));
                    var pwDlg = new PasswordSetupDialog(username)
                    {
                        Owner = this
                    };
                    if (pwDlg.ShowDialog() == true)
                    {
                        SetBusy(true);
                        loginResult.Session = await Task.Run(() => Database.Instance.ChangePassword(
                            loginResult.Session.UserId,
                            loginResult.Session.SecurityStamp,
                            password,
                            pwDlg.Password));
                        AppLogger.Audit("password.first_run_changed", username, success: true);
                        ModernMessageBox.Show(
                            LocalizationManager.Get("PasswordChangedSuccess"),
                            LocalizationManager.Get("DoneTitle"));
                    }
                    else
                    {
                        AppLogger.Audit(
                            "login.denied",
                            username,
                            success: false,
                            new { reason = "required_password_change_cancelled" });
                        ShowError(LocalizationManager.Get("PasswordChangeRequired"));
                        return;
                    }
                }

                AuthenticatedSession = loginResult.Session;
                SaveSettings();
                AppLogger.Audit(
                    "login.succeeded",
                    username,
                    success: true,
                    new { backend = config.Backend.ToString() });
                _allowClose = true;
                DialogResult = true;
            }
            else
            {
                var delay = new[]
                {
                    LoginAttemptThrottle.RegisterFailure(throttleKey),
                    LoginAttemptThrottle.RegisterFailure(globalThrottleKey)
                }.Max();
                var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(delay.TotalSeconds));
                AppLogger.Audit(
                    "login.failed",
                    username,
                    success: false,
                    new
                    {
                        reason = "invalid_credentials",
                        retryAfterSeconds,
                        backend = config.Backend.ToString()
                    });
                ShowError(string.Format(
                    LocalizationManager.Get("LoginInvalidCredentialsWithDelay"),
                    retryAfterSeconds));
            }
        }
        catch (Exception ex)
        {
            var reference = AppLogger.LogException(
                "login.error",
                ex,
                new { backend = config.Backend.ToString() });
            AppLogger.Audit(
                "login.failed",
                username,
                success: false,
                new { reason = "operation_error", reference, backend = config.Backend.ToString() });
            ShowError(string.Format(
                LocalizationManager.Get("LoginErrorWithReference"),
                reference));
        }
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
                CreateFreshDatabase(DemoDatabasePath, allowReplaceExisting: true);
                Database.Instance.Open(config);
                Database.Instance.EnsureSchema();
                Database.Instance.SeedDemoData();
            });

            ActiveConnectionConfig = config;
            SelectedDatabasePath = DemoDatabasePath;
            SelectedUsername = "demo";
            var demoUserId = Database.Instance.GetUserId(SelectedUsername);
            AuthenticatedSession = Database.Instance.GetUserSessionState(demoUserId)
                ?? throw new InvalidOperationException("Die Demo-Sitzung konnte nicht erstellt werden.");
            IsDemoSession = true;
            _allowClose = true;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            var reference = AppLogger.LogException("demo.database_create_failed", ex);
            ShowError(string.Format(
                LocalizationManager.Get("LoginCreateError"),
                string.Format(LocalizationManager.Get("ErrorReferenceValue"), reference)));
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
            TbMariaStatus.Text = $"❌ Fehler: {FormatDatabaseError(ex, "database.connection_test_failed")}";
            TbMariaStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ImportFromSqlite_Click(object sender, RoutedEventArgs e)
    {
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

        var targetConfig = BuildConnectionConfig();
        if (!ModernMessageBox.ShowConfirm(
            string.Format(
                LocalizationManager.Get("MigrationReplaceServerWarning"),
                Path.GetFileName(dialog.FileName),
                targetConfig.Host,
                targetConfig.DatabaseName),
            LocalizationManager.Get("MigrationConfirmTitle")))
            return;

        // Reauthenticate only after every file/connection dialog and the final
        // destructive confirmation, so a revoked session cannot remain authorized
        // while the user spends time in those dialogs.
        if (!await ReauthenticateMigrationAdminAsync())
            return;

        try
        {
            var sourceConfig = new ConnectionConfig
            {
                Backend = DatabaseBackend.SQLite,
                FilePath = dialog.FileName
            };

            SetBusy(true);
            var result = await Task.Run(() =>
            {
                Database.Instance.Open(targetConfig);
                Database.Instance.EnsureSchema();
                if (!ValidateMigrationSessionAuthorization(
                        _migrationAuthorizationConnection!,
                        _migrationAuthorizationUsername,
                        _migrationAuthorizationUserId,
                        _migrationAuthorizationSecurityStamp))
                    throw new UnauthorizedAccessException(LocalizationManager.Get("MigrationAdminRequired"));
                return DatabaseMigrator.Migrate(sourceConfig);
            });
            SetBusy(false);

            if (result.Success)
            {
                RequiresFreshAuthenticationAfterMigration = IsSameDatabase(
                    targetConfig,
                    _migrationAuthorizationConnection);
                AppLogger.Audit(
                    "database.migration.completed",
                    $"{sourceConfig.Backend}->{targetConfig.Backend}",
                    success: true,
                    new { rows = result.TotalRows });
                ModernMessageBox.Show($"Import erfolgreich!\n{FormatMigrationSummary(result)}", "Migration abgeschlossen");
            }
            else
            {
                var reference = LogMigrationErrors("database.migration_from_sqlite_failed", result);
                ModernMessageBox.ShowError(
                    $"Import mit {result.Errors.Count} Fehler(n) beendet. Details wurden protokolliert. Referenz: {reference}",
                    "Migration");
            }

            await LoadUsernamesAsync();
        }
        catch (Exception ex)
        {
            ShowError($"Import fehlgeschlagen: {FormatDatabaseError(ex, "database.migration_from_sqlite_failed")}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static string BuildLoginThrottleKey(ConnectionConfig config, string username)
    {
        string endpoint;
        if (config.Backend == DatabaseBackend.MariaDB)
        {
            endpoint = $"{config.Host.Trim().ToUpperInvariant()}:{config.Port}/{config.DatabaseName.Trim().ToUpperInvariant()}";
        }
        else
        {
            var filePath = config.FilePath?.Trim() ?? string.Empty;
            try { endpoint = Path.GetFullPath(filePath).ToUpperInvariant(); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                endpoint = filePath.ToUpperInvariant();
            }
        }

        var material = $"{config.Backend}\n{endpoint}\n{username.Trim().ToUpperInvariant()}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    private async void ImportFromMariaDb_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedBackend != DatabaseBackend.SQLite || string.IsNullOrEmpty(SelectedDatabasePath))
        {
            ShowError("Bitte zuerst eine lokale Datenbank öffnen oder erstellen.");
            return;
        }

        var connDlg = new MariaDbConnectDialog();
        if (connDlg.ShowDialog() != true) return;

        if (!ModernMessageBox.ShowConfirm(
            string.Format(
                LocalizationManager.Get("MigrationReplaceLocalWarning"),
                connDlg.Config.Host,
                connDlg.Config.DatabaseName,
                SelectedDatabasePath),
            LocalizationManager.Get("MigrationConfirmTitle")))
            return;

        if (!await ReauthenticateMigrationAdminAsync())
            return;

        try
        {
            var targetConfig = BuildConnectionConfig();
            SetBusy(true);
            var result = await Task.Run(() =>
            {
                Database.Instance.Open(targetConfig);
                Database.Instance.EnsureSchema();
                if (!ValidateMigrationSessionAuthorization(
                        _migrationAuthorizationConnection!,
                        _migrationAuthorizationUsername,
                        _migrationAuthorizationUserId,
                        _migrationAuthorizationSecurityStamp))
                    throw new UnauthorizedAccessException(LocalizationManager.Get("MigrationAdminRequired"));
                return DatabaseMigrator.Migrate(connDlg.Config);
            });
            SetBusy(false);

            if (result.Success)
            {
                RequiresFreshAuthenticationAfterMigration = IsSameDatabase(
                    targetConfig,
                    _migrationAuthorizationConnection);
                AppLogger.Audit(
                    "database.migration.completed",
                    $"{connDlg.Config.Backend}->{targetConfig.Backend}",
                    success: true,
                    new { rows = result.TotalRows });
                ModernMessageBox.Show($"Import erfolgreich!\n{FormatMigrationSummary(result)}", "Migration abgeschlossen");
            }
            else
            {
                var reference = LogMigrationErrors("database.migration_from_mariadb_failed", result);
                ModernMessageBox.ShowError(
                    $"Import mit {result.Errors.Count} Fehler(n) beendet. Details wurden protokolliert. Referenz: {reference}",
                    "Migration");
            }

            await LoadUsernamesAsync();
        }
        catch (Exception ex)
        {
            ShowError($"Import fehlgeschlagen: {FormatDatabaseError(ex, "database.migration_from_mariadb_failed")}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<bool> ReauthenticateMigrationAdminAsync()
    {
        if (!_migrationControlsEnabled
            || _migrationAuthorizationConnection == null
            || string.IsNullOrWhiteSpace(_migrationAuthorizationUsername)
            || App.IsDemoMode
            || !App.CanEdit(PageKeys.Admin))
        {
            AppLogger.Audit(
                "database.migration.reauthentication",
                _migrationAuthorizationUsername,
                success: false,
                new { reason = "not_eligible" });
            ShowError(LocalizationManager.Get("MigrationAdminRequired"));
            return false;
        }

        var throttleKey = BuildLoginThrottleKey(
            _migrationAuthorizationConnection,
            _migrationAuthorizationUsername);
        var remainingDelay = LoginAttemptThrottle.GetRemainingDelay(throttleKey);
        if (remainingDelay > TimeSpan.Zero)
        {
            var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(remainingDelay.TotalSeconds));
            AppLogger.Audit(
                "database.migration.reauthentication",
                _migrationAuthorizationUsername,
                success: false,
                new
                {
                    reason = "throttled",
                    retryAfterSeconds,
                    backend = _migrationAuthorizationConnection.Backend.ToString()
                });
            ShowError(string.Format(
                LocalizationManager.Get("MigrationReauthRetryAfter"),
                retryAfterSeconds));
            return false;
        }

        var passwordDialog = new InputDialog(
            LocalizationManager.Get("MigrationReauthTitle"),
            string.Format(
                LocalizationManager.Get("MigrationReauthPrompt"),
                _migrationAuthorizationUsername),
            isPassword: true)
        {
            Owner = this
        };

        if (passwordDialog.ShowDialog() != true)
            return false;

        var password = passwordDialog.ResultText;
        if (string.IsNullOrEmpty(password))
        {
            var delay = LoginAttemptThrottle.RegisterFailure(throttleKey);
            var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(delay.TotalSeconds));
            AppLogger.Audit(
                "database.migration.reauthentication",
                _migrationAuthorizationUsername,
                success: false,
                new
                {
                    reason = "empty_password",
                    retryAfterSeconds,
                    backend = _migrationAuthorizationConnection.Backend.ToString()
                });
            ShowError(string.Format(
                LocalizationManager.Get("MigrationReauthFailedWithDelay"),
                retryAfterSeconds));
            return false;
        }

        try
        {
            SetBusy(true);
            var authorized = await Task.Run(() => ValidateMigrationAuthorization(
                _migrationAuthorizationConnection,
                _migrationAuthorizationUsername,
                _migrationAuthorizationUserId,
                _migrationAuthorizationSecurityStamp,
                password));

            if (!authorized)
            {
                var delay = LoginAttemptThrottle.RegisterFailure(throttleKey);
                var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(delay.TotalSeconds));
                AppLogger.Audit(
                    "database.migration.reauthentication",
                    _migrationAuthorizationUsername,
                    success: false,
                    new
                    {
                        reason = "invalid_credentials_or_access",
                        retryAfterSeconds,
                        backend = _migrationAuthorizationConnection.Backend.ToString()
                    });
                ShowError(string.Format(
                    LocalizationManager.Get("MigrationReauthFailedWithDelay"),
                    retryAfterSeconds));
            }
            else
            {
                LoginAttemptThrottle.RegisterSuccess(throttleKey);
                AppLogger.Audit(
                    "database.migration.reauthentication",
                    _migrationAuthorizationUsername,
                    success: true,
                    new { backend = _migrationAuthorizationConnection.Backend.ToString() });
                ClearError();
            }

            return authorized;
        }
        catch (Exception ex)
        {
            AppLogger.Audit(
                "database.migration.reauthentication",
                _migrationAuthorizationUsername,
                success: false,
                new
                {
                    reason = "operation_error",
                    backend = _migrationAuthorizationConnection.Backend.ToString()
                });
            ShowError(string.Format(
                LocalizationManager.Get("MigrationReauthError"),
                FormatDatabaseError(ex, "database.migration_reauthentication_failed")));
            return false;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static bool ValidateMigrationAuthorization(
        ConnectionConfig connectionConfig,
        string username,
        long expectedUserId,
        string expectedSecurityStamp,
        string password)
        => ValidateMigrationAuthorizationCore(
            connectionConfig,
            username,
            expectedUserId,
            expectedSecurityStamp,
            password);

    private static bool ValidateMigrationSessionAuthorization(
        ConnectionConfig connectionConfig,
        string username,
        long expectedUserId,
        string expectedSecurityStamp)
        => ValidateMigrationAuthorizationCore(
            connectionConfig,
            username,
            expectedUserId,
            expectedSecurityStamp,
            password: null);

    private static bool ValidateMigrationAuthorizationCore(
        ConnectionConfig connectionConfig,
        string username,
        long expectedUserId,
        string expectedSecurityStamp,
        string? password)
    {
        IDbDialect dialect = connectionConfig.Backend == DatabaseBackend.MariaDB
            ? new MariaDbDialect()
            : new SqliteDialect();

        using DbConnection connection = dialect.CreateConnection(connectionConfig.ToConnectionString());
        connection.Open();
        using var tx = connection.BeginTransaction();
        try
        {
            string storedUsername;
            string storedHash;
            string storedStamp;
            bool isActive;
            long? roleId;
            using (var userCommand = connection.CreateCommand())
            {
                userCommand.Transaction = tx;
                userCommand.CommandText = @"SELECT username,password_hash,is_active,security_stamp,role_id
                    FROM users WHERE id=@id" +
                    (dialect is MariaDbDialect ? " FOR UPDATE" : "");
                userCommand.Parameters.AddWithValue("@id", expectedUserId);
                using var reader = userCommand.ExecuteReader();
                if (!reader.Read())
                {
                    tx.Rollback();
                    return false;
                }

                storedUsername = reader.IsDBNull(0) ? "" : reader.GetString(0);
                storedHash = reader.IsDBNull(1) ? "" : reader.GetString(1);
                isActive = !reader.IsDBNull(2) && Convert.ToInt32(reader.GetValue(2)) != 0;
                storedStamp = reader.IsDBNull(3) ? "" : reader.GetString(3);
                roleId = reader.IsDBNull(4) ? null : reader.GetInt64(4);
            }

            if (!isActive || roleId == null ||
                !string.Equals(storedUsername, username, StringComparison.Ordinal) ||
                !string.Equals(storedStamp, expectedSecurityStamp, StringComparison.Ordinal) ||
                (password != null &&
                    (string.IsNullOrEmpty(storedHash) || !PasswordHasher.Verify(password, storedHash))))
            {
                tx.Rollback();
                return false;
            }

            using var permissionCommand = connection.CreateCommand();
            permissionCommand.Transaction = tx;
            permissionCommand.CommandText = @"SELECT access_level
                FROM role_permissions
                WHERE role_id=@roleId AND page_key=@pageKey
                LIMIT 1";
            permissionCommand.Parameters.AddWithValue("@roleId", roleId.Value);
            permissionCommand.Parameters.AddWithValue("@pageKey", PageKeys.Admin);
            var authorized = string.Equals(
                permissionCommand.ExecuteScalar()?.ToString(),
                "full",
                StringComparison.OrdinalIgnoreCase);
            tx.Commit();
            return authorized;
        }
        catch
        {
            try { tx.Rollback(); } catch { }
            throw;
        }
    }

    private static bool IsSameDatabase(ConnectionConfig first, ConnectionConfig? second)
    {
        if (second == null || first.Backend != second.Backend)
            return false;

        if (first.Backend == DatabaseBackend.SQLite)
        {
            if (string.IsNullOrWhiteSpace(first.FilePath) || string.IsNullOrWhiteSpace(second.FilePath))
                return false;

            return string.Equals(
                Path.GetFullPath(first.FilePath),
                Path.GetFullPath(second.FilePath),
                StringComparison.OrdinalIgnoreCase);
        }

        return first.Port == second.Port
            && string.Equals(first.Host.Trim(), second.Host.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(first.DatabaseName.Trim(), second.DatabaseName.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private void ShowError(string msg) { ErrorText.Text = msg; ErrorText.Visibility = Visibility.Visible; }
    private void ClearError() { ErrorText.Text = ""; ErrorText.Visibility = Visibility.Collapsed; }

    private static string FormatDatabaseError(Exception exception, string eventName)
    {
        var reference = AppLogger.LogException(eventName, exception);
        var mySqlException = FindException<MySqlException>(exception);

        if (FindException<System.Security.Authentication.AuthenticationException>(exception) != null)
            return $"Die TLS-Prüfung der Serververbindung ist fehlgeschlagen. Fehlerreferenz: {reference}";

        if (FindException<TimeoutException>(exception) != null)
            return $"Zeitüberschreitung beim Verbindungsaufbau. Bitte Server, Port und Firewall prüfen. Fehlerreferenz: {reference}";

        if (mySqlException != null)
        {
            var category = mySqlException.Number switch
            {
                1049 => "Die angegebene Datenbank existiert nicht. Bitte die Datenbank auf dem Server vorab anlegen und den Namen prüfen.",
                1045 => "Zugriff verweigert. Bitte DB-Benutzer, Passwort und die Hostfreigabe dieses Benutzers prüfen.",
                1044 => "Der DB-Benutzer hat keine Berechtigung für diese Datenbank.",
                1040 or 1203 => "Der Datenbankserver hat derzeit keine freie Verbindung. Bitte später erneut versuchen.",
                1042 or 2002 or 2003 => "Der MariaDB-Server ist nicht erreichbar. Bitte Host, Port und Firewall prüfen.",
                _ => $"MariaDB-Fehler {mySqlException.Number}."
            };

            return $"{category} Fehlerreferenz: {reference}";
        }

        return string.Format(LocalizationManager.Get("OperationFailedWithReference"), reference);
    }

    private static TException? FindException<TException>(Exception exception)
        where TException : Exception
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is TException match)
                return match;
        }

        return null;
    }

    private static string FormatMigrationSummary(MigrationResult result)
    {
        var populatedTables = result.TableCounts.Count(entry => entry.Value > 0);
        return $"{result.TotalRows} Datensätze aus {populatedTables} Tabellen migriert.";
    }

    private static string LogMigrationErrors(string eventName, MigrationResult result)
    {
        var details = result.Errors.Count == 0
            ? "Migration reported an unsuccessful result without error details."
            : string.Join(Environment.NewLine, result.Errors);

        return AppLogger.LogException(
            eventName,
            new InvalidOperationException(details),
            new
            {
                result.TotalRows,
                TableCount = result.TableCounts.Count(entry => entry.Value > 0),
                ErrorCount = result.Errors.Count
            });
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
        public UserSessionState? Session { get; set; }
        public bool RequiresDefaultPasswordChange { get; init; }
    }

    private sealed class DatabaseAlreadyExistsException : InvalidOperationException
    {
        public DatabaseAlreadyExistsException()
            : base("Die gewählte Datei existiert bereits. Bitte wählen Sie für eine neue Datenbank einen neuen Dateinamen.")
        {
        }
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
