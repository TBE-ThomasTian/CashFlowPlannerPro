using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Services;

namespace CashFlowPlannerPro.Views;

public partial class FixkostenEditDialog : Window
{
    public Transaction Transaction { get; private set; }
    public bool Saved { get; private set; }

    public FixkostenEditDialog(Transaction transaction, IEnumerable<string> categories, IEnumerable<string> intervals)
    {
        InitializeComponent();
        Transaction = transaction;
        LoadCombos(categories, intervals);
        LoadData();
        SaveBtn.ToolTip = TooltipService.Get("Btn_Save");
        CancelBtn.ToolTip = TooltipService.Get("Btn_Cancel");
    }

    private void LoadCombos(IEnumerable<string> categories, IEnumerable<string> intervals)
    {
        foreach (var category in categories)
            CbCategory.Items.Add(category);

        foreach (var interval in intervals)
            CbInterval.Items.Add(interval);
    }

    private void LoadData()
    {
        TbDate.Text = FormatDate(Transaction.Date);
        TbDescription.Text = Transaction.Description;
        TbAmount.Text = FormatAmount(Transaction.Amount);
        CbCategory.Text = Transaction.CategoryName ?? "";
        CbInterval.SelectedItem = string.IsNullOrWhiteSpace(Transaction.Interval)
            ? "monatlich"
            : Transaction.Interval;
        if (CbInterval.SelectedItem == null)
            CbInterval.Text = Transaction.Interval;

        if (Transaction.Id == 0)
            DialogTitle.Text = "Neue Fixkosten";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var date = ParseDate(TbDate.Text);
        var amount = ParseAmount(TbAmount.Text);

        if (string.IsNullOrWhiteSpace(date))
        {
            ModernMessageBox.ShowError("Bitte gib ein gueltiges Datum ein.", "Fixkosten");
            return;
        }

        if (amount == null)
        {
            ModernMessageBox.ShowError("Bitte gib einen gueltigen Betrag ein.", "Fixkosten");
            return;
        }

        Transaction.Date = date;
        Transaction.Description = TbDescription.Text.Trim();
        Transaction.Amount = amount.Value;
        Transaction.CategoryName = CbCategory.Text.Trim();
        Transaction.Interval = string.IsNullOrWhiteSpace(CbInterval.Text) ? "monatlich" : CbInterval.Text.Trim();
        Transaction.Notes = EnsureFixkostenMarker(Transaction.Notes);

        Saved = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            Save_Click(sender, e);
    }

    public static Transaction? ShowNew(Transaction transaction, IEnumerable<string> categories, IEnumerable<string> intervals)
    {
        return ShowDialog(transaction, categories, intervals);
    }

    public static Transaction? ShowEdit(Transaction transaction, IEnumerable<string> categories, IEnumerable<string> intervals)
    {
        var copy = new Transaction
        {
            Id = transaction.Id,
            Date = transaction.Date,
            Description = transaction.Description,
            Amount = transaction.Amount,
            CategoryId = transaction.CategoryId,
            PersonId = transaction.PersonId,
            Interval = transaction.Interval,
            Notes = transaction.Notes,
            CreatedAt = transaction.CreatedAt,
            UpdatedAt = transaction.UpdatedAt,
            CategoryName = transaction.CategoryName
        };
        return ShowDialog(copy, categories, intervals);
    }

    private static Transaction? ShowDialog(Transaction transaction, IEnumerable<string> categories, IEnumerable<string> intervals)
    {
        var dlg = new FixkostenEditDialog(transaction, categories, intervals)
        {
            Owner = Application.Current.MainWindow?.IsVisible == true ? Application.Current.MainWindow : null
        };
        dlg.ShowDialog();
        return dlg.Saved ? dlg.Transaction : null;
    }

    private static string FormatAmount(double value) =>
        value.ToString("N2", CultureInfo.GetCultureInfo("de-DE"));

    private static string FormatDate(string? isoDate)
    {
        if (string.IsNullOrWhiteSpace(isoDate))
            return "";

        return DateTime.TryParseExact(isoDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? dt.ToString("dd.MM.yyyy")
            : isoDate;
    }

    private static string? ParseDate(string text)
    {
        text = text.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return null;

        string[] formats = ["yyyy-MM-dd", "dd.MM.yyyy", "d.M.yyyy", "dd.MM.yy", "d.M.yy"];
        if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
            return exact.ToString("yyyy-MM-dd");

        return DateTime.TryParse(text, CultureInfo.GetCultureInfo("de-DE"), DateTimeStyles.None, out var parsed)
            ? parsed.ToString("yyyy-MM-dd")
            : null;
    }

    private static double? ParseAmount(string text)
    {
        text = text.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return null;

        text = text.Replace("EUR", "", StringComparison.OrdinalIgnoreCase)
            .Replace("\u20ac", "")
            .Replace(" ", "");

        if (text.Contains('.') && text.Contains(','))
            text = text.Replace(".", "").Replace(",", ".");
        else if (text.Contains(','))
            text = text.Replace(",", ".");

        return double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static string EnsureFixkostenMarker(string? notes)
    {
        notes ??= "";
        return notes.StartsWith("FIXKOSTEN:", StringComparison.OrdinalIgnoreCase)
            ? notes
            : "FIXKOSTEN:" + notes;
    }
}
