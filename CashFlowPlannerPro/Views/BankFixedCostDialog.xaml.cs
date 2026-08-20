using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace CashFlowPlannerPro.Views;

public partial class BankFixedCostDialog : Window
{
    private static readonly string[] AllowedIntervals =
        ["einmalig", "monatlich", "vierteljährlich", "halbjährlich", "jährlich"];

    public BankFixedCostDialog(
        string bookingDate,
        double amount,
        string suggestedDescription,
        IEnumerable<string> categories)
    {
        InitializeComponent();
        DateText.Text = bookingDate;
        AmountText.Text = amount.ToString("N2", CultureInfo.GetCultureInfo("de-DE")) + " EUR";
        DescriptionText.Text = suggestedDescription;
        foreach (var interval in AllowedIntervals)
            IntervalCombo.Items.Add(interval);
        IntervalCombo.SelectedItem = "monatlich";
        CategoryCombo.Items.Add("");
        foreach (var category in categories.Where(value => !string.IsNullOrWhiteSpace(value)))
            CategoryCombo.Items.Add(category);
        CategoryCombo.SelectedIndex = 0;
        DescriptionText.Focus();
        DescriptionText.SelectAll();
    }

    public string FixedCostDescription { get; private set; } = "";
    public string Interval { get; private set; } = "monatlich";
    public string CategoryName { get; private set; } = "";

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var description = DescriptionText.Text.Trim();
        var interval = IntervalCombo.Text.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(description))
        {
            ModernMessageBox.ShowError("Bitte geben Sie eine Beschreibung ein.", "Fixkosten");
            return;
        }

        if (!AllowedIntervals.Contains(interval, StringComparer.OrdinalIgnoreCase))
        {
            ModernMessageBox.ShowError("Bitte wählen Sie ein gültiges Intervall.", "Fixkosten");
            return;
        }

        FixedCostDescription = description;
        Interval = interval;
        CategoryName = CategoryCombo.Text.Trim();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            DialogResult = false;
    }

    private void Chrome_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject source)
            return;

        for (DependencyObject? current = source; current != null; current =
                 current is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                     ? System.Windows.Media.VisualTreeHelper.GetParent(current)
                     : LogicalTreeHelper.GetParent(current))
        {
            if (current is System.Windows.Controls.Button)
                return;
        }

        DragMove();
    }
}
