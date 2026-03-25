using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace CashFlowPlannerPro.Services;

public class ScannedLineItem
{
    public int Position { get; set; }
    public string Description { get; set; } = "";
    public double Quantity { get; set; } = 1;
    public string Unit { get; set; } = "Stk";
    public double UnitPrice { get; set; }
    public double Total { get; set; }
}

public class ScannedOffer
{
    public string? OfferNumber { get; set; }
    public string? OfferDate { get; set; }
    public string? ValidUntil { get; set; }
    public string? Customer { get; set; }
    public double? NetAmount { get; set; }
    public double? VatAmount { get; set; }
    public double? GrossAmount { get; set; }
    public double? VatRate { get; set; }
    public string? Description { get; set; }
    public string? PaymentTerms { get; set; }
    public List<ScannedLineItem> LineItems { get; set; } = [];
    public string RawText { get; set; } = "";
    public double Confidence { get; set; }
}

public static class OfferScanner
{
    public static ScannedOffer ScanPdf(string pdfPath)
    {
        var result = new ScannedOffer();
        var text = ExtractText(pdfPath);
        result.RawText = text;
        if (string.IsNullOrWhiteSpace(text)) return result;

        int found = 0;

        // Offer number
        result.OfferNumber = FindPattern(text,
            @"(?:Angebots?(?:nummer|nr\.?|[\s-]?Nr\.?))\s*[:\s]?\s*(\S+)",
            @"(?:Angebot\s+Nr\.?|Offer\s*(?:No\.?|#))\s*[:\s]?\s*(\S+)",
            @"(?:Kostenvoranschlag\s*Nr\.?)\s*[:\s]?\s*(\S+)");
        if (result.OfferNumber != null) found++;

        // Offer date
        var dateStr = FindPattern(text,
            @"(?:Angebots?datum|Datum|Date)\s*[:\s]?\s*(\d{1,2}[./]\d{1,2}[./]\d{2,4})");
        result.OfferDate = ParseDate(dateStr);
        if (result.OfferDate != null) found++;

        // Valid until
        var validStr = FindPattern(text,
            @"(?:Gültig(?:keit)?\s*bis|Gültig bis zum|Valid\s*until|Bindefrist)\s*[:\s]?\s*(\d{1,2}[./]\d{1,2}[./]\d{2,4})");
        result.ValidUntil = ParseDate(validStr);

        // Customer
        var headerArea = text.Length > 600 ? text[..600] : text;
        result.Customer = FindPattern(headerArea,
            @"(?:Kunde|Empfänger|Auftraggeber|An|To|Firma)\s*[:\s]?\s*\n?\s*(.+?)(?:\n|$)");
        if (result.Customer != null) { result.Customer = result.Customer.Trim().TrimEnd(':'); found++; }

        // Amounts
        var grossStr = FindPattern(text,
            @"(?:Gesamt(?:betrag)?|Brutto(?:betrag)?|Total|Angebotssumme|Endbetrag)\s*[:\s]?\s*[€$]?\s*([\d.,]+)",
            @"[€$]\s*([\d.,]+)\s*(?:brutto|gesamt|inkl)");
        result.GrossAmount = ParseAmount(grossStr);
        if (result.GrossAmount != null) found++;

        var netStr = FindPattern(text,
            @"(?:Netto(?:betrag)?|Zwischensumme|Subtotal|Summe\s*netto)\s*[:\s]?\s*[€$]?\s*([\d.,]+)");
        result.NetAmount = ParseAmount(netStr);

        var vatStr = FindPattern(text,
            @"(?:MwSt\.?|USt\.?|Umsatzsteuer|Mehrwertsteuer|VAT)\s*(?:\(?\s*\d+\s*%?\s*\)?)?\s*[:\s]?\s*[€$]?\s*([\d.,]+)");
        result.VatAmount = ParseAmount(vatStr);

        var rateMatch = Regex.Match(text, @"(\d{1,2})\s*%\s*(?:MwSt|USt|Umsatzsteuer|Mehrwertsteuer|VAT)", RegexOptions.IgnoreCase);
        if (rateMatch.Success) result.VatRate = double.Parse(rateMatch.Groups[1].Value);

        // Payment terms
        result.PaymentTerms = FindPattern(text,
            @"(?:Zahlungsbedingung(?:en)?|Payment\s*Terms?)\s*[:\s]?\s*(.+?)(?:\n|$)",
            @"(Zahlbar\s+innerhalb\s+von\s+\d+\s+Tagen.*?)(?:\n|$)");

        // Line items - multiple patterns
        result.LineItems = ExtractLineItems(text);
        if (result.LineItems.Count > 0) found++;

        // Description
        if (result.LineItems.Count > 0)
            result.Description = string.Join("; ", result.LineItems.Take(5).Select(l => l.Description));
        else
            result.Description = FindPattern(text,
                @"(?:Betreff|Leistung|Beschreibung|Gegenstand|Subject)\s*[:\s]?\s*(.+?)(?:\n|$)");

        result.Confidence = Math.Min(1.0, found / 5.0);
        return result;
    }

