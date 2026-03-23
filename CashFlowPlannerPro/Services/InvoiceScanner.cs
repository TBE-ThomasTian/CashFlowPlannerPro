using System.Globalization;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace CashFlowPlannerPro.Services;

public class ScannedInvoice
{
    public string? InvoiceNumber { get; set; }
    public string? InvoiceDate { get; set; }
    public string? DueDate { get; set; }
    public double? Amount { get; set; }
    public double? NetAmount { get; set; }
    public double? VatAmount { get; set; }
    public double? VatRate { get; set; }
    public string? Customer { get; set; }
    public string? Description { get; set; }
    public string? Iban { get; set; }
    public string? PaymentTerms { get; set; }
    public List<string> LineItems { get; set; } = [];
    public string RawText { get; set; } = "";
    public double Confidence { get; set; }
}

public static class InvoiceScanner
{
    public static ScannedInvoice ScanPdf(string pdfPath)
    {
        var result = new ScannedInvoice();
        var text = ExtractText(pdfPath);
        result.RawText = text;

        if (string.IsNullOrWhiteSpace(text))
            return result;

        int foundFields = 0;

        // Invoice number
        result.InvoiceNumber = FindPattern(text,
            @"(?:Rechnungs?(?:nummer|nr\.?|[\s-]?Nr\.?))\s*[:\s]?\s*(\S+)",
            @"(?:Invoice\s*(?:No\.?|Number|#))\s*[:\s]?\s*(\S+)",
            @"(?:Re\.?\s*Nr\.?|RE-Nr\.?)\s*[:\s]?\s*(\S+)");
        if (result.InvoiceNumber != null) foundFields++;

        // Invoice date
        var dateStr = FindPattern(text,
            @"(?:Rechnungs?datum|Datum|Date|Ausstellungsdatum)\s*[:\s]?\s*(\d{1,2}[./]\d{1,2}[./]\d{2,4})",
            @"(?:Rechnungs?datum|Datum|Date)\s*[:\s]?\s*(\d{1,2}\.\s*\w+\s*\d{4})");
        result.InvoiceDate = ParseDateString(dateStr);
        if (result.InvoiceDate != null) foundFields++;

        // Due date
        var dueDateStr = FindPattern(text,
            @"(?:Fällig(?:keit(?:sdatum)?)?|Zahlbar bis|Due\s*Date|Zahlungsziel)\s*[:\s]?\s*(\d{1,2}[./]\d{1,2}[./]\d{2,4})",
            @"(?:Zahlbar\s+innerhalb\s+von\s+(\d+)\s+Tagen)");
        if (dueDateStr != null && int.TryParse(dueDateStr, out int days) && result.InvoiceDate != null)
        {
            if (DateTime.TryParseExact(result.InvoiceDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var invDate))
                result.DueDate = invDate.AddDays(days).ToString("yyyy-MM-dd");
        }
        else
            result.DueDate = ParseDateString(dueDateStr);
        if (result.DueDate != null) foundFields++;

        // Amounts - try gross amount first
        var amountStr = FindPattern(text,
            @"(?:Gesamt(?:betrag)?|Brutto(?:betrag)?|Total|Rechnungsbetrag|Endbetrag|Summe)\s*[:\s]?\s*[€$]?\s*([\d.,]+)\s*[€$]?",
            @"[€$]\s*([\d.,]+)\s*(?:brutto|gesamt|total)",
            @"(?:Zu\s+zahlen|Zahlbetrag)\s*[:\s]?\s*[€$]?\s*([\d.,]+)");
        result.Amount = ParseAmount(amountStr);
        if (result.Amount != null) foundFields++;

        // Net amount
        var netStr = FindPattern(text,
            @"(?:Netto(?:betrag)?|Zwischensumme|Subtotal)\s*[:\s]?\s*[€$]?\s*([\d.,]+)");
        result.NetAmount = ParseAmount(netStr);

        // VAT
        var vatStr = FindPattern(text,
            @"(?:MwSt\.?|USt\.?|Umsatzsteuer|Mehrwertsteuer|VAT)\s*(?:\(?\s*(\d+)\s*%?\s*\)?)?\s*[:\s]?\s*[€$]?\s*([\d.,]+)");
        if (vatStr != null)
        {
            result.VatAmount = ParseAmount(vatStr);
            // Try to extract rate
            var rateMatch = Regex.Match(text, @"(\d{1,2})\s*%\s*(?:MwSt|USt|Umsatzsteuer|Mehrwertsteuer|VAT)", RegexOptions.IgnoreCase);
            if (rateMatch.Success) result.VatRate = double.Parse(rateMatch.Groups[1].Value);
        }

        // Customer / Company name (first prominent name in header area)
        var headerArea = text.Length > 500 ? text[..500] : text;
        result.Customer = FindPattern(headerArea,
            @"(?:Kunde|Empfänger|Rechnungsempfänger|An|Bill\s*To|Auftraggeber)\s*[:\s]?\s*\n?\s*(.+?)(?:\n|$)",
            @"(?:Firma|Company)\s*[:\s]?\s*(.+?)(?:\n|$)");
        if (result.Customer != null)
        {
            result.Customer = result.Customer.Trim().TrimEnd(':');
            foundFields++;
        }

        // IBAN
        result.Iban = FindPattern(text,
            @"(?:IBAN)\s*[:\s]?\s*([A-Z]{2}\d{2}[\s]?[\dA-Z\s]{10,30})",
            @"([A-Z]{2}\d{2}\s?\d{4}\s?\d{4}\s?\d{4}\s?\d{4}\s?\d{0,4})");
        if (result.Iban != null)
            result.Iban = Regex.Replace(result.Iban, @"\s", "");

        // Payment terms
        result.PaymentTerms = FindPattern(text,
            @"(?:Zahlungsbedingung(?:en)?|Payment\s*Terms?)\s*[:\s]?\s*(.+?)(?:\n|$)",
            @"(Zahlbar\s+innerhalb\s+von\s+\d+\s+Tagen.*?)(?:\n|$)",
            @"(\d+\s*%?\s*Skonto.*?)(?:\n|$)");

        // Line items - look for structured lines with amounts
        var lineMatches = Regex.Matches(text,
            @"^[\s]*(\d+[\s.)])\s+(.+?)\s+([\d.,]+)\s*[€$]?\s*$",
            RegexOptions.Multiline);
        foreach (Match m in lineMatches)
            result.LineItems.Add($"{m.Groups[1].Value.Trim()} {m.Groups[2].Value.Trim()} — {m.Groups[3].Value.Trim()} €");

        // Also try tab/space separated items
        if (result.LineItems.Count == 0)
        {
            var altLines = Regex.Matches(text,
                @"(.{10,50}?)\s{2,}(\d+)\s{1,}([\d.,]+)\s*[€$]?",
                RegexOptions.Multiline);
            foreach (Match m in altLines)
            {
                var desc = m.Groups[1].Value.Trim();
                if (!Regex.IsMatch(desc, @"(?:Summe|Total|Netto|Brutto|MwSt|USt|Gesamt|IBAN|BIC)", RegexOptions.IgnoreCase))
                    result.LineItems.Add($"{desc} — {m.Groups[3].Value.Trim()} €");
            }
        }

        // Description from first line item or general context
        if (result.LineItems.Count > 0)
            result.Description = string.Join("; ", result.LineItems.Take(3).Select(l => l.Split('—')[0].Trim()));
        else
        {
            result.Description = FindPattern(text,
                @"(?:Betreff|Leistung(?:sbeschreibung)?|Beschreibung|Subject|Gegenstand)\s*[:\s]?\s*(.+?)(?:\n|$)");
        }

        // Confidence score (0-1 based on how many fields found)
        result.Confidence = Math.Min(1.0, foundFields / 5.0);

        return result;
    }

