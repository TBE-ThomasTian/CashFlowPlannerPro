using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace CashFlowPlannerPro.Views;

public partial class MainWindow : Window
{
    private readonly Button[] _navButtons;

    public MainWindow()
    {
        InitializeComponent();
        _navButtons = [Nav0, Nav1, Nav2, Nav3, Nav4, Nav5, Nav6, Nav7];
        UpdateStatusBar();
        SetActiveNav(0);
    }

    private void UpdateStatusBar()
    {
        var dbName = Path.GetFileName(App.DatabasePath);
        UserText.Text = App.CurrentUsername;
        DbText.Text = dbName;
    }

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag && int.TryParse(tag, out int idx))
        {
            ContentTabs.SelectedIndex = idx;
            SetActiveNav(idx);
        }
    }

    private void SetActiveNav(int activeIndex)
    {
        for (int i = 0; i < _navButtons.Length; i++)
        {
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
