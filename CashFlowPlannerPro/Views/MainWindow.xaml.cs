using System.IO;
using System.Windows;
using System.Windows.Controls;
using CashFlowPlannerPro.Services;

namespace CashFlowPlannerPro.Views;

public partial class MainWindow : Window
{
    private readonly Button[] _navButtons;
    private static readonly string[] NavPageKeys = [
        "dashboard", "transactions", "fixkosten", "taxes",
        "invoices", "offers", "resources", "targets", "todos", "timetracking", "kunden", "admin"
    ];

    public MainWindow()
    {
        InitializeComponent();
        _navButtons = [Nav0, Nav1, Nav2, Nav3, Nav4, Nav5, Nav6, Nav7, Nav8, Nav9, Nav10, Nav11];
        LocalizationManager.LanguageChanged += OnLanguageChanged;
        ApplyLocalization();
        UpdateStatusBar();
        ApplyPermissions();
        Closed += (_, _) => LocalizationManager.LanguageChanged -= OnLanguageChanged;
    }

    private void UpdateStatusBar()
    {
        var dbName = Path.GetFileName(App.DatabasePath);
        UserText.Text = App.CurrentUsername;
        DbText.Text = dbName;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        ApplyLocalization();
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
        Nav2.Content = LocalizationManager.Get("NavFixkosten");
        Nav3.Content = LocalizationManager.Get("NavTaxes");
        Nav4.Content = LocalizationManager.Get("NavInvoices");
        Nav5.Content = LocalizationManager.Get("NavOffers");
        Nav6.Content = LocalizationManager.Get("NavResources");
        Nav7.Content = LocalizationManager.Get("NavTargets");
        Nav8.Content = LocalizationManager.Get("NavTodos");
        Nav9.Content = LocalizationManager.Get("NavTimeTracking");
        Nav10.Content = LocalizationManager.Get("NavAddressBook");
        Nav11.Content = LocalizationManager.Get("NavAdmin");

        ProfileButton.Content = LocalizationManager.Get("ProfileButton");
        SwitchDatabaseButton.Content = LocalizationManager.Get("SwitchDatabaseButton");
        AboutButton.Content = LocalizationManager.Get("AboutButton");
        ExitButton.Content = LocalizationManager.Get("ExitButton");

        // Tooltips — Navigation
        Nav0.ToolTip = TooltipService.Get("Nav_Dashboard");
        Nav1.ToolTip = TooltipService.Get("Nav_Transactions");
        Nav2.ToolTip = TooltipService.Get("Nav_Fixkosten");
        Nav3.ToolTip = TooltipService.Get("Nav_Taxes");
        Nav4.ToolTip = TooltipService.Get("Nav_Invoices");
        Nav5.ToolTip = TooltipService.Get("Nav_Offers");
        Nav6.ToolTip = TooltipService.Get("Nav_Resources");
        Nav7.ToolTip = TooltipService.Get("Nav_Targets");
        Nav8.ToolTip = TooltipService.Get("Nav_Todos");
        Nav9.ToolTip = TooltipService.Get("Nav_TimeTracking");
        Nav10.ToolTip = TooltipService.Get("Nav_Customers");
        Nav11.ToolTip = TooltipService.Get("Nav_Admin");

        // Tooltips — System
        ProfileButton.ToolTip = TooltipService.Get("Nav_Profile");
        SwitchDatabaseButton.ToolTip = TooltipService.Get("Nav_SwitchDb");
        AboutButton.ToolTip = TooltipService.Get("Nav_About");
        ExitButton.ToolTip = TooltipService.Get("Nav_Exit");
    }

    private void ApplyPermissions()
    {
        int firstVisible = -1;
        for (int i = 0; i < _navButtons.Length && i < NavPageKeys.Length; i++)
        {
            bool canView = App.CanView(NavPageKeys[i]);
            _navButtons[i].Visibility = Visibility.Visible;
            _navButtons[i].IsEnabled = canView;
            _navButtons[i].Opacity = canView ? 1.0 : 0.45;
            if (canView && firstVisible < 0) firstVisible = i;
        }

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
        var loginDialog = new LoginDialog();
        var result = loginDialog.ShowDialog();
        if (result == true)
        {
            App.CurrentUsername = loginDialog.SelectedUsername;
            App.CurrentUserId = Data.Database.Instance.GetUserId(loginDialog.SelectedUsername);
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
        ModernMessageBox.Show(
            LocalizationManager.Get("AboutText"),
            LocalizationManager.Get("AboutTitle"));
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }
}