    private static string ExtractText(string pdfPath)
    {
        try
        {
            using var document = PdfDocument.Open(pdfPath);
            var sb = new System.Text.StringBuilder();
            foreach (var page in document.GetPages())
                sb.AppendLine(page.Text);
            return sb.ToString();
        }
        catch
        {
            return "";
        }
    }

    private static string? FindPattern(string text, params string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (match.Success)
            {
                // Return last capturing group
                var group = match.Groups[match.Groups.Count - 1];
                return group.Value.Trim();
            }
        }
        return null;
    }

    private static double? ParseAmount(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        // German format: 1.234,56 or 1234,56
        s = s.Trim().Replace(" ", "");
        // If has both . and , → German format
        if (s.Contains('.') && s.Contains(','))
            s = s.Replace(".", "").Replace(",", ".");
        else if (s.Contains(',') && !s.Contains('.'))
            s = s.Replace(",", ".");
        if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
            return Math.Round(v, 2);
        return null;
    }

    private static string? ParseDateString(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        // Try various date formats
        string[] formats = [
            "dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "d/M/yyyy",
            "dd.MM.yy", "d.M.yy", "yyyy-MM-dd",
            "dd. MMMM yyyy", "d. MMMM yyyy"
        ];
        foreach (var fmt in formats)
        {
            if (DateTime.TryParseExact(s, fmt, new CultureInfo("de-DE"), DateTimeStyles.None, out var dt))
                return dt.ToString("yyyy-MM-dd");
        }
        if (DateTime.TryParse(s, new CultureInfo("de-DE"), DateTimeStyles.None, out var dt2))
            return dt2.ToString("yyyy-MM-dd");
        return null;
    }
}
