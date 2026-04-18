using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Services;

namespace CashFlowPlannerPro.Views;

public partial class InvoiceEditDialog : Window
{
    private const double DefaultVatRate = 19;
    private bool isUpdatingAmounts;
    private string amountBase = nameof(Invoice.Amount);

    public Invoice Invoice { get; private set; }
    public bool Saved { get; private set; }

    public InvoiceEditDialog(Invoice invoice, IEnumerable<string> customerNames)
    {
        InitializeComponent();
        Invoice = invoice;
        LoadCombos(customerNames);
        LoadData();
        SaveBtn.ToolTip = TooltipService.Get("Btn_Save");
        CancelBtn.ToolTip = TooltipService.Get("Btn_Cancel");
    }

    private void LoadCombos(IEnumerable<string> customerNames)
    {
        foreach (var name in customerNames)
            CbCustomer.Items.Add(name);

        CbStatus.Items.Add("Offen");
        CbStatus.Items.Add("Bezahlt");
        CbStatus.Items.Add("\u00dcberf\u00e4llig");
        CbStatus.Items.Add("Storniert");
    }

    private void LoadData()
    {
        isUpdatingAmounts = true;
        TbIssueDate.Text = FormatDate(Invoice.IssueDate);
        TbDueDate.Text = FormatDate(Invoice.DueDate);
        CbCustomer.Text = Invoice.Customer;
        TbGross.Text = FormatAmount(Invoice.Amount);
        TbNet.Text = FormatAmount(Invoice.NetAmount);
        TbVat.Text = FormatAmount(Invoice.VatAmount);
        TbVatRate.Text = FormatAmount(Invoice.VatRate);
        TbDescription.Text = Invoice.Description;
        TbPaidDate.Text = FormatDate(Invoice.PaidDate);
        TbPaidAmount.Text = FormatAmount(Invoice.PaidAmount);
        CbStatus.SelectedItem = Invoice.Status;
        if (CbStatus.SelectedItem == null)
            CbStatus.Text = Invoice.Status;
        isUpdatingAmounts = false;
    }

    private void Gross_LostFocus(object sender, RoutedEventArgs e)
    {
        if (isUpdatingAmounts) return;
        amountBase = nameof(Invoice.Amount);
        RecalculateAmounts();
    }

    private void Net_LostFocus(object sender, RoutedEventArgs e)
    {
        if (isUpdatingAmounts) return;
        amountBase = nameof(Invoice.NetAmount);
        RecalculateAmounts();
    }

    private void Vat_LostFocus(object sender, RoutedEventArgs e)
    {
        if (isUpdatingAmounts) return;
        amountBase = nameof(Invoice.VatAmount);
        RecalculateAmounts();
    }

    private void VatRate_LostFocus(object sender, RoutedEventArgs e)
    {
        if (isUpdatingAmounts) return;
        RecalculateAmounts();
    }

