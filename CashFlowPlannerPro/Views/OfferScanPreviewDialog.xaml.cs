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

public partial class OfferScanPreviewDialog : Window
{
    private readonly string _pdfPath;
    private readonly List<BitmapSource> _pageImages = [];
    private int _currentPage;
    private int _totalPages;
    private double _zoom = 1.0;
    public Offer? ResultOffer { get; private set; }

    public OfferScanPreviewDialog(ScannedOffer scanned, string pdfPath)
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
        AcceptBtn.ToolTip = TooltipService.Get("Btn_AcceptOffer");

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
            PdfImage.Source = null;
            TbPageInfo.Text = $"Fehler: {ex.Message}";
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

    private void PopulateFields(ScannedOffer s)
    {
        var pct = (int)(s.Confidence * 100);
        TbConfidence.Text = $"{pct}%";
        TbConfidence.Foreground = new SolidColorBrush(pct >= 60
            ? Color.FromRgb(0x27, 0xAE, 0x60)
            : pct >= 30 ? Color.FromRgb(0xD9, 0x73, 0x1A)
            : Color.FromRgb(0xBF, 0x39, 0x39));

        TbOfferNumber.Text = s.OfferNumber ?? "";
        TbOfferDate.Text = FormatDate(s.OfferDate);
        TbCustomer.Text = s.Customer ?? "";
        TbGrossAmount.Text = s.GrossAmount?.ToString("N2", CultureInfo.GetCultureInfo("de-DE")) ?? "";
        TbNetAmount.Text = s.NetAmount?.ToString("N2", CultureInfo.GetCultureInfo("de-DE")) ?? "";
        TbVat.Text = s.VatAmount != null
            ? s.VatAmount.Value.ToString("N2", CultureInfo.GetCultureInfo("de-DE")) + (s.VatRate != null ? $" ({s.VatRate.Value.ToString("N2", CultureInfo.GetCultureInfo("de-DE"))}%)" : "")
            : "";
        TbDescription.Text = s.Description ?? "";
        TbRawText.Text = s.RawText;

        if (s.LineItems.Count > 0)
            ItemsGrid.ItemsSource = s.LineItems;
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
        var amount = ParseAmountText(TbGrossAmount.Text);
        var prob = ParseAmountText(TbProbability.Text);

        ResultOffer = new Offer {
            OfferNumber = TbOfferNumber.Text.Trim(),
            OfferDate = ParseBackDate(TbOfferDate.Text) ?? DateTime.Now.ToString("yyyy-MM-dd"),
            Customer = TbCustomer.Text.Trim(),
            Amount = amount,
            Probability = prob,
            Description = TbDescription.Text.Trim(),
            Status = "Offen",
            PdfPath = _pdfPath
        };
        DialogResult = true;
    }

    private static double ParseAmountText(string text)
    {
        text = text.Replace("EUR", "", StringComparison.OrdinalIgnoreCase)
            .Replace("€", "")
            .Trim();

        var parenIndex = text.IndexOf('(');
        if (parenIndex >= 0)
            text = text[..parenIndex];

        return TryParseLocalizedNumber(text, out var value)
            ? Math.Round(value, 2)
            : 0;
    }

    private static bool TryParseLocalizedNumber(string text, out double value)
    {
        text = text.Trim()
            .Replace("\u00a0", "")
            .Replace(" ", "");

        return double.TryParse(text, NumberStyles.Any, CultureInfo.GetCultureInfo("de-DE"), out value)
            || double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