    private static List<ScannedLineItem> ExtractLineItems(string text)
    {
        var items = new List<ScannedLineItem>();

        // Pattern 1: "Pos Description Qty Unit UnitPrice Total"
        // e.g. "1  Beratungsleistung  10  Std  120,00  1.200,00"
        var pattern1 = Regex.Matches(text,
            @"^\s*(\d{1,3})[.\s)]+(.{5,60}?)\s+([\d.,]+)\s+(Std\.?|Stk\.?|Psch\.?|h|m²|m³|lfm|kg|t|Monat[e]?|Tag[e]?|Stück)\s+([\d.,]+)\s+([\d.,]+)",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);
        foreach (Match m in pattern1)
        {
            items.Add(new ScannedLineItem {
                Position = int.Parse(m.Groups[1].Value),
                Description = m.Groups[2].Value.Trim(),
                Quantity = ParseAmount(m.Groups[3].Value) ?? 1,
                Unit = m.Groups[4].Value.Trim(),
                UnitPrice = ParseAmount(m.Groups[5].Value) ?? 0,
                Total = ParseAmount(m.Groups[6].Value) ?? 0
            });
        }
        if (items.Count > 0) return items;

        // Pattern 2: "Pos Description Total"
        // e.g. "1. Statische Berechnung    2.500,00 €"
        var pattern2 = Regex.Matches(text,
            @"^\s*(\d{1,3})[.\s)]+(.{5,80}?)\s{2,}([\d.,]+)\s*[€$]?\s*$",
            RegexOptions.Multiline);
        foreach (Match m in pattern2)
        {
            var desc = m.Groups[2].Value.Trim();
            // Skip summary lines
            if (Regex.IsMatch(desc, @"(?:Summe|Total|Netto|Brutto|MwSt|USt|Gesamt|Zwischensumme|IBAN)", RegexOptions.IgnoreCase))
                continue;
            items.Add(new ScannedLineItem {
                Position = int.Parse(m.Groups[1].Value),
                Description = desc,
                Total = ParseAmount(m.Groups[3].Value) ?? 0
            });
        }
        if (items.Count > 0) return items;

        // Pattern 3: Dash/bullet items with amounts
        var pattern3 = Regex.Matches(text,
            @"^\s*[-•–]\s+(.{5,80}?)\s{2,}([\d.,]+)\s*[€$]?\s*$",
            RegexOptions.Multiline);
        int pos = 1;
        foreach (Match m in pattern3)
        {
            var desc = m.Groups[1].Value.Trim();
            if (Regex.IsMatch(desc, @"(?:Summe|Total|Netto|Brutto|MwSt|USt|Gesamt)", RegexOptions.IgnoreCase))
                continue;
            items.Add(new ScannedLineItem {
                Position = pos++,
                Description = desc,
                Total = ParseAmount(m.Groups[2].Value) ?? 0
            });
        }

        return items;
    }

    private static string ExtractText(string pdfPath)
    {
        try
        {
            using var doc = PdfDocument.Open(pdfPath);
            var sb = new System.Text.StringBuilder();
            foreach (var page in doc.GetPages()) sb.AppendLine(page.Text);
            var text = sb.ToString();
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("PDF enthält keinen extrahierbaren Text. Möglicherweise ist es ein gescanntes Bild-PDF.");
            return text;
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException($"PDF konnte nicht gelesen werden: {ex.Message}", ex);
        }
    }

    private static string? FindPattern(string text, params string[] patterns)
    {
        foreach (var p in patterns)
        {
            var m = Regex.Match(text, p, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (m.Success) return m.Groups[m.Groups.Count - 1].Value.Trim();
        }
        return null;
    }

    private static double? ParseAmount(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim().Replace(" ", "");
        if (s.Contains('.') && s.Contains(','))
            s = s.Replace(".", "").Replace(",", ".");
        else if (s.Contains(',') && !s.Contains('.'))
            s = s.Replace(",", ".");
        if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
            return Math.Round(v, 2);
        return null;
    }

    private static string? ParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        string[] fmts = ["dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "dd.MM.yy", "yyyy-MM-dd"];
        foreach (var f in fmts)
            if (DateTime.TryParseExact(s.Trim(), f, new CultureInfo("de-DE"), DateTimeStyles.None, out var dt))
                return dt.ToString("yyyy-MM-dd");
        if (DateTime.TryParse(s.Trim(), new CultureInfo("de-DE"), DateTimeStyles.None, out var dt2))
            return dt2.ToString("yyyy-MM-dd");
        return null;
    }
}
