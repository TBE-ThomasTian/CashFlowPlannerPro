using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CashFlowPlannerPro.Models;
using Microsoft.Data.Sqlite;

namespace CashFlowPlannerPro.Data;

public sealed class Database : IDisposable {
    static readonly Lazy<Database> _instance = new(() => new Database());
    public static Database Instance => _instance.Value;
    SqliteConnection? _conn;
    Database() { }

    public void Open(string path) {
        Close();
        _conn = new SqliteConnection($"Data Source={path}");
        _conn.Open();
        Exec("PRAGMA journal_mode=WAL;");
        Exec("PRAGMA foreign_keys=ON;");
    }

    public void Close() {
        if (_conn != null) { _conn.Close(); _conn.Dispose(); _conn = null; }
    }

    public void Dispose() => Close();

    SqliteConnection Conn => _conn ?? throw new InvalidOperationException("Database not open");

    void Exec(string sql) { using var cmd = Conn.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }

    static string MonthLabel(int y, int m) => $"{y:D4}-{m:D2}";

    static DateTime AddMonthsClamped(DateTime d, int months) {
        int totalM = (d.Year * 12 + d.Month - 1) + months;
        int y = totalM / 12; int m = totalM % 12 + 1;
        int maxDay = DateTime.DaysInMonth(y, m);
        return new DateTime(y, m, Math.Min(d.Day, maxDay));
    }

