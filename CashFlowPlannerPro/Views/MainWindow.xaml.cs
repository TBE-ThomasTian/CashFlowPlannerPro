using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Services;
using Microsoft.Win32;

namespace CashFlowPlannerPro.Views;

public partial class MainWindow : Window
{
    private readonly Button[] _navButtons;
    private static readonly string[] NavPageKeys = [
        "dashboard", "transactions", "bank", "fixkosten", "taxes",
        "invoices", "offers", "resources", "targets", "todos", "timetracking", "kunden", "integrations", "admin"
    ];

    public MainWindow()
    {
        InitializeComponent();
        _navButtons = [Nav0, Nav1, Nav2, Nav3, Nav4, Nav5, Nav6, Nav7, Nav8, Nav9, Nav10, Nav11, Nav12, Nav13];
        LocalizationManager.LanguageChanged += OnLanguageChanged;
        ApplyLocalization();
        UpdateStatusBar();
        ApplyPermissions();
        Loaded += MainWindow_Loaded;
        Closed += (_, _) => LocalizationManager.LanguageChanged -= OnLanguageChanged;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        if (ContentTabs.SelectedIndex >= 0)
            await RefreshSelectedPageAsync(ContentTabs.SelectedIndex);
    }

    private void UpdateStatusBar()
    {
        var dbName = GetDatabaseDisplayName();
        UserText.Text = App.CurrentUsername;
        DbText.Text = App.IsDemoMode
            ? $"{dbName} · {LocalizationManager.Get("DemoMode")}"
            : dbName;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        ApplyLocalization();
        UpdateStatusBar();
    }

    private static string GetDatabaseDisplayName()
    {
        var config = App.CurrentConnectionConfig;
        if (config?.Backend == Data.DatabaseBackend.MariaDB)
            return $"Server: {config.Host}/{config.DatabaseName}";

        var path = config?.FilePath;
        if (string.IsNullOrWhiteSpace(path))
            path = App.DatabasePath;

        var fileName = Path.GetFileName(path);
        return string.IsNullOrWhiteSpace(fileName) ? "Lokale Datenbank" : $"Lokal: {fileName}";
    }

    private void ApplyLocalization()
    {
        Title = LocalizationManager.Get("MainTitle");
        MainTitleText.Text = "CashFlow";
        MainSubtitleText.Text = "Planner Pro";
        SystemSectionText.Text = LocalizationManager.Get("SystemSection");
        NavigationSectionText.Text = LocalizationManager.Get("NavigationSection");

        Nav0.Content = LocalizationManager.Get("NavDashboard");
        Nav1.Content = LocalizationManager.Get("NavTransactions");
        Nav2.Content = LocalizationManager.Get("NavBank");
        Nav3.Content = LocalizationManager.Get("NavFixkosten");
        Nav4.Content = LocalizationManager.Get("NavTaxes");
        Nav5.Content = LocalizationManager.Get("NavInvoices");
        Nav6.Content = LocalizationManager.Get("NavOffers");
        Nav7.Content = LocalizationManager.Get("NavResources");
        Nav8.Content = LocalizationManager.Get("NavTargets");
        Nav9.Content = LocalizationManager.Get("NavTodos");
        Nav10.Content = LocalizationManager.Get("NavTimeTracking");
        Nav11.Content = LocalizationManager.Get("NavAddressBook");
        Nav12.Content = LocalizationManager.Get("NavIntegrations");
        Nav13.Content = LocalizationManager.Get("NavAdmin");

        SettingsButton.Content = LocalizationManager.Get("SettingsButton");
        ProfileButton.Content = LocalizationManager.Get("ProfileButton");
        SwitchDatabaseButton.Content = LocalizationManager.Get("SwitchDatabaseButton");
        BackupButton.Content = LocalizationManager.Get("BackupButton");
        RestoreButton.Content = LocalizationManager.Get("RestoreButton");
        CheckUpdatesButton.Content = LocalizationManager.Get("CheckUpdatesButton");
        AboutButton.Content = LocalizationManager.Get("AboutButton");
        ExitButton.Content = LocalizationManager.Get("ExitButton");

        // Tooltips — Navigation
        Nav0.ToolTip = TooltipService.Get("Nav_Dashboard");
        Nav1.ToolTip = TooltipService.Get("Nav_Transactions");
        Nav2.ToolTip = TooltipService.Get("Nav_Bank");
        Nav3.ToolTip = TooltipService.Get("Nav_Fixkosten");
        Nav4.ToolTip = TooltipService.Get("Nav_Taxes");
        Nav5.ToolTip = TooltipService.Get("Nav_Invoices");
        Nav6.ToolTip = TooltipService.Get("Nav_Offers");
        Nav7.ToolTip = TooltipService.Get("Nav_Resources");
        Nav8.ToolTip = TooltipService.Get("Nav_Targets");
        Nav9.ToolTip = TooltipService.Get("Nav_Todos");
        Nav10.ToolTip = TooltipService.Get("Nav_TimeTracking");
        Nav11.ToolTip = TooltipService.Get("Nav_Customers");
        Nav12.ToolTip = TooltipService.Get("Nav_Integrations");
        Nav13.ToolTip = TooltipService.Get("Nav_Admin");

        // Tooltips — System
        SettingsButton.ToolTip = TooltipService.Get("Nav_Settings");
        ProfileButton.ToolTip = TooltipService.Get("Nav_Profile");
        SwitchDatabaseButton.ToolTip = TooltipService.Get("Nav_SwitchDb");
        BackupButton.ToolTip = LocalizationManager.Get("BackupButton");
        RestoreButton.ToolTip = LocalizationManager.Get("RestoreButton");
        CheckUpdatesButton.ToolTip = LocalizationManager.Get("CheckUpdatesButton");
        AboutButton.ToolTip = TooltipService.Get("Nav_About");
        ExitButton.ToolTip = TooltipService.Get("Nav_Exit");
    }

