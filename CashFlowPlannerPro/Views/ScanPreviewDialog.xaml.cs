using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Services;
using PDFtoImage;

namespace CashFlowPlannerPro.Views;

public partial class ScanPreviewDialog : Window
{
    private readonly string _pdfPath;
    private readonly List<BitmapSource> _pageImages = [];
    private int _currentPage;
    private int _totalPages;
    private double _zoom = 1.0;
    public Invoice? ResultInvoice { get; private set; }

    public ScanPreviewDialog(ScannedInvoice scanned, string pdfPath)
    {
        InitializeComponent();
        _pdfPath = pdfPath;
        TbFileName.Text = Path.GetFileName(pdfPath);
        PopulateFields(scanned);

        CloseBtn.ToolTip = TooltipService.Get("Btn_Close");
        PrevPageBtn.ToolTip = TooltipService.Get("Btn_PrevPage");
        NextPageBtn.ToolTip = TooltipService.Get("Btn_NextPage");
        ZoomInBtn.ToolTip = TooltipService.Get("Btn_ZoomIn");
        ZoomOutBtn.ToolTip = TooltipService.Get("Btn_ZoomOut");
        CancelBtn.ToolTip = TooltipService.Get("Btn_Cancel");
        AcceptBtn.ToolTip = TooltipService.Get("Btn_AcceptInvoice");

        Loaded += (_, _) => RenderPdf();
    }

    private void RenderPdf()
    {
        try
        {
            if (!File.Exists(_pdfPath)) return;
            var pdfBytes = File.ReadAllBytes(_pdfPath);
            _totalPages = Conversion.GetPageCount(pdfBytes);
            _pageImages.Clear();

            for (int i = 0; i < _totalPages; i++)
            {
                using var stream = new MemoryStream();
                Conversion.SavePng(stream, pdfBytes, page: i, options: new PDFtoImage.RenderOptions { Dpi = 150 });
                stream.Position = 0;
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                _pageImages.Add(bitmap);
            }

            _currentPage = 0;
            ShowPage();
        }
        catch (Exception ex)
        {
            var reference = AppLogger.LogException("invoice.scan_preview_render_failed", ex);
            PdfImage.Source = null;
            TbPageInfo.Text = $"Vorschau nicht verfügbar. Referenz: {reference}";
        }
    }

    private void ShowPage()
    {
        if (_pageImages.Count == 0) return;
        var img = _pageImages[_currentPage];
        PdfImage.Source = img;
        PdfImage.Width = img.PixelWidth * _zoom;
        PdfImage.Height = img.PixelHeight * _zoom;
        TbPageInfo.Text = $"Seite {_currentPage + 1}/{_totalPages}";
    }

    private void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage > 0) { _currentPage--; ShowPage(); }
    }

    private void NextPage_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage < _totalPages - 1) { _currentPage++; ShowPage(); }
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        _zoom = Math.Min(_zoom + 0.25, 4.0);
        ShowPage();
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        _zoom = Math.Max(_zoom - 0.25, 0.25);
        ShowPage();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) DialogResult = false;
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
        TbAmount.Text = s.Amount?.ToString("N2", CultureInfo.GetCultureInfo("de-DE")) ?? "";
        TbNetAmount.Text = s.NetAmount?.ToString("N2", CultureInfo.GetCultureInfo("de-DE")) ?? "";
        TbVat.Text = s.VatAmount != null
            ? s.VatAmount.Value.ToString("N2", CultureInfo.GetCultureInfo("de-DE")) + (s.VatRate != null ? $" ({s.VatRate.Value.ToString("N2", CultureInfo.GetCultureInfo("de-DE"))}%)" : "")
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

    private static double ParseAmountText(string text)
    {
        text = text.Trim();
        if (string.IsNullOrEmpty(text))
            return 0;

        text = text.Replace("EUR", "", StringComparison.OrdinalIgnoreCase)
            .Replace("€", "")
            .Trim();

        var parenIndex = text.IndexOf('(');
        if (parenIndex >= 0)
            text = text[..parenIndex];

        return TryParseLocalizedNumber(text, out var amount)
            ? Math.Round(amount, 2)
            : 0;
    }

    private static double ParseVatRate(string text)
    {
        var start = text.IndexOf('(');
        var end = text.IndexOf('%');
        if (start >= 0 && end > start)
            text = text.Substring(start + 1, end - start - 1);

        text = text.Trim();
        return TryParseLocalizedNumber(text, out var rate)
            ? rate
            : 19;
    }

    private static bool TryParseLocalizedNumber(string text, out double value)
    {
        text = text.Trim()
            .Replace("\u00a0", "")
            .Replace(" ", "");

        return double.TryParse(text, NumberStyles.Any, CultureInfo.GetCultureInfo("de-DE"), out value)
            || double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        var amount = ParseAmountText(TbAmount.Text);
        var netAmount = ParseAmountText(TbNetAmount.Text);
        var vatAmount = ParseAmountText(TbVat.Text);
        var vatRate = ParseVatRate(TbVat.Text);

        ResultInvoice = new Invoice {
            IssueDate = ParseBackDate(TbInvoiceDate.Text) ?? DateTime.Now.ToString("yyyy-MM-dd"),
            DueDate = ParseBackDate(TbDueDate.Text) ?? "",
            Customer = TbCustomer.Text.Trim(),
            Amount = amount,
            NetAmount = netAmount,
            VatAmount = vatAmount,
            VatRate = vatRate,
            Description = TbDescription.Text.Trim(),
            Status = "Offen",
            PdfPath = _pdfPath
        };
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