    static DateTime? ParseDate(string? s) {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d1)) return d1;
        if (DateTime.TryParseExact(s, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2)) return d2;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d3)) return d3;
        return null;
    }

    public void EnsureSchema() {
        Exec(@"CREATE TABLE IF NOT EXISTS users(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            username TEXT UNIQUE NOT NULL,
            password_hash TEXT NOT NULL,
            full_name TEXT,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP)");
        using (var cmd = Conn.CreateCommand()) {
            cmd.CommandText = "SELECT COUNT(*) FROM users";
            if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
                Exec("INSERT INTO users (username, password_hash, full_name) VALUES ('admin', 'admin', 'Administrator')");
        }
        Exec("CREATE TABLE IF NOT EXISTS categories(id INTEGER PRIMARY KEY AUTOINCREMENT,name TEXT UNIQUE NOT NULL)");
        Exec("CREATE TABLE IF NOT EXISTS persons(id INTEGER PRIMARY KEY AUTOINCREMENT,name TEXT UNIQUE NOT NULL)");
        Exec(@"CREATE TABLE IF NOT EXISTS transactions(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            date TEXT NOT NULL, description TEXT, amount REAL NOT NULL,
            category_id INTEGER, person_id INTEGER, interval TEXT, notes TEXT,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP, updated_at TEXT,
            FOREIGN KEY(category_id) REFERENCES categories(id),
            FOREIGN KEY(person_id) REFERENCES persons(id))");
        Exec(@"CREATE TABLE IF NOT EXISTS offers(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            offer_number TEXT, offer_date TEXT, date_expected TEXT,
            customer TEXT, amount REAL, probability REAL, description TEXT, status TEXT,
            payment_delay INTEGER DEFAULT 30, pdf_path TEXT,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP)");
        Exec(@"CREATE TABLE IF NOT EXISTS invoices(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            issue_date TEXT, due_date TEXT, customer TEXT, amount REAL,
            description TEXT, paid_date TEXT, paid_amount REAL, status TEXT,
            pdf_path TEXT, created_at TEXT DEFAULT CURRENT_TIMESTAMP)");
        Exec(@"CREATE TABLE IF NOT EXISTS targets(
            id INTEGER PRIMARY KEY AUTOINCREMENT, year INTEGER, month INTEGER, amount REAL)");
        Exec("CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT)");
        Exec(@"CREATE TABLE IF NOT EXISTS resources(
            id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, role TEXT,
            availability REAL DEFAULT 1.0, hourly_rate REAL DEFAULT 0,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP)");
        Exec(@"CREATE TABLE IF NOT EXISTS projects(
            id INTEGER PRIMARY KEY AUTOINCREMENT, project_number TEXT, name TEXT NOT NULL,
            color TEXT DEFAULT '#3498db', start_date TEXT, end_date TEXT,
            budget REAL DEFAULT 0, status TEXT DEFAULT 'active',
            created_at TEXT DEFAULT CURRENT_TIMESTAMP)");
        Exec(@"CREATE TABLE IF NOT EXISTS resource_allocations(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            resource_id INTEGER NOT NULL, project_id INTEGER NOT NULL,
            date TEXT NOT NULL, hours REAL DEFAULT 8.0, notes TEXT,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY(resource_id) REFERENCES resources(id) ON DELETE CASCADE,
            FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE,
            UNIQUE(resource_id, project_id, date))");
        Exec("CREATE INDEX IF NOT EXISTS idx_allocations_resource ON resource_allocations(resource_id)");
        Exec("CREATE INDEX IF NOT EXISTS idx_allocations_project ON resource_allocations(project_id)");
        Exec("CREATE INDEX IF NOT EXISTS idx_allocations_date ON resource_allocations(date)");
        var cats = new[] { "Lohn","Kapitalsteuer","Sozialversicherung","Lohnsteuer","Umsatzsteuer","Versicherung","Miete","Strom","Steuerberatung" };
        foreach (var c in cats) {
            using var cmd = Conn.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO categories(name) VALUES(@n)";
            cmd.Parameters.AddWithValue("@n", c);
            cmd.ExecuteNonQuery();
        }
    }

    // Settings
    public double GetSettingStartBalance() {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key='start_balance'";
        var r = cmd.ExecuteScalar();
        return r != null ? Convert.ToDouble(r, CultureInfo.InvariantCulture) : 0.0;
    }

    public void SetSettingStartBalance(double v) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "INSERT INTO settings(key,value) VALUES('start_balance',@v) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
        cmd.Parameters.AddWithValue("@v", v.ToString(CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    // Targets map
    public Dictionary<string, double> GetTargets() {
        var map = new Dictionary<string, double>();
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT year,month,amount FROM targets";
        using var r = cmd.ExecuteReader();
        while (r.Read()) map[MonthLabel(r.GetInt32(0), r.GetInt32(1))] = r.GetDouble(2);
        return map;
    }

    // Monthly Cashflow
    public List<MonthRow> MonthlyCashflow(int horizonMonths, bool includeOffersOffen, bool includeOffersBeauftragt, bool includeUnpaidInvoices, bool includeRecurring) {
        var evs = new List<(DateTime d, double a)>();
        // Transactions
        using (var cmd = Conn.CreateCommand()) {
            cmd.CommandText = "SELECT date,amount,COALESCE(interval,''),notes FROM transactions";
            using var r = cmd.ExecuteReader();
            while (r.Read()) {
                var d = ParseDate(r.GetString(0));
                if (d == null) continue;
                double a = r.GetDouble(1);
                string it = r.IsDBNull(2) ? "" : r.GetString(2).ToLower().Trim();
                string notes = r.IsDBNull(3) ? "" : r.GetString(3);
                bool isFixkosten = notes.StartsWith("FIXKOSTEN:");
                bool isSteuer = notes.StartsWith("STEUER:");
                if (!includeRecurring || string.IsNullOrEmpty(it) || it == "once" || it == "einmalig") {
                    evs.Add((d.Value, a));
                } else {
                    var cur = d.Value;
                    evs.Add((cur, a));
                    int stepM = 0;
                    if (it == "monthly" || it == "monatlich") stepM = 1;
                    else if (it == "quarterly" || it == "vierteljährlich") stepM = 3;
                    else if (it == "semiannual" || it == "semi-annually" || it == "halbjahr" || it == "halbjährlich") stepM = 6;
                    else if (it == "yearly" || it == "jährlich") stepM = 12;
                    else if (isFixkosten) stepM = 1;
                    else if (isSteuer) stepM = 12;
                    int stepD = it == "biweekly" ? 14 : (it == "weekly" || it == "wöchentlich") ? 7 : 0;
                    var end = AddMonthsClamped(DateTime.Today, horizonMonths);
                    end = new DateTime(end.Year, end.Month, DateTime.DaysInMonth(end.Year, end.Month));
                    int maxIter = horizonMonths * 31;
                    for (int i = 0; i < maxIter; i++) {
                        if (stepM > 0) cur = AddMonthsClamped(cur, stepM);
                        else if (stepD > 0) cur = cur.AddDays(stepD);
                        else break;
                        if (cur > end) break;
                        evs.Add((cur, a));
                    }
                }
            }
        }
        // Invoices
        using (var cmd = Conn.CreateCommand()) {
            cmd.CommandText = "SELECT status,due_date,amount,paid_date,paid_amount FROM invoices";
            using var r = cmd.ExecuteReader();
            while (r.Read()) {
                string status = r.IsDBNull(0) ? "" : r.GetString(0);
                if (status == "Offen" || status == "Überfällig" || string.IsNullOrEmpty(status)) {
                    if (includeUnpaidInvoices) {
                        var due = ParseDate(r.IsDBNull(1) ? null : r.GetString(1));
                        double totalAmt = r.IsDBNull(2) ? 0 : r.GetDouble(2);
                        double paidAmt = r.IsDBNull(4) ? 0 : r.GetDouble(4);
                        double remaining = totalAmt - paidAmt;
                        if (due.HasValue && remaining != 0) evs.Add((due.Value, remaining));
                    }
                } else if (status == "Bezahlt") {
                    string? paidS = r.IsDBNull(3) ? null : r.GetString(3);
                    if (!string.IsNullOrEmpty(paidS)) {
                        var paidD = ParseDate(paidS);
                        double amt = r.IsDBNull(4) ? (r.IsDBNull(2) ? 0 : r.GetDouble(2)) : r.GetDouble(4);
                        if (paidD.HasValue && amt != 0) evs.Add((paidD.Value, amt));
                    }
                }
            }
        }
        // Offers
        if (includeOffersOffen || includeOffersBeauftragt) {
            string where;
            if (includeOffersOffen && includeOffersBeauftragt)
                where = "WHERE status='Offen' OR status='Beauftragt' OR status IS NULL";
            else if (includeOffersOffen)
                where = "WHERE status='Offen' OR status IS NULL";
            else
                where = "WHERE status='Beauftragt'";
            using var cmd = Conn.CreateCommand();
            cmd.CommandText = $"SELECT date_expected,amount,probability,payment_delay,status FROM offers {where}";
            using var r = cmd.ExecuteReader();
            while (r.Read()) {
                var de = ParseDate(r.IsDBNull(0) ? null : r.GetString(0));
                double amt = r.IsDBNull(1) ? 0 : r.GetDouble(1);
                double p = r.IsDBNull(2) ? 0 : r.GetDouble(2);
                int delay = r.IsDBNull(3) ? 30 : r.GetInt32(3);
                if (p > 1.0) p /= 100.0;
                if (p > 0 && de.HasValue) {
                    var payDate = de.Value.AddDays(delay);
                    evs.Add((payDate, amt));
                }
            }
        }
        // Sort and bucket
        evs.Sort((a, b) => a.d.CompareTo(b.d));
        var netMap = new SortedDictionary<string, double>();
        var incMap = new SortedDictionary<string, double>();
        var expMap = new SortedDictionary<string, double>();
        var today = DateTime.Today;
        var startOfMonth = new DateTime(today.Year, today.Month, 1);
        for (int i = 0; i < horizonMonths; i++) {
            var md = AddMonthsClamped(startOfMonth, i);
            var label = MonthLabel(md.Year, md.Month);
            netMap[label] = 0; incMap[label] = 0; expMap[label] = 0;
        }
        foreach (var e in evs) {
            if (e.d < startOfMonth) continue;
            var label = MonthLabel(e.d.Year, e.d.Month);
            if (!netMap.ContainsKey(label)) continue;
            netMap[label] += e.a;
            if (e.a > 0) incMap[label] += e.a; else expMap[label] += e.a;
        }
        return netMap.Select(kv => new MonthRow {
            Month = kv.Key, Net = kv.Value, Income = incMap[kv.Key], Expenses = expMap[kv.Key]
        }).ToList();
    }

    public double ActiveOffersSum() {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT amount,probability FROM offers WHERE status='Offen' OR status='Beauftragt' OR status IS NULL";
        using var r = cmd.ExecuteReader();
        double sum = 0;
        while (r.Read()) {
            double amt = r.GetDouble(0);
            double p = r.IsDBNull(1) ? 0 : r.GetDouble(1);
            if (p > 1.0) p /= 100.0;
            if (p > 0) sum += amt;
        }
        return sum;
    }

    public double OpenInvoicesSum() {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT amount,paid_amount FROM invoices WHERE status='Offen' OR status='Überfällig' OR status IS NULL";
        using var r = cmd.ExecuteReader();
        double sum = 0;
        while (r.Read()) {
            double total = r.GetDouble(0);
            double paid = r.IsDBNull(1) ? 0 : r.GetDouble(1);
            sum += total - paid;
        }
        return sum;
    }

    public string NextOfferNumber() {
        string prefix = $"ANG-{DateTime.Today.Year}-";
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT offer_number FROM offers WHERE offer_number LIKE @p ORDER BY offer_number DESC LIMIT 1";
        cmd.Parameters.AddWithValue("@p", prefix + "%");
        var val = cmd.ExecuteScalar();
        if (val != null) {
            string last = val.ToString()!;
            string numPart = last[prefix.Length..];
            if (int.TryParse(numPart, out int n)) return prefix + (n + 1).ToString("D4");
        }
        return prefix + "0001";
    }

    // CRUD: Transactions
    public List<Transaction> GetTransactions(string? notesFilter = null) {
        var list = new List<Transaction>();
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT id,date,description,amount,category_id,person_id,interval,notes,created_at,updated_at FROM transactions";
        if (notesFilter != null) {
            cmd.CommandText += " WHERE notes LIKE @f";
            cmd.Parameters.AddWithValue("@f", notesFilter + "%");
        }
        using var r = cmd.ExecuteReader();
        while (r.Read()) {
            list.Add(new Transaction {
                Id = r.GetInt64(0),
                Date = r.IsDBNull(1) ? "" : r.GetString(1),
                Description = r.IsDBNull(2) ? "" : r.GetString(2),
                Amount = r.GetDouble(3),
                CategoryId = r.IsDBNull(4) ? null : r.GetInt64(4),
                PersonId = r.IsDBNull(5) ? null : r.GetInt64(5),
                Interval = r.IsDBNull(6) ? "" : r.GetString(6),
                Notes = r.IsDBNull(7) ? "" : r.GetString(7),
                CreatedAt = r.IsDBNull(8) ? "" : r.GetString(8),
                UpdatedAt = r.IsDBNull(9) ? "" : r.GetString(9)
            });
        }
        return list;
    }

    public void AddTransaction(Transaction t) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO transactions(date,description,amount,category_id,person_id,interval,notes,created_at,updated_at)
            VALUES(@date,@desc,@amt,@cat,@per,@intv,@notes,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP)";
        cmd.Parameters.AddWithValue("@date", t.Date);
        cmd.Parameters.AddWithValue("@desc", (object?)t.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@amt", t.Amount);
        cmd.Parameters.AddWithValue("@cat", (object?)t.CategoryId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@per", (object?)t.PersonId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@intv", (object?)t.Interval ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@notes", (object?)t.Notes ?? DBNull.Value);
        cmd.ExecuteNonQuery();
        t.Id = LastInsertId();
    }

    public void UpdateTransaction(Transaction t) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"UPDATE transactions SET date=@date,description=@desc,amount=@amt,
            category_id=@cat,person_id=@per,interval=@intv,notes=@notes,updated_at=CURRENT_TIMESTAMP WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", t.Id);
        cmd.Parameters.AddWithValue("@date", t.Date);
        cmd.Parameters.AddWithValue("@desc", (object?)t.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@amt", t.Amount);
        cmd.Parameters.AddWithValue("@cat", (object?)t.CategoryId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@per", (object?)t.PersonId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@intv", (object?)t.Interval ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@notes", (object?)t.Notes ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void DeleteTransaction(long id) { ExecWithId("DELETE FROM transactions WHERE id=@id", id); }

    // CRUD: Invoices
    public List<Invoice> GetInvoices() {
        var list = new List<Invoice>();
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT id,issue_date,due_date,customer,amount,description,paid_date,paid_amount,status,pdf_path,created_at FROM invoices";
        using var r = cmd.ExecuteReader();
        while (r.Read()) {
            list.Add(new Invoice {
                Id = r.GetInt64(0),
                IssueDate = r.IsDBNull(1) ? "" : r.GetString(1),
                DueDate = r.IsDBNull(2) ? "" : r.GetString(2),
                Customer = r.IsDBNull(3) ? "" : r.GetString(3),
                Amount = r.IsDBNull(4) ? 0 : r.GetDouble(4),
                Description = r.IsDBNull(5) ? "" : r.GetString(5),
                PaidDate = r.IsDBNull(6) ? "" : r.GetString(6),
                PaidAmount = r.IsDBNull(7) ? 0 : r.GetDouble(7),
                Status = r.IsDBNull(8) ? "" : r.GetString(8),
                PdfPath = r.IsDBNull(9) ? "" : r.GetString(9),
                CreatedAt = r.IsDBNull(10) ? "" : r.GetString(10)
            });
        }
        return list;
    }

    public void AddInvoice(Invoice i) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO invoices(issue_date,due_date,customer,amount,description,paid_date,paid_amount,status,pdf_path,created_at)
            VALUES(@issue,@due,@cust,@amt,@desc,@paid_d,@paid_a,@status,@pdf,CURRENT_TIMESTAMP)";
        cmd.Parameters.AddWithValue("@issue", (object?)i.IssueDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@due", (object?)i.DueDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cust", (object?)i.Customer ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@amt", i.Amount);
        cmd.Parameters.AddWithValue("@desc", (object?)i.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@paid_d", (object?)i.PaidDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@paid_a", i.PaidAmount);
        cmd.Parameters.AddWithValue("@status", (object?)i.Status ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pdf", (object?)i.PdfPath ?? DBNull.Value);
        cmd.ExecuteNonQuery();
        i.Id = LastInsertId();
    }

    public void UpdateInvoice(Invoice i) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"UPDATE invoices SET issue_date=@issue,due_date=@due,customer=@cust,amount=@amt,
            description=@desc,paid_date=@paid_d,paid_amount=@paid_a,status=@status,pdf_path=@pdf WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", i.Id);
        cmd.Parameters.AddWithValue("@issue", (object?)i.IssueDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@due", (object?)i.DueDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cust", (object?)i.Customer ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@amt", i.Amount);
        cmd.Parameters.AddWithValue("@desc", (object?)i.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@paid_d", (object?)i.PaidDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@paid_a", i.PaidAmount);
        cmd.Parameters.AddWithValue("@status", (object?)i.Status ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pdf", (object?)i.PdfPath ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void DeleteInvoice(long id) { ExecWithId("DELETE FROM invoices WHERE id=@id", id); }

    // CRUD: Offers
    public List<Offer> GetOffers() {
        var list = new List<Offer>();
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT id,offer_number,offer_date,date_expected,customer,amount,probability,description,status,payment_delay,pdf_path,created_at FROM offers";
        using var r = cmd.ExecuteReader();
        while (r.Read()) {
            list.Add(new Offer {
                Id = r.GetInt64(0),
                OfferNumber = r.IsDBNull(1) ? "" : r.GetString(1),
                OfferDate = r.IsDBNull(2) ? "" : r.GetString(2),
                DateExpected = r.IsDBNull(3) ? "" : r.GetString(3),
                Customer = r.IsDBNull(4) ? "" : r.GetString(4),
                Amount = r.IsDBNull(5) ? 0 : r.GetDouble(5),
                Probability = r.IsDBNull(6) ? 0 : r.GetDouble(6),
                Description = r.IsDBNull(7) ? "" : r.GetString(7),
                Status = r.IsDBNull(8) ? "" : r.GetString(8),
                PaymentDelay = r.IsDBNull(9) ? 30 : r.GetInt32(9),
                PdfPath = r.IsDBNull(10) ? "" : r.GetString(10),
                CreatedAt = r.IsDBNull(11) ? "" : r.GetString(11)
            });
        }
        return list;
    }

    public void AddOffer(Offer o) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO offers(offer_number,offer_date,date_expected,customer,amount,probability,description,status,payment_delay,pdf_path,created_at)
            VALUES(@onum,@odate,@dexp,@cust,@amt,@prob,@desc,@status,@delay,@pdf,CURRENT_TIMESTAMP)";
        cmd.Parameters.AddWithValue("@onum", (object?)o.OfferNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@odate", (object?)o.OfferDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@dexp", (object?)o.DateExpected ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cust", (object?)o.Customer ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@amt", o.Amount);
        cmd.Parameters.AddWithValue("@prob", o.Probability);
        cmd.Parameters.AddWithValue("@desc", (object?)o.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@status", (object?)o.Status ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@delay", o.PaymentDelay);
        cmd.Parameters.AddWithValue("@pdf", (object?)o.PdfPath ?? DBNull.Value);
        cmd.ExecuteNonQuery();
        o.Id = LastInsertId();
    }

    public void UpdateOffer(Offer o) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"UPDATE offers SET offer_number=@onum,offer_date=@odate,date_expected=@dexp,customer=@cust,
            amount=@amt,probability=@prob,description=@desc,status=@status,payment_delay=@delay,pdf_path=@pdf WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", o.Id);
        cmd.Parameters.AddWithValue("@onum", (object?)o.OfferNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@odate", (object?)o.OfferDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@dexp", (object?)o.DateExpected ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cust", (object?)o.Customer ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@amt", o.Amount);
        cmd.Parameters.AddWithValue("@prob", o.Probability);
        cmd.Parameters.AddWithValue("@desc", (object?)o.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@status", (object?)o.Status ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@delay", o.PaymentDelay);
        cmd.Parameters.AddWithValue("@pdf", (object?)o.PdfPath ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void DeleteOffer(long id) { ExecWithId("DELETE FROM offers WHERE id=@id", id); }

    // CRUD: Targets
    public List<Target> GetTargets_List() {
        var list = new List<Target>();
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT id,year,month,amount FROM targets";
        using var r = cmd.ExecuteReader();
        while (r.Read()) {
            list.Add(new Target {
                Id = r.GetInt64(0), Year = r.GetInt32(1), Month = r.GetInt32(2), Amount = r.GetDouble(3)
            });
        }
        return list;
    }

    public void AddTarget(Target t) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "INSERT INTO targets(year,month,amount) VALUES(@y,@m,@a)";
        cmd.Parameters.AddWithValue("@y", t.Year);
        cmd.Parameters.AddWithValue("@m", t.Month);
        cmd.Parameters.AddWithValue("@a", t.Amount);
        cmd.ExecuteNonQuery();
        t.Id = LastInsertId();
    }

    public void UpdateTarget(Target t) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "UPDATE targets SET year=@y,month=@m,amount=@a WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", t.Id);
        cmd.Parameters.AddWithValue("@y", t.Year);
        cmd.Parameters.AddWithValue("@m", t.Month);
        cmd.Parameters.AddWithValue("@a", t.Amount);
        cmd.ExecuteNonQuery();
    }

    public void DeleteTarget(long id) { ExecWithId("DELETE FROM targets WHERE id=@id", id); }

    // CRUD: Resources
    public List<Resource> GetResources() {
        var list = new List<Resource>();
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT id,name,role,availability,hourly_rate,created_at FROM resources";
        using var r = cmd.ExecuteReader();
        while (r.Read()) {
            list.Add(new Resource {
                Id = r.GetInt64(0), Name = r.GetString(1),
                Role = r.IsDBNull(2) ? "" : r.GetString(2),
                Availability = r.IsDBNull(3) ? 1.0 : r.GetDouble(3),
                HourlyRate = r.IsDBNull(4) ? 0 : r.GetDouble(4),
                CreatedAt = r.IsDBNull(5) ? "" : r.GetString(5)
            });
        }
        return list;
    }

    public void AddResource(Resource res) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "INSERT INTO resources(name,role,availability,hourly_rate,created_at) VALUES(@n,@r,@a,@hr,CURRENT_TIMESTAMP)";
        cmd.Parameters.AddWithValue("@n", res.Name);
        cmd.Parameters.AddWithValue("@r", (object?)res.Role ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@a", res.Availability);
        cmd.Parameters.AddWithValue("@hr", res.HourlyRate);
        cmd.ExecuteNonQuery();
        res.Id = LastInsertId();
    }

    public void UpdateResource(Resource res) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "UPDATE resources SET name=@n,role=@r,availability=@a,hourly_rate=@hr WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", res.Id);
        cmd.Parameters.AddWithValue("@n", res.Name);
        cmd.Parameters.AddWithValue("@r", (object?)res.Role ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@a", res.Availability);
        cmd.Parameters.AddWithValue("@hr", res.HourlyRate);
        cmd.ExecuteNonQuery();
    }

    public void DeleteResource(long id) { ExecWithId("DELETE FROM resources WHERE id=@id", id); }

    // CRUD: Projects
    public List<Project> GetProjects() {
        var list = new List<Project>();
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT id,project_number,name,color,start_date,end_date,budget,status,created_at FROM projects";
        using var r = cmd.ExecuteReader();
        while (r.Read()) {
            list.Add(new Project {
                Id = r.GetInt64(0),
                ProjectNumber = r.IsDBNull(1) ? "" : r.GetString(1),
                Name = r.GetString(2),
                Color = r.IsDBNull(3) ? "#3498db" : r.GetString(3),
                StartDate = r.IsDBNull(4) ? "" : r.GetString(4),
                EndDate = r.IsDBNull(5) ? "" : r.GetString(5),
                Budget = r.IsDBNull(6) ? 0 : r.GetDouble(6),
                Status = r.IsDBNull(7) ? "active" : r.GetString(7),
                CreatedAt = r.IsDBNull(8) ? "" : r.GetString(8)
            });
        }
        return list;
    }

    public void AddProject(Project p) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO projects(project_number,name,color,start_date,end_date,budget,status,created_at)
            VALUES(@pn,@n,@c,@sd,@ed,@b,@s,CURRENT_TIMESTAMP)";
        cmd.Parameters.AddWithValue("@pn", (object?)p.ProjectNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@n", p.Name);
        cmd.Parameters.AddWithValue("@c", (object?)p.Color ?? "#3498db");
        cmd.Parameters.AddWithValue("@sd", (object?)p.StartDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ed", (object?)p.EndDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@b", p.Budget);
        cmd.Parameters.AddWithValue("@s", (object?)p.Status ?? "active");
        cmd.ExecuteNonQuery();
        p.Id = LastInsertId();
    }

    public void UpdateProject(Project p) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"UPDATE projects SET project_number=@pn,name=@n,color=@c,start_date=@sd,
            end_date=@ed,budget=@b,status=@s WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", p.Id);
        cmd.Parameters.AddWithValue("@pn", (object?)p.ProjectNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@n", p.Name);
        cmd.Parameters.AddWithValue("@c", (object?)p.Color ?? "#3498db");
        cmd.Parameters.AddWithValue("@sd", (object?)p.StartDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ed", (object?)p.EndDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@b", p.Budget);
        cmd.Parameters.AddWithValue("@s", (object?)p.Status ?? "active");
        cmd.ExecuteNonQuery();
    }

    public void DeleteProject(long id) { ExecWithId("DELETE FROM projects WHERE id=@id", id); }

    // CRUD: ResourceAllocations
    public List<ResourceAllocation> GetAllocations(DateTime start, DateTime end) {
        var list = new List<ResourceAllocation>();
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT id,resource_id,project_id,date,hours,notes,created_at FROM resource_allocations WHERE date>=@s AND date<=@e";
        cmd.Parameters.AddWithValue("@s", start.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@e", end.ToString("yyyy-MM-dd"));
        using var r = cmd.ExecuteReader();
        while (r.Read()) {
            list.Add(new ResourceAllocation {
                Id = r.GetInt64(0), ResourceId = r.GetInt64(1), ProjectId = r.GetInt64(2),
                Date = r.GetString(3), Hours = r.IsDBNull(4) ? 8.0 : r.GetDouble(4),
                Notes = r.IsDBNull(5) ? "" : r.GetString(5),
                CreatedAt = r.IsDBNull(6) ? "" : r.GetString(6)
            });
        }
        return list;
    }

    public void AddAllocation(ResourceAllocation a) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO resource_allocations(resource_id,project_id,date,hours,notes,created_at)
            VALUES(@rid,@pid,@d,@h,@n,CURRENT_TIMESTAMP)";
        cmd.Parameters.AddWithValue("@rid", a.ResourceId);
        cmd.Parameters.AddWithValue("@pid", a.ProjectId);
        cmd.Parameters.AddWithValue("@d", a.Date);
        cmd.Parameters.AddWithValue("@h", a.Hours);
        cmd.Parameters.AddWithValue("@n", (object?)a.Notes ?? DBNull.Value);
        cmd.ExecuteNonQuery();
        a.Id = LastInsertId();
    }

    public void DeleteAllocation(long id) { ExecWithId("DELETE FROM resource_allocations WHERE id=@id", id); }

    // Categories
    public List<string> GetCategories() {
        var list = new List<string>();
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM categories ORDER BY name";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    // Users
    public bool ValidateUser(string username, string password) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM users WHERE username=@u AND password_hash=@p";
        cmd.Parameters.AddWithValue("@u", username);
        cmd.Parameters.AddWithValue("@p", password);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    public List<string> GetUsernames() {
        var list = new List<string>();
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT username FROM users ORDER BY username";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    // Helpers
    long LastInsertId() {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT last_insert_rowid()";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    void ExecWithId(string sql, long id) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }
}