    private void RecalculateAmounts()
    {
        var gross = ParseAmount(TbGross.Text);
        var net = ParseAmount(TbNet.Text);
        var vat = ParseAmount(TbVat.Text);
        var vatRate = NormalizeVatRate(ParseAmount(TbVatRate.Text, DefaultVatRate) ?? DefaultVatRate);

        if (gross == null || net == null || vat == null)
            return;

        switch (amountBase)
        {
            case nameof(Invoice.NetAmount):
                net = RoundCurrency(net.Value);
                vat = RoundCurrency(net.Value * vatRate / 100);
                gross = RoundCurrency(net.Value + vat.Value);
                break;
            case nameof(Invoice.VatAmount):
                vat = RoundCurrency(vat.Value);
                if (!IsZero(net.Value))
                {
                    gross = RoundCurrency(net.Value + vat.Value);
                    vatRate = RoundRate(vat.Value / net.Value * 100);
                }
                else if (!IsZero(gross.Value))
                {
                    net = RoundCurrency(gross.Value - vat.Value);
                    vatRate = !IsZero(net.Value) ? RoundRate(vat.Value / net.Value * 100) : DefaultVatRate;
                }
                break;
            default:
                gross = RoundCurrency(gross.Value);
                if (IsZero(vatRate))
                {
                    net = gross;
                    vat = 0;
                }
                else
                {
                    net = RoundCurrency(gross.Value / (1 + vatRate / 100));
                    vat = RoundCurrency(gross.Value - net.Value);
                }
                break;
        }

        isUpdatingAmounts = true;
        TbGross.Text = FormatAmount(gross.Value);
        TbNet.Text = FormatAmount(net.Value);
        TbVat.Text = FormatAmount(vat.Value);
        TbVatRate.Text = FormatAmount(vatRate);
        isUpdatingAmounts = false;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var gross = ParseAmount(TbGross.Text);
        var net = ParseAmount(TbNet.Text);
        var vat = ParseAmount(TbVat.Text);
        var vatRate = ParseAmount(TbVatRate.Text, DefaultVatRate);
        var paidAmount = ParseAmount(TbPaidAmount.Text, 0);

        if (gross == null || net == null || vat == null || vatRate == null || paidAmount == null)
        {
            ModernMessageBox.ShowError("Bitte pruefe die Betragsfelder.", "Rechnung bearbeiten");
            return;
        }

        Invoice.IssueDate = ParseDate(TbIssueDate.Text) ?? "";
        Invoice.DueDate = ParseDate(TbDueDate.Text) ?? "";
        Invoice.Customer = CbCustomer.Text.Trim();
        Invoice.Amount = RoundCurrency(gross.Value);
        Invoice.NetAmount = RoundCurrency(net.Value);
        Invoice.VatAmount = RoundCurrency(vat.Value);
        Invoice.VatRate = NormalizeVatRate(vatRate.Value);
        Invoice.Description = TbDescription.Text.Trim();
        Invoice.PaidDate = ParseDate(TbPaidDate.Text);
        Invoice.PaidAmount = RoundCurrency(paidAmount.Value);
        Invoice.Status = CbStatus.Text.Trim();

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

    public static Invoice? ShowEdit(Invoice invoice, IEnumerable<string> customerNames)
    {
        var copy = new Invoice
        {
            Id = invoice.Id,
            IssueDate = invoice.IssueDate,
            DueDate = invoice.DueDate,
            Customer = invoice.Customer,
            CustomerId = invoice.CustomerId,
            Amount = invoice.Amount,
            NetAmount = invoice.NetAmount,
            VatAmount = invoice.VatAmount,
            VatRate = invoice.VatRate,
            Description = invoice.Description,
            PaidDate = invoice.PaidDate,
            PaidAmount = invoice.PaidAmount,
            Status = invoice.Status,
            PdfPath = invoice.PdfPath,
            CreatedAt = invoice.CreatedAt
        };

        var dlg = new InvoiceEditDialog(copy, customerNames)
        {
            Owner = Application.Current.MainWindow?.IsVisible == true ? Application.Current.MainWindow : null
        };
        dlg.ShowDialog();
        return dlg.Saved ? dlg.Invoice : null;
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
            : text;
    }

    private static double? ParseAmount(string text, double? emptyValue = null)
    {
        text = text.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return emptyValue;

        text = text.Replace("EUR", "", StringComparison.OrdinalIgnoreCase)
            .Replace("€", "")
            .Replace(" ", "");

        if (text.Contains('.') && text.Contains(','))
            text = text.Replace(".", "").Replace(",", ".");
        else if (text.Contains(','))
            text = text.Replace(",", ".");

        return double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static double NormalizeVatRate(double vatRate) =>
        double.IsNaN(vatRate) || double.IsInfinity(vatRate) || vatRate < 0
            ? DefaultVatRate
            : RoundRate(vatRate);

    private static double RoundCurrency(double value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static double RoundRate(double value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static bool IsZero(double value) => Math.Abs(value) < 0.005;
}
