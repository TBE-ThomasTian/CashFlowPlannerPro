using System.Windows;

namespace CashFlowPlannerPro.Views;

public partial class InputDialog : Window
{
    public string InputText => TbInput.Text.Trim();

    public InputDialog(string title, string label, string defaultValue = "")
    {
        InitializeComponent();
        Title = title;
        TbLabel.Text = label;
        TbInput.Text = defaultValue;
        if (Application.Current?.MainWindow != null) Owner = Application.Current.MainWindow;
        TbInput.Focus();
        TbInput.SelectAll();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
