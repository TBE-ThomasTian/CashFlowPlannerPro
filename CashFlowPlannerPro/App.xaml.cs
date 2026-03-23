using System.Windows;

namespace CashFlowPlannerPro;

public partial class App : Application
{
    public static string CurrentUsername { get; set; } = string.Empty;
    public static string DatabasePath { get; set; } = string.Empty;
    public static Dictionary<string, string> Permissions { get; set; } = [];

    public static string GetAccess(string pageKey) =>
        Permissions.GetValueOrDefault(pageKey, "none");

    public static bool CanView(string pageKey) =>
        GetAccess(pageKey) is "read" or "full";

    public static bool CanEdit(string pageKey) =>
        GetAccess(pageKey) == "full";

    public static void LoadPermissions()
    {
        Permissions = Data.Database.Instance.GetUserPermissions(CurrentUsername);
        // Fallback: if no permissions found (e.g. no role assigned), admin gets full access
        if (Permissions.Count == 0 && CurrentUsername.Equals("admin", System.StringComparison.OrdinalIgnoreCase))
        {
            Permissions = new() {
                ["dashboard"] = "full", ["transactions"] = "full", ["fixkosten"] = "full",
                ["taxes"] = "full", ["invoices"] = "full", ["offers"] = "full",
                ["resources"] = "full", ["targets"] = "full", ["admin"] = "full"
            };
        }
    }

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        DispatcherUnhandledException += (s, ex) =>
        {
            Views.ModernMessageBox.ShowError(ex.Exception.Message, "Fehler");
            ex.Handled = true;
        };

        var loginDialog = new Views.LoginDialog();
        var result = loginDialog.ShowDialog();
        if (result == true)
        {
            CurrentUsername = loginDialog.SelectedUsername;
            DatabasePath = loginDialog.SelectedDatabasePath;
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
