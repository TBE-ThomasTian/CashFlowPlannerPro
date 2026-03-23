using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Services;

namespace CashFlowPlannerPro.Views;

public partial class ScanPreviewDialog : Window
{
    private readonly string _pdfPath;
    public Invoice? ResultInvoice { get; private set; }

    public ScanPreviewDialog(ScannedInvoice scanned, string pdfPath)
    {
        InitializeComponent();
        _pdfPath = pdfPath;
        PopulateFields(scanned);
        Loaded += (_, _) => LoadPdfPreview();
    }

    private void LoadPdfPreview()
    {
        try
        {
            if (File.Exists(_pdfPath))
                PdfViewer.Navigate(new Uri(_pdfPath));
        }
        catch { /* PDF preview not available */ }
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void PopulateFields(ScannedInvoice s)
    {
        var pct = (int)(s.Confidence * 100);
        TbConfidence.Text = $"{pct}%";
        TbConfidence.Foreground = new SolidColorBrush(pct >= 60
            ? Color.FromRgb(0x27, 0xAE, 0x60)
            : pct >= 30 ? Color.FromRgb(0xD9, 0x73, 0x1A)
            : Color.FromRgb(0xBF, 0x39, 0x39));

        TbInvoiceNumber.Text = s.InvoiceNumber ?? "";
        TbInvoiceDate.Text = FormatDate(s.InvoiceDate);
        TbDueDate.Text = FormatDate(s.DueDate);
        TbCustomer.Text = s.Customer ?? "";
        TbAmount.Text = s.Amount?.ToString("F2") ?? "";
        TbNetAmount.Text = s.NetAmount?.ToString("F2") ?? "";
        TbVat.Text = s.VatAmount != null
            ? $"{s.VatAmount:F2}" + (s.VatRate != null ? $" ({s.VatRate}%)" : "")
            : "";
        TbIban.Text = s.Iban ?? "";
        TbDescription.Text = s.Description ?? "";
        TbPaymentTerms.Text = s.PaymentTerms ?? "";
        TbRawText.Text = s.RawText;
    }

    private static string FormatDate(string? isoDate)
    {
        if (string.IsNullOrEmpty(isoDate)) return "";
        if (DateTime.TryParseExact(isoDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt.ToString("dd.MM.yyyy");
        return isoDate;
    }

    private static string? ParseBackDate(string text)
    {
        text = text.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (DateTime.TryParseExact(text, "dd.MM.yyyy", new CultureInfo("de-DE"), DateTimeStyles.None, out var dt))
            return dt.ToString("yyyy-MM-dd");
        if (DateTime.TryParse(text, new CultureInfo("de-DE"), DateTimeStyles.None, out var dt2))
            return dt2.ToString("yyyy-MM-dd");
        return text;
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        double.TryParse(TbAmount.Text.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out var amount);
        ResultInvoice = new Invoice {
            IssueDate = ParseBackDate(TbInvoiceDate.Text) ?? DateTime.Now.ToString("yyyy-MM-dd"),
            DueDate = ParseBackDate(TbDueDate.Text),
            Customer = TbCustomer.Text.Trim(),
            Amount = amount,
            Description = TbDescription.Text.Trim(),
            Status = "Offen",
            PdfPath = _pdfPath
        };
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
