using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Services;

namespace CashFlowPlannerPro.Views;

public partial class ProjectEditDialog : Window
{
    private enum DiscountInputMode
    {
        Percent,
        Amount
    }

    public Project Project { get; private set; }
    public bool Saved { get; private set; }
    private string _selectedColor;
    private bool _updatingBudgetFields;
    private decimal _originalBudget;
    private decimal _discountPercent;
    private decimal _discountAmount;
    private decimal _finalBudget;
    private DiscountInputMode _discountInputMode = DiscountInputMode.Percent;

    private static readonly string[] Colors = [
        "#E74C3C", "#E67E22", "#F1C40F", "#2ECC71", "#3498DB",
        "#9B59B6", "#1ABC9C", "#34495E", "#BF247A", "#D9731A"
    ];

    public ProjectEditDialog(Project project)
    {
        InitializeComponent();
        Project = project;
        _selectedColor = project.Color ?? "#3498db";
        LoadClientCombo();
        LoadData();
        TbCustomColor.Text = _selectedColor;
        BuildColorPicker();
        SaveBtn.ToolTip = TooltipService.Get("Btn_Save");
        CancelBtn.ToolTip = TooltipService.Get("Btn_Cancel");

        Loaded += (_, _) => FixEditableComboBoxes();
    }

