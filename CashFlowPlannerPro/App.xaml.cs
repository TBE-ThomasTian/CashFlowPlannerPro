using System.Windows;

namespace CashFlowPlannerPro;

public partial class App : Application
{
    public static string CurrentUsername { get; set; } = string.Empty;
    public static string DatabasePath { get; set; } = string.Empty;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        DispatcherUnhandledException += (s, ex) =>
        {
            MessageBox.Show(ex.Exception.ToString(), "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };

        var loginDialog = new Views.LoginDialog();
        var result = loginDialog.ShowDialog();
        if (result == true)
        {
            CurrentUsername = loginDialog.SelectedUsername;
            DatabasePath = loginDialog.SelectedDatabasePath;
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
