using System.Windows;
using System.Windows.Input;
using CashFlowPlannerPro.Services;

namespace CashFlowPlannerPro.Views;

public partial class InputDialog : Window
{
    private readonly bool _isPassword;
    public string InputText => TbInput.Text.Trim();
    public string ResultText => _isPassword ? PbInput.Password : TbInput.Text.Trim();

    public InputDialog(string title, string label, string defaultValue = "", bool isPassword = false)
    {
        InitializeComponent();
        _isPassword = isPassword;
        Title = title;
        TbLabel.Text = label;

        if (isPassword)
        {
            TbInput.Visibility = Visibility.Collapsed;
            PbInput.Visibility = Visibility.Visible;
            PbInput.Focus();
        }
        else
        {
            TbInput.Text = defaultValue;
            TbInput.Focus();
            TbInput.SelectAll();
        }

        if (Application.Current?.MainWindow != null) Owner = Application.Current.MainWindow;
        OkBtn.ToolTip = TooltipService.Get("Btn_OK");
        CancelBtn.ToolTip = TooltipService.Get("Btn_Cancel");
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) DialogResult = false;
    }
}
