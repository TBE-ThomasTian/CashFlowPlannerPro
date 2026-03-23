using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace CashFlowPlannerPro.Views;

public partial class MainWindow : Window
{
    private readonly Button[] _navButtons;
    private static readonly string[] NavPageKeys = [
        "dashboard", "transactions", "fixkosten", "taxes",
        "invoices", "offers", "resources", "targets", "admin"
    ];

    public MainWindow()
    {
        InitializeComponent();
        _navButtons = [Nav0, Nav1, Nav2, Nav3, Nav4, Nav5, Nav6, Nav7, Nav8];
        UpdateStatusBar();
        ApplyPermissions();
    }

    private void UpdateStatusBar()
    {
        var dbName = Path.GetFileName(App.DatabasePath);
        UserText.Text = App.CurrentUsername;
        DbText.Text = dbName;
    }

    private void ApplyPermissions()
    {
        int firstVisible = -1;
        for (int i = 0; i < _navButtons.Length && i < NavPageKeys.Length; i++)
        {
            bool canView = App.CanView(NavPageKeys[i]);
            _navButtons[i].Visibility = canView ? Visibility.Visible : Visibility.Collapsed;
            if (canView && firstVisible < 0) firstVisible = i;
        }
        // Navigate to first visible page
        if (firstVisible >= 0)
        {
            ContentTabs.SelectedIndex = firstVisible;
            SetActiveNav(firstVisible);
        }
    }

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            var parts = tag.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[1], out int idx))
            {
                ContentTabs.SelectedIndex = idx;
                SetActiveNav(idx);
            }
        }
    }

    private void SetActiveNav(int activeIndex)
    {
        for (int i = 0; i < _navButtons.Length; i++)
        {
            if (_navButtons[i].Visibility != Visibility.Visible) continue;
            _navButtons[i].Style = i == activeIndex
                ? (Style)FindResource("SidebarButtonActive")
                : (Style)FindResource("SidebarButton");
        }
    }

    private void SwitchDatabase_Click(object sender, RoutedEventArgs e)
    {
        var loginDialog = new LoginDialog();
        var result = loginDialog.ShowDialog();
        if (result == true)
        {
            App.CurrentUsername = loginDialog.SelectedUsername;
            App.DatabasePath = loginDialog.SelectedDatabasePath;
            App.LoadPermissions();
            var newWindow = new MainWindow();
            newWindow.Show();
            Close();
        }
    }

    private void UserSettings_Click(object sender, RoutedEventArgs e)
    {
        UserSettingsDialog.Show(App.CurrentUsername);
        UpdateStatusBar();
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        ModernMessageBox.Show("CashFlow Planner Pro\nVersion 2.0\n\nFinanzplanung leicht gemacht.",
            "Über CashFlow Planner Pro");
    }
}