    private void ApplyPermissions()
    {
        int firstVisible = -1;
        for (int i = 0; i < _navButtons.Length && i < NavPageKeys.Length; i++)
        {
            bool canView = App.CanView(NavPageKeys[i]);
            _navButtons[i].Visibility = canView ? Visibility.Visible : Visibility.Collapsed;
            _navButtons[i].IsEnabled = canView;
            _navButtons[i].Opacity = 1.0;
            if (ContentTabs.Items[i] is TabItem tab)
            {
                tab.IsEnabled = canView;
                tab.Visibility = canView ? Visibility.Visible : Visibility.Collapsed;
                if (!canView)
                    tab.Content = null;
            }
            if (canView && firstVisible < 0) firstVisible = i;
        }

        if (firstVisible >= 0)
        {
            EnsurePageCreated(firstVisible);
            ContentTabs.SelectedIndex = firstVisible;
            SetActiveNav(firstVisible);
        }

        var canAdministerDatabase = App.CanEdit(PageKeys.Admin) && !App.IsDemoMode;
        BackupButton.IsEnabled = canAdministerDatabase;
        RestoreButton.IsEnabled = canAdministerDatabase;
        BackupButton.Opacity = canAdministerDatabase ? 1.0 : 0.45;
        RestoreButton.Opacity = canAdministerDatabase ? 1.0 : 0.45;
    }

    private async void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (!PermissionGuard.EnsureSessionValid("navigation"))
            return;

