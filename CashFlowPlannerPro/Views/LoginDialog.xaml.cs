using System.ComponentModel;
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
using MySqlConnector;

namespace CashFlowPlannerPro.Views;

public partial class LoginDialog : Window
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CashFlowPlannerPro");
    private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");

    private List<string> _usernames = [];
    private bool _allowClose;
    private bool _isBusy;

    public string SelectedUsername { get; private set; } = string.Empty;
    public UserSessionState? AuthenticatedSession { get; private set; }
    public ConnectionConfig? ActiveConnectionConfig { get; private set; }

    public LoginDialog() : this(null, null, 0, null)
    {
    }

    internal LoginDialog(
        ConnectionConfig? initialConnection,
        string? initialUsername,
        long initialUserId,
        string? initialSecurityStamp)
    {
        // The identity arguments remain part of the internal API used by the
        // fail-closed database switch. The new login still authenticates the
        // selected user against the target MariaDB database from scratch.
        _ = initialUserId;
        _ = initialSecurityStamp;

        InitializeComponent();
        LocalizationManager.LanguageChanged += OnLanguageChanged;
        LoadSettings();

        if (initialConnection != null)
            ApplyConnectionSettings(initialConnection);
        if (!string.IsNullOrWhiteSpace(initialUsername))
            UsernameTextBox.Text = initialUsername.Trim();

        ApplyLocalization();
        UpdateConnectionExpanderState();
        if (initialConnection != null)
            DatabaseExpander.IsExpanded = true;

        TestConnectionBtn.ToolTip = TooltipService.Get("Btn_TestConnection");
        LoginButton.ToolTip = TooltipService.Get("Btn_Login");
        BusyText.Text = LocalizationManager.Get("LoadingPleaseWait");
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"v{version?.Major}.{version?.Minor}.{version?.Build}";

        UsernameTextBox.TextChanged += (_, _) => UpdateUsernamePlaceholder();
        UsernameTextBox.GotFocus += (_, _) =>
        {
            if (_usernames.Count > 0)
                UsernamePopup.IsOpen = true;
        };
        UpdateUsernamePlaceholder();

        Closing += LoginDialog_Closing;
        Closed += (_, _) => LocalizationManager.LanguageChanged -= OnLanguageChanged;
    }

    private void LoginDialog_Closing(object? sender, CancelEventArgs e)
    {
        if (_isBusy && !_allowClose)
            e.Cancel = true;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        ApplyLocalization();
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

                if (!string.IsNullOrWhiteSpace(secure.AppUsername))
                    UsernameTextBox.Text = secure.AppUsername.Trim();
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogException("connection.settings.load_failed", ex);
        }
    }

    private void ApplyBasicSettings(AppSettings settings)
    {
        TbMariaHost.Text = settings.MariaDbHost ?? TbMariaHost.Text;
        TbMariaPort.Text = (settings.MariaDbPort > 0 ? settings.MariaDbPort : 3306).ToString();
        TbMariaDatabase.Text = settings.MariaDbDatabase ?? TbMariaDatabase.Text;
        TbMariaUser.Text = settings.MariaDbUsername ?? string.Empty;
    }

    private void ApplySecureSettings(SecureConnectionData secure)
    {
        TbMariaHost.Text = secure.Host ?? TbMariaHost.Text;
        TbMariaPort.Text = (secure.Port > 0 ? secure.Port : 3306).ToString();
        TbMariaDatabase.Text = secure.DatabaseName ?? TbMariaDatabase.Text;
        TbMariaUser.Text = secure.DbUsername ?? TbMariaUser.Text;
        PbMariaPassword.Password = secure.DbPassword ?? string.Empty;
    }

    private void ApplyConnectionSettings(ConnectionConfig config)
    {
        TbMariaHost.Text = config.Host;
        TbMariaPort.Text = (config.Port > 0 ? config.Port : 3306).ToString();
        TbMariaDatabase.Text = config.DatabaseName;
        TbMariaUser.Text = config.DbUsername;
        PbMariaPassword.Password = config.DbPassword;
    }

    private void SaveSettings()
    {
        try
        {
            SaveBasicSettings();

            if (ChkRememberSettings.IsChecked == true)
            {
                int.TryParse(TbMariaPort.Text, out var port);
                var secure = new SecureConnectionData
                {
                    RememberSettings = true,
                    AppUsername = UsernameTextBox.Text?.Trim(),
                    Host = TbMariaHost.Text.Trim(),
                    Port = port > 0 ? port : 3306,
                    DatabaseName = TbMariaDatabase.Text.Trim(),
                    DbUsername = TbMariaUser.Text.Trim(),
                    DbPassword = PbMariaPassword.Password
                };

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
            MariaDbHost = TbMariaHost.Text.Trim(),
            MariaDbPort = port > 0 ? port : 3306,
            MariaDbDatabase = TbMariaDatabase.Text.Trim(),
            MariaDbUsername = TbMariaUser.Text.Trim()
        };

        var json = JsonSerializer.Serialize(settings);
        File.WriteAllText(SettingsFile, json);
    }

    private void ApplyLocalization()
    {
        Title = LocalizationManager.Get("LoginWindowTitle");
        TitleText.Text = LocalizationManager.Get("MainTitle");
        SubtitleText.Text = LocalizationManager.Get("LoginSubtitle");
        DatabaseStepText.Text = LocalizationManager.Get("LoginStepDatabase");
        SignInStepText.Text = LocalizationManager.Get("LoginStepSignIn");
        TbMariaUser.ToolTip = LocalizationManager.Get("MariaDbDedicatedUserHint");
        LoginButton.Content = LocalizationManager.Get("LoginButton");
        UsernamePlaceholder.Text = LocalizationManager.Get("LoginUsernamePlaceholder");
        BusyText.Text = LocalizationManager.Get("LoadingPleaseWait");
    }

    private void UpdateConnectionExpanderState()
    {
        if (DatabaseExpander == null)
            return;

        DatabaseExpander.IsExpanded = string.IsNullOrWhiteSpace(TbMariaHost.Text)
            || string.IsNullOrWhiteSpace(TbMariaDatabase.Text)
            || string.IsNullOrWhiteSpace(TbMariaUser.Text);
    }

    private void UpdateUsernamePlaceholder()
    {
        UsernamePlaceholder.Visibility = string.IsNullOrEmpty(UsernameTextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private bool TryBuildConnectionConfig(out ConnectionConfig config)
    {
        config = null!;
        var host = TbMariaHost.Text.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            ShowError("Bitte einen MariaDB-Server eingeben.");
            return false;
        }

        if (!int.TryParse(TbMariaPort.Text, out var port) || port is < 1 or > 65535)
        {
            ShowError("Bitte einen gültigen MariaDB-Port zwischen 1 und 65535 eingeben.");
            return false;
        }

        var databaseName = TbMariaDatabase.Text.Trim();
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            ShowError("Bitte den Namen der MariaDB-Datenbank eingeben.");
            return false;
        }

        var databaseUsername = TbMariaUser.Text.Trim();
        if (string.IsNullOrWhiteSpace(databaseUsername))
        {
            ShowError("Bitte einen MariaDB-Benutzer eingeben.");
            return false;
        }

        config = new ConnectionConfig
        {
            Host = host,
            Port = port,
            DatabaseName = databaseName,
            DbUsername = databaseUsername,
            DbPassword = PbMariaPassword.Password
        };
        return true;
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
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

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildConnectionConfig(out var config))
            return;

        var username = UsernameTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(username))
        {
            ShowError(LocalizationManager.Get("LoginUsernameRequired"));
            return;
        }

        var password = PasswordBox.Password;
        if (string.IsNullOrEmpty(password))
        {
            ShowError(LocalizationManager.Get("LoginPasswordRequired"));
            return;
        }

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
                new { retryAfterSeconds, backend = "MariaDB" });
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
                    RequiresDefaultPasswordChange = session != null
                        && Database.Instance.IsFirstRun
                        && username == "admin"
                        && password == "admin"
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
                    var passwordDialog = new PasswordSetupDialog(username)
                    {
                        Owner = this
                    };
                    if (passwordDialog.ShowDialog() == true)
                    {
                        SetBusy(true);
                        loginResult.Session = await Task.Run(() => Database.Instance.ChangePassword(
                            loginResult.Session.UserId,
                            loginResult.Session.SecurityStamp,
                            password,
                            passwordDialog.Password));
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
                    new { backend = "MariaDB" });
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
                        backend = "MariaDB"
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
                new { backend = "MariaDB" });
            AppLogger.Audit(
                "login.failed",
                username,
                success: false,
                new { reason = "operation_error", reference, backend = "MariaDB" });
            ShowError(string.Format(
                LocalizationManager.Get("LoginErrorWithReference"),
                reference));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void TestMariaDbConnection_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildConnectionConfig(out var config))
            return;

        try
        {
            SetBusy(true);
            _usernames = await Task.Run(() =>
            {
                Database.Instance.Open(config);
                Database.Instance.EnsureSchema();
                return Database.Instance.GetUsernames();
            });
            UsernameListBox.Items.Clear();
            foreach (var username in _usernames)
                UsernameListBox.Items.Add(username);

            ClearError();
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

    private static string BuildLoginThrottleKey(ConnectionConfig config, string username)
    {
        var endpoint = $"{config.Host.Trim().ToUpperInvariant()}:{config.Port}/{config.DatabaseName.Trim().ToUpperInvariant()}";
        var material = $"MariaDB\n{endpoint}\n{username.Trim().ToUpperInvariant()}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void ClearError()
    {
        ErrorText.Text = string.Empty;
        ErrorText.Visibility = Visibility.Collapsed;
    }

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

    private sealed class AppSettings
    {
        public string? MariaDbHost { get; set; }
        public int MariaDbPort { get; set; } = 3306;
        public string? MariaDbDatabase { get; set; }
        public string? MariaDbUsername { get; set; }
    }
}