    private void FixEditableComboBoxes()
    {
        foreach (var cb in new[] { CbClient })
        {
            if (cb.Template.FindName("PART_EditableTextBox", cb) is System.Windows.Controls.TextBox tb)
            {
                tb.Foreground = Brushes.White;
                tb.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1A, 0x40));
                tb.CaretBrush = Brushes.White;
            }
        }
    }

    private void LoadClientCombo()
    {
        var customers = Database.Instance.GetCustomers()
            .Select(c => c.DisplayName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .OrderBy(n => n);
        foreach (var name in customers)
            CbClient.Items.Add(name);
    }

    private void LoadData()
    {
        TbProjectNumber.Text = Project.ProjectNumber;
        TbName.Text = Project.Name;
        CbClient.Text = Project.Client;
        LoadBudgetData();

        if (DateTime.TryParse(Project.StartDate, out var sd)) DpStart.SelectedDate = sd;
        if (DateTime.TryParse(Project.EndDate, out var ed)) DpEnd.SelectedDate = ed;

        foreach (ComboBoxItem item in CbStatus.Items)
        {
            if (item.Content?.ToString() == Project.Status)
            { CbStatus.SelectedItem = item; break; }
        }
        if (CbStatus.SelectedItem == null) CbStatus.SelectedIndex = 0;

        if (Project.Id == 0) DialogTitle.Text = "Neues Projekt";
    }

    private void LoadBudgetData()
    {
        _originalBudget = RoundMoney(ToDecimal(Project.OriginalBudget));
        _discountPercent = ToDecimal(Project.DiscountPercent);
        _finalBudget = RoundMoney(ToDecimal(Project.Budget));

        // Compatibility for projects saved before original value and discount
        // were stored separately.
        if (_originalBudget == 0m && _discountPercent == 0m && _finalBudget != 0m)
            _originalBudget = _finalBudget;

        _discountAmount = RoundMoney(_originalBudget - _finalBudget);
        var amountFromStoredPercent = RoundMoney(_originalBudget * _discountPercent / 100m);
        if (_discountPercent is < 0m or > 100m ||
            Math.Abs(amountFromStoredPercent - _discountAmount) > 0.01m)
        {
            _discountPercent = CalculatePercent(_originalBudget, _discountAmount);
            _discountInputMode = DiscountInputMode.Amount;
        }

        _updatingBudgetFields = true;
        try
        {
            TbOriginalBudget.Text = FormatMoney(_originalBudget);
            TbDiscountPercent.Text = FormatNumber(_discountPercent);
            TbDiscountAmount.Text = FormatMoney(_discountAmount);
            TbBudget.Text = FormatMoney(_finalBudget);
        }
        finally
        {
            _updatingBudgetFields = false;
        }
    }

    private void TbOriginalBudget_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingBudgetFields ||
            !TryParseNumber(TbOriginalBudget.Text, out var originalBudget) ||
            originalBudget < 0m)
            return;

        _originalBudget = RoundMoney(originalBudget);
        if (_discountInputMode == DiscountInputMode.Percent)
            RecalculateFromPercent();
        else
            RecalculateFromAmount();
    }

    private void TbDiscountPercent_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingBudgetFields ||
            !TryParseNumber(TbDiscountPercent.Text, out var discountPercent))
            return;

        _discountInputMode = DiscountInputMode.Percent;
        _discountPercent = discountPercent;
        RecalculateFromPercent();
    }

    private void TbDiscountAmount_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingBudgetFields ||
            !TryParseNumber(TbDiscountAmount.Text, out var discountAmount))
            return;

        _discountInputMode = DiscountInputMode.Amount;
        _discountAmount = RoundMoney(discountAmount);
        RecalculateFromAmount();
    }

    private void RecalculateFromPercent()
    {
        var previewPercent = Math.Clamp(_discountPercent, 0m, 100m);
        _discountAmount = RoundMoney(_originalBudget * previewPercent / 100m);
        _finalBudget = RoundMoney(_originalBudget - _discountAmount);

        _updatingBudgetFields = true;
        try
        {
            TbDiscountAmount.Text = FormatMoney(_discountAmount);
            TbBudget.Text = FormatMoney(_finalBudget);
        }
        finally
        {
            _updatingBudgetFields = false;
        }
    }

    private void RecalculateFromAmount()
    {
        _discountPercent = CalculatePercent(_originalBudget, _discountAmount);
        _finalBudget = RoundMoney(Math.Max(0m, _originalBudget - _discountAmount));

        _updatingBudgetFields = true;
        try
        {
            TbDiscountPercent.Text = FormatNumber(_discountPercent);
            TbBudget.Text = FormatMoney(_finalBudget);
        }
        finally
        {
            _updatingBudgetFields = false;
        }
    }

    private static decimal CalculatePercent(decimal originalBudget, decimal discountAmount) =>
        originalBudget <= 0m
            ? 0m
            : Math.Round(discountAmount / originalBudget * 100m, 6, MidpointRounding.AwayFromZero);

    private static decimal RoundMoney(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal ToDecimal(double value)
    {
        try
        {
            return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        }
        catch (OverflowException)
        {
            return 0m;
        }
    }

    private static string FormatMoney(decimal value) =>
        value.ToString("N2", CultureInfo.CurrentCulture);

    private static string FormatNumber(decimal value) =>
        value.ToString("0.######", CultureInfo.CurrentCulture);

    private static bool TryParseNumber(string? text, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var cleaned = text.Trim()
            .Replace("€", "", StringComparison.Ordinal)
            .Replace("%", "", StringComparison.Ordinal)
            .Replace("\u00A0", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace("'", "", StringComparison.Ordinal);

        var commaIndex = cleaned.LastIndexOf(',');
        var dotIndex = cleaned.LastIndexOf('.');
        if (commaIndex >= 0 && dotIndex >= 0)
        {
            var decimalSeparator = commaIndex > dotIndex ? ',' : '.';
            var groupingSeparator = decimalSeparator == ',' ? "." : ",";
            cleaned = cleaned.Replace(groupingSeparator, "", StringComparison.Ordinal)
                .Replace(decimalSeparator, '.');
        }
        else if (commaIndex >= 0 || dotIndex >= 0)
        {
            var separator = commaIndex >= 0 ? ',' : '.';
            var separatorIndex = Math.Max(commaIndex, dotIndex);
            var occurrenceCount = cleaned.Count(character => character == separator);
            var digitsAfterSeparator = cleaned.Length - separatorIndex - 1;
            var currentDecimalSeparator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
            var isCurrentGrouping = separator.ToString() != currentDecimalSeparator &&
                                    digitsAfterSeparator == 3 && occurrenceCount == 1;

            if (isCurrentGrouping)
                cleaned = cleaned.Replace(separator.ToString(), "", StringComparison.Ordinal);
            else if (occurrenceCount == 1)
                cleaned = cleaned.Replace(separator, '.');
            else
            {
                var parts = cleaned.Split(separator);
                var containsOnlyGroupedThousands = parts.Skip(1).All(part => part.Length == 3);
                cleaned = containsOnlyGroupedThousands
                    ? string.Concat(parts)
                    : string.Concat(parts[..^1]) + "." + parts[^1];
            }
        }

        return decimal.TryParse(
            cleaned,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out value);
    }

    private void BuildColorPicker()
    {
        ColorPicker.Children.Clear();
        foreach (var hex in Colors)
        {
            Color c;
            try { c = (Color)ColorConverter.ConvertFromString(hex); }
            catch { continue; }

            var circle = new Ellipse {
                Width = 28, Height = 28, Fill = new SolidColorBrush(c),
                Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand,
                Stroke = hex == _selectedColor ? Brushes.White : Brushes.Transparent,
                StrokeThickness = 3
            };
            var capturedHex = hex;
            circle.MouseLeftButtonDown += (_, _) => {
                _selectedColor = capturedHex;
                TbCustomColor.Text = capturedHex;
                UpdateColorPreview();
                BuildColorPicker(); // refresh selection
            };
            ColorPicker.Children.Add(circle);
        }
        UpdateColorPreview();
    }

    private void TbCustomColor_TextChanged(object sender, TextChangedEventArgs e)
    {
        var text = TbCustomColor.Text.Trim();
        if (!text.StartsWith("#")) text = "#" + text;
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(text);
            _selectedColor = text;
            UpdateColorPreview();
            // Deselect palette circles
            foreach (var child in ColorPicker.Children)
                if (child is Ellipse el)
                    el.Stroke = Brushes.Transparent;
        }
        catch { }
    }

    private void UpdateColorPreview()
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(_selectedColor);
            ColorPreview.Background = new SolidColorBrush(c);
        }
        catch
        {
            ColorPreview.Background = Brushes.Gray;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TbName.Text))
        {
            ModernMessageBox.ShowError("Bitte geben Sie einen Projektnamen ein.", "Pflichtfeld");
            return;
        }

        if (!TryParseNumber(TbOriginalBudget.Text, out var originalBudget) || originalBudget < 0m)
        {
            ModernMessageBox.ShowError("Bitte geben Sie einen gültigen, nicht negativen Ausgangswert ein.", "Ungültiger Ausgangswert");
            TbOriginalBudget.Focus();
            TbOriginalBudget.SelectAll();
            return;
        }

        originalBudget = RoundMoney(originalBudget);
        decimal discountPercent;
        decimal discountAmount;
        if (_discountInputMode == DiscountInputMode.Percent)
        {
            if (!TryParseNumber(TbDiscountPercent.Text, out discountPercent) ||
                discountPercent is < 0m or > 100m)
            {
                ModernMessageBox.ShowError("Bitte geben Sie einen Rabatt zwischen 0 und 100 Prozent ein.", "Ungültiger Rabatt");
                TbDiscountPercent.Focus();
                TbDiscountPercent.SelectAll();
                return;
            }

            discountPercent = Math.Round(discountPercent, 6, MidpointRounding.AwayFromZero);
            discountAmount = RoundMoney(originalBudget * discountPercent / 100m);
        }
        else
        {
            if (!TryParseNumber(TbDiscountAmount.Text, out discountAmount) ||
                discountAmount < 0m || discountAmount > originalBudget)
            {
                ModernMessageBox.ShowError("Der Rabattbetrag muss zwischen 0 € und dem Ausgangswert liegen.", "Ungültiger Rabatt");
                TbDiscountAmount.Focus();
                TbDiscountAmount.SelectAll();
                return;
            }

            discountAmount = RoundMoney(discountAmount);
            discountPercent = CalculatePercent(originalBudget, discountAmount);
        }

        _finalBudget = RoundMoney(originalBudget - discountAmount);

        Project.ProjectNumber = TbProjectNumber.Text.Trim();
        Project.Name = TbName.Text.Trim();
        Project.Client = CbClient.Text.Trim();
        Project.Color = _selectedColor;
        Project.OriginalBudget = (double)originalBudget;
        Project.DiscountPercent = (double)discountPercent;
        Project.Budget = (double)_finalBudget;
        Project.StartDate = DpStart.SelectedDate?.ToString("yyyy-MM-dd");
        Project.EndDate = DpEnd.SelectedDate?.ToString("yyyy-MM-dd");
        Project.Status = (CbStatus.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "active";

        Saved = true;
        Close();
    }

    private void DatePicker_FixStyle(object sender, RoutedEventArgs e)
    {
        if (sender is DatePicker dp)
        {
            var textBox = dp.Template.FindName("PART_TextBox", dp) as System.Windows.Controls.Primitives.DatePickerTextBox;
            if (textBox != null)
            {
                textBox.Foreground = Brushes.White;
                textBox.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1A, 0x40));
                textBox.BorderThickness = new Thickness(0);

                // Remove the watermark
                var wm = textBox.Template?.FindName("PART_Watermark", textBox) as ContentControl;
                if (wm != null) wm.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
        if (e.Key == Key.Enter) Save_Click(sender, e);
    }

    // --- Static API ---
    public static Project? ShowEdit(Project project)
    {
        var dlg = new ProjectEditDialog(new Project {
            Id = project.Id, ProjectNumber = project.ProjectNumber, Name = project.Name,
            Client = project.Client, Color = project.Color, StartDate = project.StartDate,
            EndDate = project.EndDate, OriginalBudget = project.OriginalBudget,
            DiscountPercent = project.DiscountPercent, Budget = project.Budget, Status = project.Status
        });
        dlg.Owner = Application.Current.MainWindow?.IsVisible == true ? Application.Current.MainWindow : null;
        dlg.ShowDialog();
        return dlg.Saved ? dlg.Project : null;
    }

    public static Project? ShowNew()
    {
        return ShowEdit(new Project());
    }
}
