using System.Windows;
using CashFlowPlannerPro.Services;

namespace CashFlowPlannerPro;

public partial class App : Application
{
    public static string CurrentUsername { get; set; } = string.Empty;
    public static long CurrentUserId { get; set; }
    public static string DatabasePath { get; set; } = string.Empty;
    public static bool IsDemoMode { get; set; }
    public static Data.ConnectionConfig? CurrentConnectionConfig { get; set; }
    public static Dictionary<string, string> Permissions { get; set; } = [];

    public static string GetAccess(string pageKey)
    {
        if (CurrentUsername.Equals("admin", System.StringComparison.OrdinalIgnoreCase))
            return "full";
        return Permissions.GetValueOrDefault(pageKey, "none");
    }

    public static bool CanView(string pageKey) =>
        GetAccess(pageKey) is "read" or "full";

    public static bool CanEdit(string pageKey) =>
        GetAccess(pageKey) == "full";

    public static void LoadPermissions()
    {
        Permissions = Data.Database.Instance.GetUserPermissions(CurrentUsername);
    }

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        LocalizationManager.LoadSavedLanguage();

        DispatcherUnhandledException += (s, ex) =>
        {
            Views.ModernMessageBox.ShowError(ex.Exception.Message, LocalizationManager.Get("AppErrorTitle"));
            ex.Handled = true;
        };

        var loginDialog = new Views.LoginDialog();
        var result = loginDialog.ShowDialog();
        if (result == true)
        {
            CurrentUsername = loginDialog.SelectedUsername;
            CurrentUserId = Data.Database.Instance.GetUserId(CurrentUsername);
            DatabasePath = loginDialog.SelectedDatabasePath;
            IsDemoMode = loginDialog.IsDemoSession;
            CurrentConnectionConfig = loginDialog.ActiveConnectionConfig?.Clone();
            LoadPermissions();
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
}
