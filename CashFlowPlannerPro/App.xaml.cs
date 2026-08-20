using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Services;

namespace CashFlowPlannerPro;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\CashFlowPlannerPro.SingleInstance";
    private static readonly string[] ObsoleteSqliteProgramFiles =
    [
        "e_sqlite3.dll",
        "Microsoft.Data.Sqlite.dll",
        "SQLitePCLRaw.batteries_v2.dll",
        "SQLitePCLRaw.core.dll",
        "SQLitePCLRaw.provider.e_sqlite3.dll",
        Path.Combine("runtimes", "win-x64", "native", "e_sqlite3.dll"),
        Path.Combine("sql", "migrations", "001_init.sql"),
        Path.Combine("sql", "migrations", "002_add_pdf_attachments.sql"),
        Path.Combine("sql", "migrations", "003_resources.sql"),
        Path.Combine("sql", "migrations", "004_add_project_number.sql")
    ];
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private int _fatalShutdownStarted;
    private static int _sessionInvalidationStarted;
    public static string CurrentUsername { get; set; } = string.Empty;
    public static long CurrentUserId { get; set; }
    public static string CurrentSecurityStamp { get; set; } = string.Empty;
    public static Data.ConnectionConfig? CurrentConnectionConfig { get; set; }
    public static Dictionary<string, string> Permissions { get; set; } = [];

    public static string GetAccess(string pageKey) =>
        Permissions.GetValueOrDefault(pageKey, "none");

    public static bool CanView(string pageKey) =>
        GetAccess(pageKey) is "read" or "full";

    public static bool CanEdit(string pageKey) =>
        GetAccess(pageKey) == "full";

    public static void ApplySessionState(UserSessionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        CurrentUserId = state.UserId;
        CurrentUsername = state.Username;
        CurrentSecurityStamp = state.SecurityStamp;
        Permissions = new Dictionary<string, string>(state.Permissions, StringComparer.Ordinal);
    }

    public static bool TryValidateCurrentSession(out UserSessionState? state)
    {
        state = null;
        if (CurrentUserId <= 0 || string.IsNullOrWhiteSpace(CurrentUsername) ||
            string.IsNullOrWhiteSpace(CurrentSecurityStamp))
            return false;

        state = Data.Database.Instance.GetUserSessionState(CurrentUserId);
        if (state is not { IsActive: true } ||
            !string.Equals(state.Username, CurrentUsername, StringComparison.Ordinal) ||
            !string.Equals(state.SecurityStamp, CurrentSecurityStamp, StringComparison.Ordinal))
            return false;

        Permissions = new Dictionary<string, string>(state.Permissions, StringComparer.Ordinal);
        return true;
    }

    public static void ClearSessionState()
    {
        CurrentUsername = string.Empty;
        CurrentUserId = 0;
        CurrentSecurityStamp = string.Empty;
        CurrentConnectionConfig = null;
        Permissions = [];
    }

    public static void InvalidateCurrentSession(string auditAction)
    {
        if (Interlocked.Exchange(ref _sessionInvalidationStarted, 1) != 0)
            return;

        AppLogger.Audit("session.invalidated", auditAction, success: false);
        try { Data.Database.Instance.Close(); }
        catch (Exception ex) { AppLogger.LogException("session.close_failed", ex); }
        ClearSessionState();

        void EndSession()
        {
            try
            {
                Views.ModernMessageBox.ShowError(
                    LocalizationManager.Get("SessionInvalidated"),
                    LocalizationManager.Get("AppErrorTitle"));
            }
            finally
            {
                Current?.Shutdown();
            }
        }

        if (Current?.Dispatcher.CheckAccess() == true)
            EndSession();
        else
            Current?.Dispatcher.BeginInvoke(EndSession);
    }

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (e.Args.Any(argument =>
                string.Equals(argument, "--security-smoke", StringComparison.Ordinal)))
        {
            var exitCode = SecuritySmokeRunner.Run();
            Environment.Exit(exitCode);
        }

        if (!TryAcquireSingleInstance())
        {
            MessageBox.Show(
                LocalizationManager.Get("ApplicationAlreadyRunning"),
                "CashFlow Planner Pro",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        Exit += (_, _) => ReleaseSingleInstance();
        RemoveObsoleteSqliteProgramFiles();
        UiScaleService.Initialize();
        LocalizationManager.LoadSavedLanguage();
        ApplyWpfCultureLanguage();

        DispatcherUnhandledException += (s, ex) =>
        {
            var reference = AppLogger.LogException("ui.unhandled", ex.Exception);
            if (Interlocked.Exchange(ref _fatalShutdownStarted, 1) == 0)
            {
                try
                {
                    Views.ModernMessageBox.ShowError(
                        $"Ein unerwarteter Fehler ist aufgetreten. Die Anwendung wird sicher beendet. Referenz: {reference}",
                        LocalizationManager.Get("AppErrorTitle"));
                }
                catch (Exception dialogException)
                {
                    AppLogger.LogException("ui.fatal_dialog_failed", dialogException, new { reference });
                }
            }

            // Do not continue in an unknown UI/data state after an exception that
            // escaped every page-level handler.
            ex.Handled = true;
            Shutdown(-1);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            if (ex.ExceptionObject is Exception exception)
                AppLogger.LogException("process.unhandled", exception, new { ex.IsTerminating });
        };

        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            AppLogger.LogException("task.unobserved", ex.Exception);
            ex.SetObserved();
        };

        var loginDialog = new Views.LoginDialog();
        var result = loginDialog.ShowDialog();
        if (result == true)
        {
            var username = loginDialog.SelectedUsername;
            var session = loginDialog.AuthenticatedSession;
            if (session is not { IsActive: true } ||
                !string.Equals(session.Username, username, StringComparison.Ordinal))
            {
                Views.ModernMessageBox.ShowError(
                    LocalizationManager.Get("SessionInvalidated"),
                    LocalizationManager.Get("AppErrorTitle"));
                Shutdown();
                return;
            }

            ApplySessionState(session);
            CurrentConnectionConfig = loginDialog.ActiveConnectionConfig?.Clone()
                ?? throw new InvalidOperationException("Die aktive MariaDB-Verbindung fehlt.");
            var mainWindow = new Views.MainWindow();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        else
        {
            Shutdown();
        }
    }

    private static void ApplyWpfCultureLanguage()
    {
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));
    }

    private static void RemoveObsoleteSqliteProgramFiles()
    {
        var appRoot = Path.GetFullPath(AppContext.BaseDirectory);
        var appRootPrefix = Path.TrimEndingDirectorySeparator(appRoot) + Path.DirectorySeparatorChar;

        foreach (var relativePath in ObsoleteSqliteProgramFiles)
        {
            try
            {
                var targetPath = Path.GetFullPath(Path.Combine(appRoot, relativePath));
                if (!targetPath.StartsWith(appRootPrefix, StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(targetPath))
                {
                    continue;
                }

                var attributes = File.GetAttributes(targetPath);
                if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                    continue;

                File.Delete(targetPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLogger.LogException(
                    "application.obsolete_sqlite_program_file_cleanup_failed",
                    ex,
                    new { file = relativePath });
            }
        }
    }

    private bool TryAcquireSingleInstance()
    {
        try
        {
            _singleInstanceMutex = new Mutex(
                initiallyOwned: true,
                SingleInstanceMutexName,
                out var createdNew);
            _ownsSingleInstanceMutex = createdNew;
            if (!createdNew)
            {
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
            }
            return createdNew;
        }
        catch (Exception ex)
        {
            AppLogger.LogException("application.single_instance_lock_failed", ex);
            return false;
        }
    }

    private void ReleaseSingleInstance()
    {
        if (_singleInstanceMutex == null)
            return;
        try
        {
            if (_ownsSingleInstanceMutex)
                _singleInstanceMutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // The process is already shutting down; disposal is sufficient.
        }
        finally
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            _ownsSingleInstanceMutex = false;
        }
    }
}