        if (sender is Button btn && btn.Tag is string tag)
        {
            var parts = tag.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[1], out int idx))
            {
                if (idx < 0 || idx >= NavPageKeys.Length || !App.CanView(NavPageKeys[idx]))
                    return;
                EnsurePageCreated(idx);
                ContentTabs.SelectedIndex = idx;
                SetActiveNav(idx);
                await RefreshSelectedPageAsync(idx);
            }
        }

        var canAdministerDatabase = App.CanEdit(PageKeys.Admin) && !App.IsDemoMode;
        BackupButton.IsEnabled = canAdministerDatabase;
        RestoreButton.IsEnabled = canAdministerDatabase;
        BackupButton.Opacity = canAdministerDatabase ? 1.0 : 0.45;
        RestoreButton.Opacity = canAdministerDatabase ? 1.0 : 0.45;
    }

    private void EnsurePageCreated(int index)
    {
        if (index < 0 || index >= ContentTabs.Items.Count ||
            ContentTabs.Items[index] is not TabItem tab || tab.Content != null)
            return;

        tab.Content = index switch
        {
            0 => new DashboardView(),
            1 => new TransactionsView(),
            2 => new BankView(),
            3 => new FixkostenView(),
            4 => new TaxesView(),
            5 => new InvoicesView(),
            6 => new OffersView(),
            7 => new ResourcesView(),
            8 => new TargetsView(),
            9 => new TodoView(),
            10 => new TimeTrackingView(),
            11 => new CustomersView(),
            12 => new IntegrationsView(),
            13 => new AdminView(),
            _ => null
        };
    }

    private async Task RefreshSelectedPageAsync(int index)
    {
        if (index == 2 && ContentTabs.Items[index] is TabItem { Content: BankView bankView })
            await bankView.ActivateAsync();

        if (index == 7 && ContentTabs.Items[index] is TabItem { Content: ResourcesView resourcesView })
            resourcesView.Reload();
    }

    private void SetActiveNav(int activeIndex)
    {
        for (int i = 0; i < _navButtons.Length; i++)
        {
            if (!_navButtons[i].IsEnabled)
            {
                _navButtons[i].Style = (Style)FindResource("SidebarButton");
                continue;
            }
            _navButtons[i].Style = i == activeIndex
                ? (Style)FindResource("SidebarButtonActive")
                : (Style)FindResource("SidebarButton");
        }
    }

    private void SwitchDatabase_Click(object sender, RoutedEventArgs e)
    {
        if (!PermissionGuard.EnsureSessionValid("database.switch"))
            return;

        var previousSession = CaptureSession();
        var loginDialog = new LoginDialog(
            previousSession.ConnectionConfig,
            previousSession.Username,
            previousSession.UserId,
            previousSession.SecurityStamp);

        var switchCompleted = false;
        Exception? switchException = null;
        string? switchErrorReference = null;
        try
        {
            if (loginDialog.ShowDialog() == true)
            {
                ApplyAuthenticatedSession(loginDialog);
                var newWindow = new MainWindow();
                newWindow.Show();
                switchCompleted = true;
                Close();
            }
        }
        catch (Exception ex)
        {
            switchException = ex;
            switchErrorReference = AppLogger.LogException("database.switch_failed", ex);
        }

        if (switchCompleted)
            return;

        if (loginDialog.RequiresFreshAuthenticationAfterMigration)
        {
            ClearSession();
            ModernMessageBox.Show(
                LocalizationManager.Get("MigrationFreshLoginRequired"),
                LocalizationManager.Get("MigrationConfirmTitle"));
            Application.Current.Shutdown();
            return;
        }

        try
        {
            RestoreSession(previousSession);
            UpdateStatusBar();

            if (switchException != null)
            {
                ModernMessageBox.ShowError(
                    string.Format(
                        LocalizationManager.Get("SwitchDatabaseFailed"),
                        string.Format(
                            LocalizationManager.Get("ErrorReferenceValue"),
                            switchErrorReference ?? "-")),
                    LocalizationManager.Get("SwitchDatabaseTitle"));
            }
        }
        catch (Exception restoreException)
        {
            // Never leave the old privileged UI attached to whichever candidate
            // database happened to be open when restoration failed.
            ClearSession();
            var reference = AppLogger.LogException("database.switch_restore_failed", restoreException);

            ModernMessageBox.ShowError(
                string.Format(
                    LocalizationManager.Get("SwitchDatabaseRestoreFailed"),
                    string.Format(LocalizationManager.Get("ErrorReferenceValue"), reference)),
                LocalizationManager.Get("SwitchDatabaseTitle"));
            Application.Current.Shutdown();
        }
    }

    private static void ClearSession()
    {
        Data.Database.Instance.Close();
        App.ClearSessionState();
    }

    private static SessionSnapshot CaptureSession()
    {
        var config = App.CurrentConnectionConfig?.Clone();
        if (config == null && !string.IsNullOrWhiteSpace(App.DatabasePath))
        {
            config = new Data.ConnectionConfig
            {
                Backend = Data.DatabaseBackend.SQLite,
                FilePath = App.DatabasePath
            };
        }

        if (config == null)
            throw new InvalidOperationException(LocalizationManager.Get("SwitchDatabaseNoActiveSession"));

        return new SessionSnapshot(
            config,
            App.CurrentUsername,
            App.CurrentUserId,
            App.DatabasePath,
            App.IsDemoMode,
            App.CurrentSecurityStamp,
            new Dictionary<string, string>(App.Permissions, StringComparer.Ordinal));
    }

    private static void ApplyAuthenticatedSession(LoginDialog loginDialog)
    {
        var config = loginDialog.ActiveConnectionConfig?.Clone()
            ?? throw new InvalidOperationException(LocalizationManager.Get("SwitchDatabaseTargetMissing"));
        var username = loginDialog.SelectedUsername;
        var session = loginDialog.AuthenticatedSession;
        if (session is not { IsActive: true } ||
            !string.Equals(session.Username, username, StringComparison.Ordinal))
            throw new UnauthorizedAccessException(LocalizationManager.Get("SessionInvalidated"));
        var databasePath = loginDialog.SelectedDatabasePath;
        var isDemoMode = loginDialog.IsDemoSession;

        App.CurrentConnectionConfig = config;
        App.ApplySessionState(session);
        App.DatabasePath = databasePath;
        App.IsDemoMode = isDemoMode;
    }

    private static void RestoreSession(SessionSnapshot snapshot)
    {
        Data.Database.Instance.Open(snapshot.ConnectionConfig);
        Data.Database.Instance.EnsureSchema();

        // Complete all operations that can fail before publishing the restored
        // process-wide session values.
        var restoredUserId = Data.Database.Instance.GetUserId(snapshot.Username);
        if (restoredUserId <= 0)
            throw new UnauthorizedAccessException("Der bisherige Benutzer existiert in der vorherigen Datenbank nicht mehr.");
        var restoredSession = Data.Database.Instance.GetUserSessionState(restoredUserId);
        if (restoredUserId != snapshot.UserId ||
            restoredSession is not { IsActive: true } ||
            !string.Equals(restoredSession.Username, snapshot.Username, StringComparison.Ordinal) ||
            !string.Equals(restoredSession.SecurityStamp, snapshot.SecurityStamp, StringComparison.Ordinal))
            throw new UnauthorizedAccessException(LocalizationManager.Get("SessionInvalidated"));

        App.CurrentConnectionConfig = snapshot.ConnectionConfig.Clone();
        App.ApplySessionState(restoredSession);
        App.DatabasePath = snapshot.DatabasePath;
        App.IsDemoMode = snapshot.IsDemoMode;
    }

    private sealed record SessionSnapshot(
        Data.ConnectionConfig ConnectionConfig,
        string Username,
        long UserId,
        string DatabasePath,
        bool IsDemoMode,
        string SecurityStamp,
        Dictionary<string, string> Permissions);

    private void UserSettings_Click(object sender, RoutedEventArgs e)
    {
        UserSettingsDialog.Show(App.CurrentUsername);
        UpdateStatusBar();
    }

    private void ApplicationSettings_Click(object sender, RoutedEventArgs e)
    {
        ApplicationSettingsDialog.ShowDialogWindow();
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        ModernMessageBox.Show(
            LocalizationManager.Get("AboutText"),
            LocalizationManager.Get("AboutTitle"));
    }

    private void Backup_Click(object sender, RoutedEventArgs e)
    {
        if (App.IsDemoMode || !PermissionGuard.DemandEdit(PageKeys.Admin, "database.backup.create"))
        {
            ModernMessageBox.ShowError(
                "Backups dürfen nur Benutzer mit vollständigem Verwaltungszugriff erstellen.",
                LocalizationManager.Get("AppErrorTitle"));
            return;
        }

        if (!BackupService.SupportsFileBackup())
        {
            ModernMessageBox.Show(
                LocalizationManager.Get("BackupSQLiteOnly"),
                LocalizationManager.Get("BackupTitle"));
            return;
        }

        var sourcePath = App.CurrentConnectionConfig!.FilePath;
        var dialog = new SaveFileDialog
        {
            Title = LocalizationManager.Get("BackupDialogTitle"),
            Filter = LocalizationManager.Get("BackupDialogFilter"),
            DefaultExt = ".db",
            FileName = $"{Path.GetFileNameWithoutExtension(sourcePath)}-backup-{DateTime.Now:yyyyMMdd-HHmmss}.db"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            if (!PermissionGuard.DemandEdit(PageKeys.Admin, "database.backup.create.confirmed"))
                return;
            BackupService.CreateBackup(dialog.FileName);
            AppLogger.Audit("database.backup.created", Path.GetFileName(dialog.FileName), success: true);
            ModernMessageBox.Show(
                string.Format(LocalizationManager.Get("BackupSuccess"), dialog.FileName),
                LocalizationManager.Get("BackupTitle"));
            UpdateStatusBar();
        }
        catch (Exception ex)
        {
            var reference = AppLogger.LogException("database.backup_failed", ex);
            if (!PermissionGuard.EnsureSessionValid("database.backup.failure"))
                return;
            ModernMessageBox.ShowError(
                string.Format(
                    LocalizationManager.Get("BackupFailed"),
                    string.Format(LocalizationManager.Get("ErrorReferenceValue"), reference)),
                LocalizationManager.Get("BackupTitle"));
        }
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (App.IsDemoMode || !PermissionGuard.DemandEdit(PageKeys.Admin, "database.restore"))
        {
            ModernMessageBox.ShowError(
                "Backups dürfen nur Benutzer mit vollständigem Verwaltungszugriff wiederherstellen.",
                LocalizationManager.Get("AppErrorTitle"));
            return;
        }

        if (!BackupService.SupportsFileBackup())
        {
            ModernMessageBox.Show(
                LocalizationManager.Get("BackupSQLiteOnly"),
                LocalizationManager.Get("RestoreTitle"));
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = LocalizationManager.Get("RestoreDialogTitle"),
            Filter = LocalizationManager.Get("BackupDialogFilter"),
            DefaultExt = ".db"
        };

        if (dialog.ShowDialog() != true)
            return;

        if (!ModernMessageBox.ShowConfirm(
            string.Format(LocalizationManager.Get("RestoreConfirm"), Path.GetFileName(dialog.FileName)),
            LocalizationManager.Get("RestoreTitle")))
            return;

        try
        {
            if (!PermissionGuard.DemandEdit(PageKeys.Admin, "database.restore.confirmed"))
                return;
            var auditActorUsername = App.CurrentUsername;
            var auditActorUserId = App.CurrentUserId;
            BackupService.RestoreBackup(dialog.FileName);
            AppLogger.AuditAs(
                "database.restore.completed",
                Path.GetFileName(dialog.FileName),
                success: true,
                auditActorUsername,
                auditActorUserId);
        }
        catch (Exception ex)
        {
            var reference = AppLogger.LogException("database.restore_failed", ex);
            if (!PermissionGuard.EnsureSessionValid("database.restore.failure"))
                return;
            ModernMessageBox.ShowError(
                string.Format(LocalizationManager.Get("RestoreFailed"), $"Referenz: {reference}"),
                LocalizationManager.Get("RestoreTitle"));
            return;
        }

        // The restored file has a potentially different user/role universe.
        // Never keep the current authenticated UI alive against that database.
        ClearSession();
        ModernMessageBox.Show(
            LocalizationManager.Get("RestoreSuccessRestart"),
            LocalizationManager.Get("RestoreTitle"));
        RestartApplicationFailClosed();
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (await UpdateService.CheckForUpdatesAsync())
            Application.Current.Shutdown();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private static void RestartApplicationFailClosed()
    {
        try
        {
            var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(exePath))
                throw new InvalidOperationException("Der Anwendungspfad wurde nicht gefunden.");

            if (Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true
            }) == null)
                throw new InvalidOperationException("Die Anwendung konnte nicht neu gestartet werden.");
        }
        catch (Exception ex)
        {
            var reference = AppLogger.LogException("application.restart_after_restore_failed", ex);
            ModernMessageBox.ShowError(
                $"Die Datenbank wurde wiederhergestellt, aber die Anwendung konnte nicht automatisch neu gestartet werden. " +
                $"Bitte starten Sie sie manuell. Fehlerreferenz: {reference}",
                LocalizationManager.Get("AppErrorTitle"));
        }
        finally
        {
            Application.Current.Shutdown();
        }
    }
}
