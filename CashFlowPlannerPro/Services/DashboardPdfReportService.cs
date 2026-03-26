using System.Globalization;
using System.IO;
using System.Text;
using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.ViewModels;

namespace CashFlowPlannerPro.Services;

public static class DashboardPdfReportService
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    public static void ExportMonthlyReport(string path, DashboardViewModel vm)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var data = BuildData(vm);
        StyledPdfWriter.Write(path, data);
    }

    public static string BuildMonthlyReportPreview(DashboardViewModel vm)
    {
        var data = BuildData(vm);
        var sb = new StringBuilder();
        sb.AppendLine("CashFlow Planner Pro");
        sb.AppendLine("Monatsreport Dashboard");
        sb.AppendLine($"Erstellt am: {data.GeneratedAt}");
        sb.AppendLine();
        sb.AppendLine("Finanzbild");
        foreach (var item in data.Kpis)
            sb.AppendLine($"{item.Label}: {item.Value}");
        sb.AppendLine();
        sb.AppendLine("Operative Signale");
        foreach (var item in data.Operations)
            sb.AppendLine($"{item.Label}: {item.Value}");
        sb.AppendLine();
        sb.AppendLine("Monatswerte");
        foreach (var row in data.MonthRows)
            sb.AppendLine($"{row.Month} | {Money(row.Income)} | {Money(row.Expenses)} | {Money(row.Net)}");
        return sb.ToString();
    }

    private static ReportData BuildData(DashboardViewModel vm)
    {
        var overdueTodos = Database.Instance.GetAllTodos()
            .Where(t => !string.Equals(t.Status, "Erledigt", StringComparison.OrdinalIgnoreCase))
            .Where(t => DateTime.TryParse(t.DueDate, out var due) && due.Date < DateTime.Today)
            .OrderBy(t => t.DueDate)
            .Take(4)
            .Select(t => new CriticalTodo(
                Ascii(t.Title),
                DateTime.TryParse(t.DueDate, out var dt) ? dt.ToString("dd.MM.yyyy", De) : "-"))
            .ToList();

        return new ReportData(
            Company: CompanyProfileService.Load(),
            GeneratedAt: DateTime.Now.ToString("dd.MM.yyyy HH:mm", De),
            Period: $"{vm.HorizonMonths} Monate Vorschau",
            Kpis:
            [
                new ReportKpi("Kontostand Heute", Ascii(vm.CurrentBalance)),
                new ReportKpi("Prognose Ende", Ascii(vm.ForecastEnd)),
                new ReportKpi("Offene Rechnungen", Ascii(vm.OpenInvoices)),
                new ReportKpi("Aktive Angebote", Ascii(vm.ActiveOffers)),
                new ReportKpi("Monatl. Cashflow", Ascii(vm.MonthlyCashflow)),
                new ReportKpi("Burn Rate", Ascii(vm.BurnRate)),
                new ReportKpi("Runway", Ascii(vm.Runway)),
                new ReportKpi("Stunden diesen Monat", Ascii(vm.HoursThisMonth))
            ],
            Operations:
            [
                new ReportKpi("Offene ToDos", Ascii(vm.OpenTodos)),
                new ReportKpi("Ueberfaellig", Ascii(vm.OverdueTodos)),
                new ReportKpi("Auslastung", Ascii(vm.TeamUtilization)),
                new ReportKpi("Laufende Timer", Ascii(vm.RunningTimers))
            ],
            ForecastSettings:
                $"Rechnungen {(vm.IncludeInvoices ? "an" : "aus")} | Angebote offen {(vm.IncludeOffersOffen ? "an" : "aus")} | Beauftragt {(vm.IncludeOffersBeauftragt ? "an" : "aus")} | Wiederkehrend {(vm.IncludeRecurring ? "an" : "aus")}",
            CriticalTodos: overdueTodos,
            MonthRows: vm.MonthRows.Take(8).ToList()
        );
    }

    private static string Money(double value) => value.ToString("N2", De) + " EUR";

    private static string Ascii(string value)
    {
        return value
            .Replace("€", "EUR")
            .Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue")
            .Replace("Ä", "Ae").Replace("Ö", "Oe").Replace("Ü", "Ue")
            .Replace("ß", "ss")
            .Replace("∞", "unbegrenzt");
    }

    private sealed record ReportKpi(string Label, string Value);
    private sealed record CriticalTodo(string Title, string DueDate);
    private sealed record ReportData(
        CompanyProfile Company,
        string GeneratedAt,
        string Period,
        IReadOnlyList<ReportKpi> Kpis,
        IReadOnlyList<ReportKpi> Operations,
        string ForecastSettings,
        IReadOnlyList<CriticalTodo> CriticalTodos,
        IReadOnlyList<MonthRow> MonthRows);

    private static class StyledPdfWriter
    {
        private const double PageWidth = 595;
        private const double PageHeight = 842;
        private const double Margin = 28;

        public static void Write(string path, ReportData data)
        {
            var canvas = new PdfCanvas(PageWidth, PageHeight);
            DrawPage(canvas, data);
            PdfDocumentWriter.Write(path, canvas);
        }

        private static void DrawPage(PdfCanvas c, ReportData data)
        {
            c.Fill(0xF7, 0xF3, 0xFB);

            DrawHero(c, data);
            DrawTopKpis(c, data);
            DrawMiddleBlocks(c, data);
            DrawMonthTable(c, data);
        }

        private static void DrawHero(PdfCanvas c, ReportData data)
        {
            c.RoundRect(Margin, 650, 539, 152, 18, fill: Rgb(0x34, 0x2B, 0x6C), stroke: Rgb(0x47, 0x3E, 0x80), lineWidth: 1);

            // Company name & contact at top
            if (!string.IsNullOrWhiteSpace(data.Company.CompanyName))
            {
                c.Text(data.Company.CompanyName, Margin + 24, 786, 10, true, Rgb(0xDE, 0xD8, 0xF6));
                var contact = BuildCompanyContactLine(data.Company);
                if (!string.IsNullOrWhiteSpace(contact))
                    c.Text(contact, Margin + 24, 774, 7, false, Rgb(0xCF, 0xC7, 0xEC));
            }

            // Monatsreport badge — below company info
            c.RoundRect(Margin + 18, 750, 108, 22, 11, fill: Rgb(0x47, 0x3E, 0x80), stroke: null, lineWidth: 0);
            c.Circle(Margin + 33, 761, 4, fill: Rgb(0xF2, 0xA5, 0xCC));
            c.Text("Monatsreport", Margin + 44, 756, 10, true, Rgb(255, 255, 255));

            c.Text("CashFlow Planner Pro", Margin + 24, 720, 22, true, Rgb(255, 255, 255));
            c.Text("Executive Summary fuer Cashflow, Pipeline und operative Steuerung", Margin + 24, 700, 10, false, Rgb(0xDE, 0xD8, 0xF6));

            c.Text("Erstellt", 440, 748, 8, false, Rgb(0xCF, 0xC7, 0xEC));
            c.Text(data.GeneratedAt, 440, 733, 10, true, Rgb(255, 255, 255));
            c.Text("Zeitraum", 440, 715, 8, false, Rgb(0xCF, 0xC7, 0xEC));
            c.Text(data.Period, 440, 700, 10, true, Rgb(255, 255, 255));
        }

        private static void DrawTopKpis(PdfCanvas c, ReportData data)
        {
            double x = Margin;
            double y = 555;
            double w = 124;
            double h = 82;

            for (int i = 0; i < 4; i++)
            {
                var item = data.Kpis[i];
                double cardX = x + i * (w + 10);
                c.RoundRect(cardX, y, w, h, 14, fill: Rgb(255, 255, 255), stroke: Rgb(0xE8, 0xDD, 0xF1), lineWidth: 1);
                c.Text(item.Label, cardX + 10, y + 57, 8, false, Rgb(0x6C, 0x63, 0x8A));
                // Auto-fit font size based on value length
                double fontSize = item.Value.Length > 16 ? 9 : item.Value.Length > 12 ? 10 : 11;
                c.Text(Fit(item.Value, 20), cardX + 10, y + 33, fontSize, true, Rgb(0x2A, 0x23, 0x59));
            }
        }

        private static void DrawMiddleBlocks(PdfCanvas c, ReportData data)
        {
            c.RoundRect(Margin, 330, 305, 205, 16, fill: Rgb(255, 255, 255), stroke: Rgb(0xE8, 0xDD, 0xF1), lineWidth: 1);
            c.Text("Live-Finanzbild", Margin + 16, 510, 15, true, Rgb(0x2A, 0x23, 0x59));
            c.Text("Kompakte Management-Sicht auf die aktuelle Lage", Margin + 16, 492, 11, false, Rgb(0x7A, 0x72, 0x96));

            DrawMiniInfoCard(c, Margin + 16, 385, 132, 46, data.Kpis[4], Rgb(0xF6, 0xF0, 0xFB));
            DrawMiniInfoCard(c, Margin + 157, 385, 132, 46, data.Kpis[5], Rgb(0xF6, 0xF0, 0xFB));
            DrawMiniInfoCard(c, Margin + 16, 332, 132, 46, data.Kpis[6], Rgb(0xF6, 0xF0, 0xFB));
            DrawMiniInfoCard(c, Margin + 157, 332, 132, 46, data.Kpis[7], Rgb(0xF6, 0xF0, 0xFB));

            c.RoundRect(334, 330, 233, 205, 16, fill: Rgb(255, 255, 255), stroke: Rgb(0xE8, 0xDD, 0xF1), lineWidth: 1);
            c.Text("Operative Signale", 350, 510, 15, true, Rgb(0x2A, 0x23, 0x59));
            c.Text("Kennzahlen und kritische Aufgaben", 350, 492, 11, false, Rgb(0x7A, 0x72, 0x96));

            DrawSignalCard(c, 350, 438, 96, 42, data.Operations[0], Rgb(0xFF, 0xF5, 0xF5), Rgb(0xBF, 0x24, 0x7A));
            DrawSignalCard(c, 455, 438, 96, 42, data.Operations[1], Rgb(0xFF, 0xF7, 0xED), Rgb(0xD9, 0x73, 0x1A));
            DrawSignalCard(c, 350, 388, 96, 42, data.Operations[2], Rgb(0xF3, 0xF7, 0xFF), Rgb(0x36, 0x59, 0xB3));
            DrawSignalCard(c, 455, 388, 96, 42, data.Operations[3], Rgb(0xF1, 0xFB, 0xF9), Rgb(0x0F, 0x9D, 0x7A));

            c.RoundRect(350, 341, 201, 36, 12, fill: Rgb(0xFC, 0xFA, 0xFF), stroke: Rgb(0xEA, 0xE0, 0xF3), lineWidth: 1);
            c.Text("Kritische ToDos", 362, 362, 11, true, Rgb(0x2A, 0x23, 0x59));
            var todo = data.CriticalTodos.FirstOrDefault();
            if (todo != null)
            {
                c.Text(todo.Title, 362, 347, 10, false, Rgb(0x2A, 0x23, 0x59));
                c.Text($"Faellig: {todo.DueDate}", 362, 335, 9, false, Rgb(0xBF, 0x24, 0x7A));
            }
            else
            {
                c.Text("Keine kritischen ToDos im aktuellen Report.", 362, 347, 10, false, Rgb(0x7A, 0x72, 0x96));
            }
        }

        private static void DrawMonthTable(PdfCanvas c, ReportData data)
        {
            c.RoundRect(Margin, 62, 539, 250, 16, fill: Rgb(255, 255, 255), stroke: Rgb(0xE8, 0xDD, 0xF1), lineWidth: 1);
            c.Text("Forecast Monatswerte", Margin + 16, 287, 13, true, Rgb(0x2A, 0x23, 0x59));
            c.Text(data.ForecastSettings, Margin + 180, 289, 6, false, Rgb(0x7A, 0x72, 0x96));

            double tableX = Margin + 16;
            double tableY = 92;
            double[] widths = [70, 66, 66, 66, 72, 66, 69];
            string[] headers = ["Monat", "Einnahmen", "Ausgaben", "Netto", "Kumuliert", "Ziel", "Abweichung"];

            double currentX = tableX;
            for (int i = 0; i < headers.Length; i++)
            {
                c.Rect(currentX, tableY + 170, widths[i], 24, fill: Rgb(0xEE, 0xE7, 0xF5), stroke: Rgb(0xE8, 0xDD, 0xF1), lineWidth: 1);
                c.Text(headers[i], currentX + 5, tableY + 178, 8, true, Rgb(0x2A, 0x23, 0x59));
                currentX += widths[i];
            }

            for (int rowIndex = 0; rowIndex < data.MonthRows.Count; rowIndex++)
            {
                var row = data.MonthRows[rowIndex];
                string[] cells =
                [
                    row.Month,
                    Money(row.Income),
                    Money(row.Expenses),
                    Money(row.Net),
                    Money(row.Cumulative),
                    Money(row.Target),
                    Money(row.Variance)
                ];

                double rowY = tableY + 146 - (rowIndex * 22);
                currentX = tableX;
                for (int col = 0; col < cells.Length; col++)
                {
                    var bg = rowIndex % 2 == 0 ? Rgb(255, 255, 255) : Rgb(0xFB, 0xF8, 0xFD);
                    c.Rect(currentX, rowY, widths[col], 22, fill: bg, stroke: Rgb(0xE8, 0xDD, 0xF1), lineWidth: 1);
                    c.Text(Trim(cells[col], col == 0 ? 10 : 10), currentX + 5, rowY + 8, 7.5, false, Rgb(0x2A, 0x23, 0x59));
                    currentX += widths[col];
                }
            }
        }

        private static void DrawMiniInfoCard(PdfCanvas c, double x, double y, double w, double h, ReportKpi item, PdfColor bg)
        {
            c.RoundRect(x, y, w, h, 12, fill: bg, stroke: null, lineWidth: 0);
            c.Text(item.Label, x + 8, y + 29, 8, false, Rgb(0x6C, 0x63, 0x8A));
            double fontSize = item.Value.Length > 14 ? 9 : item.Value.Length > 10 ? 10 : 11;
            c.Text(Fit(item.Value, 18), x + 8, y + 13, fontSize, true, Rgb(0x2A, 0x23, 0x59));
        }

        private static void DrawSignalCard(PdfCanvas c, double x, double y, double w, double h, ReportKpi item, PdfColor bg, PdfColor valueColor)
        {
            c.RoundRect(x, y, w, h, 12, fill: bg, stroke: null, lineWidth: 0);
            c.Text(item.Label, x + 8, y + 27, 8.5, false, Rgb(0x8A, 0x74, 0x80));
            c.Text(Fit(item.Value, 10), x + 8, y + 11, 10.5, true, valueColor);
        }

        private static string Trim(string value, int len) => value.Length > len ? value[..len] : value;
        private static string Fit(string value, int len) => value.Length > len ? value.Substring(0, len - 1) + "…" : value;

        private static string BuildCompanyContactLine(CompanyProfile profile)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(profile.AddressLine1)) parts.Add(profile.AddressLine1);
            if (!string.IsNullOrWhiteSpace(profile.ContactEmail)) parts.Add(profile.ContactEmail);
            if (!string.IsNullOrWhiteSpace(profile.ContactPhone)) parts.Add(profile.ContactPhone);
            if (!string.IsNullOrWhiteSpace(profile.Website)) parts.Add(profile.Website);
            return string.Join(" | ", parts);
        }

        private static PdfColor Rgb(int r, int g, int b) => new(r / 255.0, g / 255.0, b / 255.0);
    }

    private sealed class PdfCanvas(double width, double height)
    {
        private readonly StringBuilder _content = new();

        public byte[] BuildContent()
        {
            return Encoding.ASCII.GetBytes(_content.ToString());
        }

        public void Fill(int r, int g, int b)
        {
            Rect(0, 0, width, height, new PdfColor(r / 255.0, g / 255.0, b / 255.0), null, 0);
        }

        public void Rect(double x, double y, double w, double h, PdfColor fill, PdfColor? stroke, double lineWidth)
        {
            _content.AppendLine("q");
            if (fill != null) _content.AppendLine(fill.ToFillColor());
            if (stroke != null)
            {
                _content.AppendLine(stroke.ToStrokeColor());
                _content.AppendLine($"{Fmt(lineWidth)} w");
            }
            _content.AppendLine($"{Fmt(x)} {Fmt(y)} {Fmt(w)} {Fmt(h)} re");
            _content.AppendLine(stroke == null ? "f" : "B");
            _content.AppendLine("Q");
        }

        public void RoundRect(double x, double y, double w, double h, double r, PdfColor fill, PdfColor? stroke, double lineWidth)
        {
            double k = 0.5522847498;
            double c = r * k;
            _content.AppendLine("q");
            _content.AppendLine(fill.ToFillColor());
            if (stroke != null)
            {
                _content.AppendLine(stroke.ToStrokeColor());
                _content.AppendLine($"{Fmt(lineWidth)} w");
            }
            _content.AppendLine($"{Fmt(x + r)} {Fmt(y)} m");
            _content.AppendLine($"{Fmt(x + w - r)} {Fmt(y)} l");
            _content.AppendLine($"{Fmt(x + w - r + c)} {Fmt(y)} {Fmt(x + w)} {Fmt(y + r - c)} {Fmt(x + w)} {Fmt(y + r)} c");
            _content.AppendLine($"{Fmt(x + w)} {Fmt(y + h - r)} l");
            _content.AppendLine($"{Fmt(x + w)} {Fmt(y + h - r + c)} {Fmt(x + w - r + c)} {Fmt(y + h)} {Fmt(x + w - r)} {Fmt(y + h)} c");
            _content.AppendLine($"{Fmt(x + r)} {Fmt(y + h)} l");
            _content.AppendLine($"{Fmt(x + r - c)} {Fmt(y + h)} {Fmt(x)} {Fmt(y + h - r + c)} {Fmt(x)} {Fmt(y + h - r)} c");
            _content.AppendLine($"{Fmt(x)} {Fmt(y + r)} l");
            _content.AppendLine($"{Fmt(x)} {Fmt(y + r - c)} {Fmt(x + r - c)} {Fmt(y)} {Fmt(x + r)} {Fmt(y)} c");
            _content.AppendLine(stroke == null ? "f" : "B");
            _content.AppendLine("Q");
        }

        public void Circle(double x, double y, double r, PdfColor fill)
        {
            RoundRect(x - r, y - r, r * 2, r * 2, r, fill, null, 0);
        }

        public void Text(string text, double x, double y, double size, bool bold, PdfColor color)
        {
            _content.AppendLine("BT");
            _content.AppendLine(color.ToFillColor());
            _content.AppendLine($"/{(bold ? "F2" : "F1")} {Fmt(size)} Tf");
            _content.AppendLine($"1 0 0 1 {Fmt(x)} {Fmt(y)} Tm");
            _content.AppendLine($"({Escape(text)}) Tj");
            _content.AppendLine("ET");
        }

        private static string Escape(string text) =>
            text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)")
                .Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue")
                .Replace("Ä", "Ae").Replace("Ö", "Oe").Replace("Ü", "Ue")
                .Replace("ß", "ss").Replace("€", "EUR").Replace("∞", "unbegrenzt");

        private static string Fmt(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private sealed record PdfColor(double R, double G, double B)
    {
        public string ToFillColor() => $"{Fmt(R)} {Fmt(G)} {Fmt(B)} rg";
        public string ToStrokeColor() => $"{Fmt(R)} {Fmt(G)} {Fmt(B)} RG";
        private static string Fmt(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static class PdfDocumentWriter
    {
        public static void Write(string path, PdfCanvas canvas)
        {
            var content = canvas.BuildContent();
            var objects = new List<byte[]>
            {
                Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
                Ascii("<< /Type /Pages /Count 1 /Kids [5 0 R] >>"),
                Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"),
                Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>"),
                Ascii("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents 6 0 R >>"),
                Combine(Ascii($"<< /Length {content.Length} >>\nstream\n"), content, Ascii("\nendstream"))
            };

            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(fs, Encoding.ASCII);
            bw.Write(Ascii("%PDF-1.4\n"));

            var offsets = new List<long> { 0 };
            for (int i = 0; i < objects.Count; i++)
            {
                offsets.Add(fs.Position);
                bw.Write(Ascii($"{i + 1} 0 obj\n"));
                bw.Write(objects[i]);
                bw.Write(Ascii("\nendobj\n"));
            }

            long xref = fs.Position;
            bw.Write(Ascii($"xref\n0 {objects.Count + 1}\n"));
            bw.Write(Ascii("0000000000 65535 f \n"));
            foreach (var offset in offsets.Skip(1))
                bw.Write(Ascii($"{offset:0000000000} 00000 n \n"));
            bw.Write(Ascii($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF"));
        }

        private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

        private static byte[] Combine(params byte[][] arrays)
        {
            using var ms = new MemoryStream();
            foreach (var item in arrays)
                ms.Write(item, 0, item.Length);
            return ms.ToArray();
        }
    }
}
