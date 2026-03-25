using System.Windows;
using System.Windows.Input;
using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Services;

namespace CashFlowPlannerPro.Views;

public partial class MariaDbConnectDialog : Window
{
    public ConnectionConfig Config { get; private set; } = new();

    public MariaDbConnectDialog()
    {
        InitializeComponent();
        if (Application.Current?.MainWindow != null) Owner = Application.Current.MainWindow;
        ConnectBtn.ToolTip = TooltipService.Get("Btn_OK");
        CancelBtn.ToolTip = TooltipService.Get("Btn_Cancel");
        TbHost.Focus();
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TbHost.Text))
        {
            ModernMessageBox.ShowError("Bitte einen Host eingeben.", "Fehler");
            return;
        }

        int.TryParse(TbPort.Text, out var port);
        Config = new ConnectionConfig
        {
            Backend = DatabaseBackend.MariaDB,
            Host = TbHost.Text.Trim(),
            Port = port > 0 ? port : 3306,
            DatabaseName = TbDatabase.Text.Trim(),
            DbUsername = TbUser.Text.Trim(),
            DbPassword = PbPassword.Password
        };

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) DialogResult = false;
    }
}
