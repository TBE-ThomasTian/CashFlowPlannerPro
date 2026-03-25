using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using CashFlowPlannerPro.Models;

namespace CashFlowPlannerPro.Data;

public sealed class Database : IDisposable {
    static readonly Lazy<Database> _instance = new(() => new Database());
    public static Database Instance => _instance.Value;
    DbConnection? _conn;
    IDbDialect _dialect = new SqliteDialect();
    Database() { }

    public IDbDialect Dialect => _dialect;

    public void Open(ConnectionConfig config) {
        Close();
        _dialect = config.Backend switch {
            DatabaseBackend.MariaDB => new MariaDbDialect(),
            _ => new SqliteDialect()
        };

        // MariaDB: auto-create database if it doesn't exist
        if (config.Backend == DatabaseBackend.MariaDB && !string.IsNullOrEmpty(config.DatabaseName))
        {
            var bootstrapStr = $"Server={config.Host};Port={config.Port};User={config.DbUsername};Password={config.DbPassword};CharSet=utf8mb4";
            using var bootstrapConn = _dialect.CreateConnection(bootstrapStr);
            bootstrapConn.Open();
            using var cmd = bootstrapConn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE IF NOT EXISTS `{config.DatabaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci";
            cmd.ExecuteNonQuery();
        }

        var connStr = config.ToConnectionString();
        _conn = _dialect.CreateConnection(connStr);
        _conn.Open();
        _dialect.ConfigureConnection(_conn);
    }

    public void Open(string path) => Open(new ConnectionConfig {
        Backend = DatabaseBackend.SQLite,
        FilePath = path
    });

    public void Close() {
        if (_conn != null) { _conn.Close(); _conn.Dispose(); _conn = null; }
    }

    public void Dispose() => Close();

    public bool IsFirstRun { get; private set; }
    DbConnection Conn => _conn ?? throw new InvalidOperationException("Database not open");
    public DbConnection GetConnection() => Conn;

    private void TryMigrate(string sql) {
        try { Exec(_dialect.RewriteDdl(sql)); }
        catch (Exception ex) when (_dialect.IsMigrationError(ex)) { }
        catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"[DB Migration Error] {sql}: {ex.Message}");
            throw;
        }
    }

    void Exec(string sql) {
        try {
            using var cmd = Conn.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery();
        } catch (Exception ex) {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "db_debug.log"),
                $"\n--- FAIL ---\nSQL: {sql}\nERR: {ex.Message}\n");
            throw;
        }
    }
    void ExecDdl(string sql) {
        var rewritten = _dialect.RewriteDdl(sql);
        System.IO.File.AppendAllText(
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "db_debug.log"),
            $"\n--- DDL ---\n{rewritten}\n");
        Exec(rewritten);
    }

    static readonly string[] MonthNames = ["Jan", "Feb", "Mär", "Apr", "Mai", "Jun", "Jul", "Aug", "Sep", "Okt", "Nov", "Dez"];
    static string MonthLabel(int y, int m) => $"{MonthNames[m - 1]} {y % 100:D2}";

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
        ExecDdl(@"CREATE TABLE IF NOT EXISTS users(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            username TEXT UNIQUE NOT NULL,
            password_hash TEXT NOT NULL,
            full_name TEXT,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP)");
        using (var cmd = Conn.CreateCommand()) {
            cmd.CommandText = "SELECT COUNT(*) FROM users";
            if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            {
                // First-time setup: create admin with hashed default password
                // The login dialog will detect legacy format and prompt for change
                var hashedPw = Services.PasswordHasher.Hash("admin");
                using var ins = Conn.CreateCommand();
                ins.CommandText = "INSERT INTO users (username, password_hash, full_name) VALUES ('admin', @p, 'Administrator')";
                ins.Parameters.AddWithValue("@p", hashedPw);
                ins.ExecuteNonQuery();
                IsFirstRun = true;
            }
        }
        ExecDdl("CREATE TABLE IF NOT EXISTS categories(id INTEGER PRIMARY KEY AUTOINCREMENT,name TEXT UNIQUE NOT NULL)");
        ExecDdl("CREATE TABLE IF NOT EXISTS persons(id INTEGER PRIMARY KEY AUTOINCREMENT,name TEXT UNIQUE NOT NULL)");
        ExecDdl(@"CREATE TABLE IF NOT EXISTS transactions(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            date TEXT NOT NULL, description TEXT, amount REAL NOT NULL,
            category_id INTEGER, person_id INTEGER, interval TEXT, notes TEXT,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP, updated_at TEXT,
            FOREIGN KEY(category_id) REFERENCES categories(id),
            FOREIGN KEY(person_id) REFERENCES persons(id))");
        ExecDdl(@"CREATE TABLE IF NOT EXISTS offers(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            offer_number TEXT, offer_date TEXT, date_expected TEXT,
            customer TEXT, amount REAL, probability REAL, description TEXT, status TEXT,
            payment_delay INTEGER DEFAULT 30, pdf_path TEXT,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP)");
        ExecDdl(@"CREATE TABLE IF NOT EXISTS invoices(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            issue_date TEXT, due_date TEXT, customer TEXT, amount REAL,
            description TEXT, paid_date TEXT, paid_amount REAL, status TEXT,
            pdf_path TEXT, created_at TEXT DEFAULT CURRENT_TIMESTAMP)");
        ExecDdl(@"CREATE TABLE IF NOT EXISTS targets(
            id INTEGER PRIMARY KEY AUTOINCREMENT, year INTEGER, month INTEGER, amount REAL)");
        ExecDdl("CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT)");
        ExecDdl(@"CREATE TABLE IF NOT EXISTS resources(
            id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, role TEXT,
            availability REAL DEFAULT 1.0, hourly_rate REAL DEFAULT 0,
            work_start_hour INTEGER DEFAULT 8, work_end_hour INTEGER DEFAULT 17,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP)");
        TryMigrate("ALTER TABLE resources ADD COLUMN work_start_hour INTEGER DEFAULT 8");
        TryMigrate("ALTER TABLE resources ADD COLUMN work_end_hour INTEGER DEFAULT 17");
        ExecDdl(@"CREATE TABLE IF NOT EXISTS projects(
            id INTEGER PRIMARY KEY AUTOINCREMENT, project_number TEXT, name TEXT NOT NULL,
            client TEXT DEFAULT '', color TEXT DEFAULT '#3498db', start_date TEXT, end_date TEXT,
            budget REAL DEFAULT 0, status TEXT DEFAULT 'active',
            created_at TEXT DEFAULT CURRENT_TIMESTAMP)");
        TryMigrate("ALTER TABLE projects ADD COLUMN client TEXT DEFAULT ''");
        ExecDdl(@"CREATE TABLE IF NOT EXISTS resource_allocations(
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
        ExecDdl(@"CREATE TABLE IF NOT EXISTS hardware_resources(
            id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, type TEXT,
            cost_per_hour REAL DEFAULT 0, color TEXT DEFAULT '#17a2b8', notes TEXT,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP)");
        ExecDdl(@"CREATE TABLE IF NOT EXISTS hardware_allocations(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            resource_id INTEGER NOT NULL, hardware_id INTEGER NOT NULL, project_id INTEGER NOT NULL,
            date TEXT NOT NULL, hours REAL DEFAULT 8.0, notes TEXT,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY(resource_id) REFERENCES resources(id) ON DELETE CASCADE,
            FOREIGN KEY(hardware_id) REFERENCES hardware_resources(id) ON DELETE CASCADE,
            FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE,
            UNIQUE(resource_id, hardware_id, project_id, date))");
        Exec("CREATE INDEX IF NOT EXISTS idx_hw_alloc_resource ON hardware_allocations(resource_id)");
        Exec("CREATE INDEX IF NOT EXISTS idx_hw_alloc_hardware ON hardware_allocations(hardware_id)");
        Exec("CREATE INDEX IF NOT EXISTS idx_hw_alloc_date ON hardware_allocations(date)");
        ExecDdl(@"CREATE TABLE IF NOT EXISTS project_milestones(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            project_id INTEGER NOT NULL, name TEXT NOT NULL,
            status TEXT DEFAULT 'Offen', deadline TEXT, responsible TEXT,
            hours_budget REAL DEFAULT 0, priority INTEGER DEFAULT 2,
            dependencies TEXT, notes TEXT, sort_order INTEGER DEFAULT 0,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE)");
        Exec("CREATE INDEX IF NOT EXISTS idx_milestones_project ON project_milestones(project_id)");

        // Roles & permissions
        ExecDdl(@"CREATE TABLE IF NOT EXISTS roles(
            id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT UNIQUE NOT NULL,
            description TEXT DEFAULT '')");
        ExecDdl(@"CREATE TABLE IF NOT EXISTS role_permissions(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            role_id INTEGER NOT NULL, page_key TEXT NOT NULL,
            access_level TEXT DEFAULT 'none',
            FOREIGN KEY(role_id) REFERENCES roles(id) ON DELETE CASCADE,
            UNIQUE(role_id, page_key))");
        TryMigrate("ALTER TABLE users ADD COLUMN role_id INTEGER REFERENCES roles(id)");
        TryMigrate("ALTER TABLE users ADD COLUMN avatar_data TEXT");
        TryMigrate("ALTER TABLE resources ADD COLUMN avatar_data TEXT");
        EnsureDefaultRoles();

        // User ToDos
        ExecDdl(@"CREATE TABLE IF NOT EXISTS user_todos(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id INTEGER NOT NULL, title TEXT NOT NULL,
            description TEXT, status TEXT DEFAULT 'Offen',
            priority INTEGER DEFAULT 2, due_date TEXT,
            project_id INTEGER, milestone_id INTEGER,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY(user_id) REFERENCES users(id) ON DELETE CASCADE,
            FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE SET NULL,
            FOREIGN KEY(milestone_id) REFERENCES project_milestones(id) ON DELETE SET NULL)");
        Exec("CREATE INDEX IF NOT EXISTS idx_todos_user ON user_todos(user_id)");

        ExecDdl(@"CREATE TABLE IF NOT EXISTS user_settings(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id INTEGER NOT NULL,
            `key` TEXT NOT NULL,
            value TEXT,
            FOREIGN KEY(user_id) REFERENCES users(id) ON DELETE CASCADE,
            UNIQUE(user_id, `key`))");

        ExecDdl(@"CREATE TABLE IF NOT EXISTS time_entries(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id INTEGER NOT NULL,
            project_id INTEGER NOT NULL,
            activity_type TEXT NOT NULL,
            description TEXT,
            entry_date TEXT NOT NULL,
            start_time TEXT,
            end_time TEXT,
            duration_hours REAL DEFAULT 0,
            is_running INTEGER DEFAULT 0,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY(user_id) REFERENCES users(id) ON DELETE CASCADE,
            FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE)");
        Exec("CREATE INDEX IF NOT EXISTS idx_time_entries_user ON time_entries(user_id)");
        Exec("CREATE INDEX IF NOT EXISTS idx_time_entries_project ON time_entries(project_id)");
        Exec("CREATE INDEX IF NOT EXISTS idx_time_entries_running ON time_entries(user_id,is_running)");

        // Customers / Adressbuch
        ExecDdl(@"CREATE TABLE IF NOT EXISTS customers(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            company TEXT, contact_name TEXT, email TEXT, phone TEXT,
            street TEXT, zip_code TEXT, city TEXT, country TEXT DEFAULT 'Deutschland',
            tax_id TEXT, status TEXT DEFAULT 'Aktiv', notes TEXT,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP)");
        TryMigrate("ALTER TABLE invoices ADD COLUMN customer_id INTEGER REFERENCES customers(id)");
        TryMigrate("ALTER TABLE offers ADD COLUMN customer_id INTEGER REFERENCES customers(id)");

        // Migration: ensure "kunden" permission exists for all roles that have "invoices" access
        try {
            using var kcmd = Conn.CreateCommand();
            kcmd.CommandText = _dialect.InsertOrIgnore(@"INSERT OR IGNORE INTO role_permissions(role_id,page_key,access_level)
                SELECT role_id,'kunden',access_level FROM role_permissions WHERE page_key='invoices'");
            kcmd.ExecuteNonQuery();
        } catch { }

        var cats = new[] { "Lohn","Kapitalsteuer","Sozialversicherung","Lohnsteuer","Umsatzsteuer","Versicherung","Miete","Strom","Steuerberatung" };
        foreach (var c in cats) {
            using var cmd = Conn.CreateCommand();
            cmd.CommandText = _dialect.InsertOrIgnore("INSERT OR IGNORE INTO categories(name) VALUES(@n)");
            cmd.Parameters.AddWithValue("@n", c);
            cmd.ExecuteNonQuery();
        }
    }

    // Settings
    public double GetSettingStartBalance() {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE `key`='start_balance'";
        var r = cmd.ExecuteScalar();
        return r != null ? Convert.ToDouble(r, CultureInfo.InvariantCulture) : 0.0;
    }

    public void SetSettingStartBalance(double v) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = _dialect.UpsertSettings("@v");
        cmd.Parameters.AddWithValue("@v", v.ToString(CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    // Avatar methods
    public void SaveUserAvatar(string username, string? base64Data) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "UPDATE users SET avatar_data=@d WHERE username=@u";
        cmd.Parameters.AddWithValue("@d", (object?)base64Data ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@u", username);
        cmd.ExecuteNonQuery();
    }

    public string? GetUserAvatar(string username) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT avatar_data FROM users WHERE username=@u";
        cmd.Parameters.AddWithValue("@u", username);
        var r = cmd.ExecuteScalar();
        return r != null && r != DBNull.Value ? r.ToString() : null;
    }

    public void SaveResourceAvatar(long resourceId, string? base64Data) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "UPDATE resources SET avatar_data=@d WHERE id=@id";
        cmd.Parameters.AddWithValue("@d", (object?)base64Data ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", resourceId);
        cmd.ExecuteNonQuery();
    }

    public string? GetResourceAvatar(long resourceId) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT avatar_data FROM resources WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", resourceId);
        var r = cmd.ExecuteScalar();
        return r != null && r != DBNull.Value ? r.ToString() : null;
    }

    // Generic user settings (key-value per user)
    public string? GetUserSetting(long userId, string settingKey) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM user_settings WHERE user_id=@uid AND `key`=@k";
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@k", settingKey);
        var r = cmd.ExecuteScalar();
        return r != null && r != DBNull.Value ? r.ToString() : null;
    }

    public void SaveUserSetting(long userId, string settingKey, string? value) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = _dialect is MariaDbDialect
            ? "INSERT INTO user_settings(user_id,`key`,value) VALUES(@uid,@k,@v) ON DUPLICATE KEY UPDATE value=@v"
            : "INSERT INTO user_settings(user_id,`key`,value) VALUES(@uid,@k,@v) ON CONFLICT(user_id,`key`) DO UPDATE SET value=@v";
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@k", settingKey);
        cmd.Parameters.AddWithValue("@v", (object?)value ?? DBNull.Value);
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

    // Monthly Cashflow Forecast
    // NOTE: Invoices and Transactions are separate data sources. To avoid double-counting,
    // users should NOT create a transaction for an invoice payment AND mark the invoice as paid.
    // The forecast uses: Transactions (actual + recurring) + unpaid Invoices + weighted Offers.
    public List<MonthRow> MonthlyCashflow(int horizonMonths, bool includeOffersOffen, bool includeOffersBeauftragt, bool includeUnpaidInvoices, bool includeRecurring) {
        var evs = new List<(DateTime d, double a)>();
        // Transactions
        using (var cmd = Conn.CreateCommand()) {
            cmd.CommandText = "SELECT date,amount,COALESCE(`interval`,''),notes FROM transactions";
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
                    evs.Add((payDate, amt * p)); // Weighted by probability
                }
            }
        }
        // Sort and bucket — use ordered list (not SortedDictionary which sorts alphabetically!)
        evs.Sort((a, b) => a.d.CompareTo(b.d));
        var monthOrder = new List<string>();
        var netMap = new Dictionary<string, double>();
        var incMap = new Dictionary<string, double>();
        var expMap = new Dictionary<string, double>();
        var today = DateTime.Today;
        var startOfMonth = new DateTime(today.Year, today.Month, 1);
        for (int i = 0; i < horizonMonths; i++) {
            var md = AddMonthsClamped(startOfMonth, i);
            var label = MonthLabel(md.Year, md.Month);
            if (!netMap.ContainsKey(label)) {
                monthOrder.Add(label);
                netMap[label] = 0; incMap[label] = 0; expMap[label] = 0;
            }
        }
        foreach (var e in evs) {
            if (e.d < startOfMonth) continue;
            var label = MonthLabel(e.d.Year, e.d.Month);
            if (!netMap.ContainsKey(label)) continue;
            netMap[label] += e.a;
            if (e.a > 0) incMap[label] += e.a; else expMap[label] += e.a;
        }
        return monthOrder.Select(m => new MonthRow {
            Month = m, Net = netMap[m], Income = incMap[m], Expenses = expMap[m]
        }).ToList();
    }

    /// <summary>
    /// Returns actual balance = startBalance + all transactions up to today.
    /// Only uses transactions (not invoices/offers) to avoid double-counting.
    /// Invoices are tracked separately for forecasting; the actual payment
    /// should be recorded as a transaction when it arrives.
    /// </summary>
    public double GetActualCashflowToDate(double startBalance) {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        double sum = startBalance;
        // Actual transactions up to today (one-time only, no recurring projections)
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(SUM(amount),0) FROM transactions WHERE date <= @d";
        cmd.Parameters.AddWithValue("@d", today);
        sum += Convert.ToDouble(cmd.ExecuteScalar());
        return sum;
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
            if (p > 0) sum += amt * p; // Weighted by probability
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
        cmd.CommandText = "SELECT id,date,description,amount,category_id,person_id,`interval`,notes,created_at,updated_at FROM transactions";
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
        cmd.CommandText = @"INSERT INTO transactions(date,description,amount,category_id,person_id,`interval`,notes,created_at,updated_at)
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
            category_id=@cat,person_id=@per,`interval`=@intv,notes=@notes,updated_at=CURRENT_TIMESTAMP WHERE id=@id";
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
        cmd.CommandText = "SELECT id,name,role,availability,hourly_rate,work_start_hour,work_end_hour,created_at FROM resources";
        using var r = cmd.ExecuteReader();
        while (r.Read()) {
            list.Add(new Resource {
                Id = r.GetInt64(0), Name = r.GetString(1),
                Role = r.IsDBNull(2) ? "" : r.GetString(2),
                Availability = r.IsDBNull(3) ? 1.0 : r.GetDouble(3),
                HourlyRate = r.IsDBNull(4) ? 0 : r.GetDouble(4),
                WorkStartHour = r.IsDBNull(5) ? 8 : r.GetInt32(5),
                WorkEndHour = r.IsDBNull(6) ? 17 : r.GetInt32(6),
                CreatedAt = r.IsDBNull(7) ? "" : r.GetString(7)
            });
        }
        return list;
    }

    public void AddResource(Resource res) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "INSERT INTO resources(name,role,availability,hourly_rate,work_start_hour,work_end_hour,created_at) VALUES(@n,@r,@a,@hr,@ws,@we,CURRENT_TIMESTAMP)";
        cmd.Parameters.AddWithValue("@n", res.Name);
        cmd.Parameters.AddWithValue("@r", (object?)res.Role ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@a", res.Availability);
        cmd.Parameters.AddWithValue("@hr", res.HourlyRate);
        cmd.Parameters.AddWithValue("@ws", res.WorkStartHour);
        cmd.Parameters.AddWithValue("@we", res.WorkEndHour);
        cmd.ExecuteNonQuery();
        res.Id = LastInsertId();
    }

    public void UpdateResource(Resource res) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "UPDATE resources SET name=@n,role=@r,availability=@a,hourly_rate=@hr,work_start_hour=@ws,work_end_hour=@we WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", res.Id);
        cmd.Parameters.AddWithValue("@n", res.Name);
        cmd.Parameters.AddWithValue("@r", (object?)res.Role ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@a", res.Availability);
        cmd.Parameters.AddWithValue("@hr", res.HourlyRate);
        cmd.Parameters.AddWithValue("@ws", res.WorkStartHour);
        cmd.Parameters.AddWithValue("@we", res.WorkEndHour);
        cmd.ExecuteNonQuery();
    }

    public void DeleteResource(long id) { ExecWithId("DELETE FROM resources WHERE id=@id", id); }

    // CRUD: Projects
    public List<Project> GetProjects() {
        var list = new List<Project>();
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT id,project_number,name,client,color,start_date,end_date,budget,status,created_at FROM projects";
        using var r = cmd.ExecuteReader();
        while (r.Read()) {
            list.Add(new Project {
                Id = r.GetInt64(0),
                ProjectNumber = r.IsDBNull(1) ? "" : r.GetString(1),
                Name = r.GetString(2),
                Client = r.IsDBNull(3) ? "" : r.GetString(3),
                Color = r.IsDBNull(4) ? "#3498db" : r.GetString(4),
                StartDate = r.IsDBNull(5) ? "" : r.GetString(5),
                EndDate = r.IsDBNull(6) ? "" : r.GetString(6),
                Budget = r.IsDBNull(7) ? 0 : r.GetDouble(7),
                Status = r.IsDBNull(8) ? "active" : r.GetString(8),
                CreatedAt = r.IsDBNull(9) ? "" : r.GetString(9)
            });
        }
        return list;
    }

    public void AddProject(Project p) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO projects(project_number,name,client,color,start_date,end_date,budget,status,created_at)
            VALUES(@pn,@n,@cl,@c,@sd,@ed,@b,@s,CURRENT_TIMESTAMP)";
        cmd.Parameters.AddWithValue("@pn", (object?)p.ProjectNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@n", p.Name);
        cmd.Parameters.AddWithValue("@cl", (object?)p.Client ?? "");
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
        cmd.CommandText = @"UPDATE projects SET project_number=@pn,name=@n,client=@cl,color=@c,start_date=@sd,
            end_date=@ed,budget=@b,status=@s WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", p.Id);
        cmd.Parameters.AddWithValue("@pn", (object?)p.ProjectNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@n", p.Name);
        cmd.Parameters.AddWithValue("@cl", (object?)p.Client ?? "");
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

    // CRUD: HardwareResources
    public List<HardwareResource> GetHardwareResources() {
        var list = new List<HardwareResource>();
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT id,name,type,cost_per_hour,color,notes,created_at FROM hardware_resources ORDER BY name";
        using var r = cmd.ExecuteReader();
        while (r.Read()) {
            list.Add(new HardwareResource {
                Id = r.GetInt64(0), Name = r.GetString(1),
                Type = r.IsDBNull(2) ? "" : r.GetString(2),
                CostPerHour = r.IsDBNull(3) ? 0 : r.GetDouble(3),
                Color = r.IsDBNull(4) ? "#17a2b8" : r.GetString(4),
                Notes = r.IsDBNull(5) ? "" : r.GetString(5),
                CreatedAt = r.IsDBNull(6) ? "" : r.GetString(6)
            });
        }
        return list;
    }

    public void AddHardwareResource(HardwareResource h) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO hardware_resources(name,type,cost_per_hour,color,notes,created_at)
            VALUES(@n,@t,@c,@col,@no,CURRENT_TIMESTAMP)";
        cmd.Parameters.AddWithValue("@n", h.Name);
        cmd.Parameters.AddWithValue("@t", (object?)h.Type ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@c", h.CostPerHour);
        cmd.Parameters.AddWithValue("@col", (object?)h.Color ?? "#17a2b8");
        cmd.Parameters.AddWithValue("@no", (object?)h.Notes ?? DBNull.Value);
        cmd.ExecuteNonQuery();
        h.Id = LastInsertId();
    }

    public void UpdateHardwareResource(HardwareResource h) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "UPDATE hardware_resources SET name=@n,type=@t,cost_per_hour=@c,color=@col,notes=@no WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", h.Id);
        cmd.Parameters.AddWithValue("@n", h.Name);
        cmd.Parameters.AddWithValue("@t", (object?)h.Type ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@c", h.CostPerHour);
        cmd.Parameters.AddWithValue("@col", (object?)h.Color ?? "#17a2b8");
        cmd.Parameters.AddWithValue("@no", (object?)h.Notes ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void DeleteHardwareResource(long id) { ExecWithId("DELETE FROM hardware_resources WHERE id=@id", id); }

    // CRUD: HardwareAllocations
    public List<HardwareAllocation> GetHardwareAllocations(DateTime start, DateTime end) {
        var list = new List<HardwareAllocation>();
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"SELECT ha.id,ha.resource_id,ha.hardware_id,ha.project_id,ha.date,ha.hours,ha.notes,
            hr.name,hr.color,p.name
            FROM hardware_allocations ha
            JOIN hardware_resources hr ON hr.id=ha.hardware_id
            JOIN projects p ON p.id=ha.project_id
            WHERE ha.date>=@s AND ha.date<=@e";
        cmd.Parameters.AddWithValue("@s", start.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@e", end.ToString("yyyy-MM-dd"));
        using var r = cmd.ExecuteReader();
        while (r.Read()) {
            list.Add(new HardwareAllocation {
                Id = r.GetInt64(0), ResourceId = r.GetInt64(1), HardwareId = r.GetInt64(2),
                ProjectId = r.GetInt64(3), Date = r.GetString(4),
                Hours = r.IsDBNull(5) ? 8.0 : r.GetDouble(5),
                Notes = r.IsDBNull(6) ? "" : r.GetString(6),
                HardwareName = r.GetString(7), HardwareColor = r.GetString(8),
                ProjectName = r.GetString(9)
            });
        }
        return list;
    }

    public void AddHardwareAllocation(HardwareAllocation a) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = _dialect.InsertOrIgnore(@"INSERT OR IGNORE INTO hardware_allocations(resource_id,hardware_id,project_id,date,hours,notes,created_at)
            VALUES(@rid,@hid,@pid,@d,@h,@n,CURRENT_TIMESTAMP)");
        cmd.Parameters.AddWithValue("@rid", a.ResourceId);
        cmd.Parameters.AddWithValue("@hid", a.HardwareId);
        cmd.Parameters.AddWithValue("@pid", a.ProjectId);
        cmd.Parameters.AddWithValue("@d", a.Date);
        cmd.Parameters.AddWithValue("@h", a.Hours);
        cmd.Parameters.AddWithValue("@n", (object?)a.Notes ?? DBNull.Value);
        cmd.ExecuteNonQuery();
        a.Id = LastInsertId();
    }

    public void DeleteHardwareAllocation(long id) { ExecWithId("DELETE FROM hardware_allocations WHERE id=@id", id); }

    // CRUD: ProjectMilestones
    public List<ProjectMilestone> GetMilestones(long projectId) {
        var list = new List<ProjectMilestone>();
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT id,project_id,name,status,deadline,responsible,hours_budget,priority,dependencies,notes,sort_order,created_at FROM project_milestones WHERE project_id=@pid ORDER BY sort_order,id";
        cmd.Parameters.AddWithValue("@pid", projectId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) {
            list.Add(new ProjectMilestone {
                Id = r.GetInt64(0), ProjectId = r.GetInt64(1), Name = r.GetString(2),
                Status = r.IsDBNull(3) ? "Offen" : r.GetString(3),
                Deadline = r.IsDBNull(4) ? null : r.GetString(4),
                Responsible = r.IsDBNull(5) ? null : r.GetString(5),
                HoursBudget = r.IsDBNull(6) ? 0 : r.GetDouble(6),
                Priority = r.IsDBNull(7) ? 2 : r.GetInt32(7),
                Dependencies = r.IsDBNull(8) ? null : r.GetString(8),
                Notes = r.IsDBNull(9) ? null : r.GetString(9),
                SortOrder = r.IsDBNull(10) ? 0 : r.GetInt32(10),
                CreatedAt = r.IsDBNull(11) ? null : r.GetString(11)
            });
        }
        return list;
    }

    public void AddMilestone(ProjectMilestone m) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO project_milestones(project_id,name,status,deadline,responsible,hours_budget,priority,dependencies,notes,sort_order,created_at)
            VALUES(@pid,@n,@s,@dl,@r,@hb,@p,@dep,@no,@so,CURRENT_TIMESTAMP)";
        cmd.Parameters.AddWithValue("@pid", m.ProjectId);
        cmd.Parameters.AddWithValue("@n", m.Name);
        cmd.Parameters.AddWithValue("@s", m.Status);
        cmd.Parameters.AddWithValue("@dl", (object?)m.Deadline ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@r", (object?)m.Responsible ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@hb", m.HoursBudget);
        cmd.Parameters.AddWithValue("@p", m.Priority);
        cmd.Parameters.AddWithValue("@dep", (object?)m.Dependencies ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@no", (object?)m.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@so", m.SortOrder);
        cmd.ExecuteNonQuery();
        m.Id = LastInsertId();
    }

    public void UpdateMilestone(ProjectMilestone m) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"UPDATE project_milestones SET name=@n,status=@s,deadline=@dl,responsible=@r,
            hours_budget=@hb,priority=@p,dependencies=@dep,notes=@no,sort_order=@so WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", m.Id);
        cmd.Parameters.AddWithValue("@n", m.Name);
        cmd.Parameters.AddWithValue("@s", m.Status);
        cmd.Parameters.AddWithValue("@dl", (object?)m.Deadline ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@r", (object?)m.Responsible ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@hb", m.HoursBudget);
        cmd.Parameters.AddWithValue("@p", m.Priority);
        cmd.Parameters.AddWithValue("@dep", (object?)m.Dependencies ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@no", (object?)m.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@so", m.SortOrder);
        cmd.ExecuteNonQuery();
    }

    public void DeleteMilestone(long id) { ExecWithId("DELETE FROM project_milestones WHERE id=@id", id); }

    public List<(ProjectMilestone milestone, string projectName, string projectColor)> GetAllMilestones() {
        var list = new List<(ProjectMilestone, string, string)>();
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"SELECT m.id,m.project_id,m.name,m.status,m.deadline,m.responsible,
            m.hours_budget,m.priority,m.dependencies,m.notes,m.sort_order,m.created_at,
            p.name,p.color
            FROM project_milestones m JOIN projects p ON p.id=m.project_id ORDER BY m.sort_order,m.id";
        using var r = cmd.ExecuteReader();
        while (r.Read()) {
            list.Add((new ProjectMilestone {
                Id = r.GetInt64(0), ProjectId = r.GetInt64(1), Name = r.GetString(2),
                Status = r.IsDBNull(3) ? "Offen" : r.GetString(3),
                Deadline = r.IsDBNull(4) ? null : r.GetString(4),
                Responsible = r.IsDBNull(5) ? null : r.GetString(5),
                HoursBudget = r.IsDBNull(6) ? 0 : r.GetDouble(6),
                Priority = r.IsDBNull(7) ? 2 : r.GetInt32(7),
                Dependencies = r.IsDBNull(8) ? null : r.GetString(8),
                Notes = r.IsDBNull(9) ? null : r.GetString(9),
                SortOrder = r.IsDBNull(10) ? 0 : r.GetInt32(10),
                CreatedAt = r.IsDBNull(11) ? null : r.GetString(11)
            }, r.GetString(12), r.IsDBNull(13) ? "#3498db" : r.GetString(13)));
        }
        return list;
    }

    // CRUD: UserTodos
    public List<UserTodo> GetTodos(long userId) {
        var list = new List<UserTodo>();
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT id,user_id,title,description,status,priority,due_date,project_id,milestone_id,created_at FROM user_todos WHERE user_id=@uid ORDER BY priority,due_date,id";
        cmd.Parameters.AddWithValue("@uid", userId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new UserTodo {
            Id = r.GetInt64(0), UserId = r.GetInt64(1), Title = r.GetString(2),
            Description = r.IsDBNull(3) ? null : r.GetString(3),
            Status = r.IsDBNull(4) ? "Offen" : r.GetString(4),
            Priority = r.IsDBNull(5) ? 2 : r.GetInt32(5),
            DueDate = r.IsDBNull(6) ? null : r.GetString(6),
            ProjectId = r.IsDBNull(7) ? null : r.GetInt64(7),
            MilestoneId = r.IsDBNull(8) ? null : r.GetInt64(8),
            CreatedAt = r.IsDBNull(9) ? null : r.GetString(9)
        });
        return list;
    }

    public List<UserTodo> GetAllTodos() {
        var list = new List<UserTodo>();
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT id,user_id,title,description,status,priority,due_date,project_id,milestone_id,created_at FROM user_todos ORDER BY priority,due_date,id";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new UserTodo {
            Id = r.GetInt64(0), UserId = r.GetInt64(1), Title = r.GetString(2),
            Description = r.IsDBNull(3) ? null : r.GetString(3),
            Status = r.IsDBNull(4) ? "Offen" : r.GetString(4),
            Priority = r.IsDBNull(5) ? 2 : r.GetInt32(5),
            DueDate = r.IsDBNull(6) ? null : r.GetString(6),
            ProjectId = r.IsDBNull(7) ? null : r.GetInt64(7),
            MilestoneId = r.IsDBNull(8) ? null : r.GetInt64(8),
            CreatedAt = r.IsDBNull(9) ? null : r.GetString(9)
        });
        return list;
    }

    public void AddTodo(UserTodo t) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO user_todos(user_id,title,description,status,priority,due_date,project_id,milestone_id) VALUES(@uid,@t,@d,@s,@p,@dd,@pid,@mid)";
        cmd.Parameters.AddWithValue("@uid", t.UserId);
        cmd.Parameters.AddWithValue("@t", t.Title);
        cmd.Parameters.AddWithValue("@d", (object?)t.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@s", t.Status);
        cmd.Parameters.AddWithValue("@p", t.Priority);
        cmd.Parameters.AddWithValue("@dd", (object?)t.DueDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pid", (object?)t.ProjectId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@mid", (object?)t.MilestoneId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
        t.Id = LastInsertId();
    }

    public void UpdateTodo(UserTodo t) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"UPDATE user_todos SET title=@t,description=@d,status=@s,priority=@p,due_date=@dd,project_id=@pid,milestone_id=@mid WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", t.Id);
        cmd.Parameters.AddWithValue("@t", t.Title);
        cmd.Parameters.AddWithValue("@d", (object?)t.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@s", t.Status);
        cmd.Parameters.AddWithValue("@p", t.Priority);
        cmd.Parameters.AddWithValue("@dd", (object?)t.DueDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pid", (object?)t.ProjectId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@mid", (object?)t.MilestoneId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void DeleteTodo(long id) { ExecWithId("DELETE FROM user_todos WHERE id=@id", id); }

    // Time Tracking
    public TimeEntry? GetRunningTimeEntry(long userId) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"SELECT te.id,te.user_id,te.project_id,p.name,te.activity_type,te.description,
            te.entry_date,te.start_time,te.end_time,te.duration_hours,te.is_running,te.created_at
            FROM time_entries te
            JOIN projects p ON p.id=te.project_id
            WHERE te.user_id=@uid AND te.is_running=1
            ORDER BY te.id DESC LIMIT 1";
        cmd.Parameters.AddWithValue("@uid", userId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new TimeEntry {
            Id = r.GetInt64(0),
            UserId = r.GetInt64(1),
            ProjectId = r.GetInt64(2),
            ProjectName = r.GetString(3),
            ActivityType = r.GetString(4),
            Description = r.IsDBNull(5) ? "" : r.GetString(5),
            EntryDate = r.GetString(6),
            StartTime = r.IsDBNull(7) ? null : r.GetString(7),
            EndTime = r.IsDBNull(8) ? null : r.GetString(8),
            DurationHours = r.IsDBNull(9) ? 0 : r.GetDouble(9),
            IsRunning = !r.IsDBNull(10) && r.GetInt64(10) == 1,
            CreatedAt = r.IsDBNull(11) ? null : r.GetString(11)
        };
    }

    public List<TimeEntry> GetTimeEntries(long userId, int limit = 50) {
        var list = new List<TimeEntry>();
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"SELECT te.id,te.user_id,te.project_id,p.name,te.activity_type,te.description,
            te.entry_date,te.start_time,te.end_time,te.duration_hours,te.is_running,te.created_at
            FROM time_entries te
            JOIN projects p ON p.id=te.project_id
            WHERE te.user_id=@uid
            ORDER BY te.entry_date DESC, te.id DESC
            LIMIT @limit";
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@limit", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read()) {
            list.Add(new TimeEntry {
                Id = r.GetInt64(0),
                UserId = r.GetInt64(1),
                ProjectId = r.GetInt64(2),
                ProjectName = r.GetString(3),
                ActivityType = r.GetString(4),
                Description = r.IsDBNull(5) ? "" : r.GetString(5),
                EntryDate = r.GetString(6),
                StartTime = r.IsDBNull(7) ? null : r.GetString(7),
                EndTime = r.IsDBNull(8) ? null : r.GetString(8),
                DurationHours = r.IsDBNull(9) ? 0 : r.GetDouble(9),
                IsRunning = !r.IsDBNull(10) && r.GetInt64(10) == 1,
                CreatedAt = r.IsDBNull(11) ? null : r.GetString(11)
            });
        }
        return list;
    }

    public List<TimeSummary> GetProjectTimeSummary(long userId) {
        var list = new List<TimeSummary>();
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"SELECT p.name, COALESCE(SUM(te.duration_hours),0)
            FROM time_entries te
            JOIN projects p ON p.id=te.project_id
            WHERE te.user_id=@uid AND te.is_running=0
            GROUP BY te.project_id, p.name
            ORDER BY SUM(te.duration_hours) DESC, p.name";
        cmd.Parameters.AddWithValue("@uid", userId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) {
            list.Add(new TimeSummary {
                Label = r.GetString(0),
                Hours = r.IsDBNull(1) ? 0 : r.GetDouble(1)
            });
        }
        return list;
    }

    public List<TimeSummary> GetActivityTimeSummary(long userId) {
        var list = new List<TimeSummary>();
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"SELECT te.activity_type, COALESCE(SUM(te.duration_hours),0)
            FROM time_entries te
            WHERE te.user_id=@uid AND te.is_running=0
            GROUP BY te.activity_type
            ORDER BY SUM(te.duration_hours) DESC, te.activity_type";
        cmd.Parameters.AddWithValue("@uid", userId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) {
            list.Add(new TimeSummary {
                Label = r.GetString(0),
                Hours = r.IsDBNull(1) ? 0 : r.GetDouble(1)
            });
        }
        return list;
    }

    public long StartTimeEntry(long userId, long projectId, string activityType, string? description) {
        using var tx = Conn.BeginTransaction();
        using (var stopCmd = Conn.CreateCommand()) {
            stopCmd.Transaction = tx;
            stopCmd.CommandText = $@"UPDATE time_entries
                SET is_running=0,
                    end_time=@now,
                    duration_hours={_dialect.DurationHoursExpr("start_time", "@now")}
                WHERE user_id=@uid AND is_running=1";
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            stopCmd.Parameters.AddWithValue("@uid", userId);
            stopCmd.Parameters.AddWithValue("@now", now);
            stopCmd.ExecuteNonQuery();
        }

        using (var cmd = Conn.CreateCommand()) {
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO time_entries(user_id,project_id,activity_type,description,entry_date,start_time,is_running,created_at)
                VALUES(@uid,@pid,@act,@desc,@date,@start,1,CURRENT_TIMESTAMP)";
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@pid", projectId);
            cmd.Parameters.AddWithValue("@act", activityType);
            cmd.Parameters.AddWithValue("@desc", (object?)description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@date", DateTime.Today.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@start", now);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return LastInsertId();
    }

    public void StopTimeEntry(long id) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = $@"UPDATE time_entries
            SET is_running=0,
                end_time=@now,
                duration_hours={_dialect.DurationHoursExpr("start_time", "@now")}
            WHERE id=@id AND is_running=1";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.ExecuteNonQuery();
    }

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
        cmd.CommandText = "SELECT password_hash FROM users WHERE username=@u";
        cmd.Parameters.AddWithValue("@u", username);
        var result = cmd.ExecuteScalar();
        if (result == null || result == DBNull.Value) return false;
        var storedHash = result.ToString()!;
        var valid = Services.PasswordHasher.Verify(password, storedHash);
        // Auto-migrate legacy plaintext passwords to PBKDF2
        if (valid && Services.PasswordHasher.IsLegacyFormat(storedHash))
            ChangePassword(username, password);
        return valid;
    }

    public long GetUserId(string username) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM users WHERE username=@u";
        cmd.Parameters.AddWithValue("@u", username);
        var result = cmd.ExecuteScalar();
        return result != null ? Convert.ToInt64(result) : 0;
    }

    public List<string> GetUsernames() {
        var list = new List<string>();
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT username FROM users ORDER BY username";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    public void ChangePassword(string username, string newPassword) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "UPDATE users SET password_hash=@p WHERE username=@u";
        cmd.Parameters.AddWithValue("@u", username);
        cmd.Parameters.AddWithValue("@p", Services.PasswordHasher.Hash(newPassword));
        cmd.ExecuteNonQuery();
    }

    public void AddUser(string username, string password, string fullName) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "INSERT INTO users(username, password_hash, full_name) VALUES(@u, @p, @f)";
        cmd.Parameters.AddWithValue("@u", username);
        cmd.Parameters.AddWithValue("@p", Services.PasswordHasher.Hash(password));
        cmd.Parameters.AddWithValue("@f", fullName);
        cmd.ExecuteNonQuery();
    }

    public void DeleteUser(string username) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "DELETE FROM users WHERE username=@u";
        cmd.Parameters.AddWithValue("@u", username);
        cmd.ExecuteNonQuery();
    }

    public void UpdateUserFullName(string username, string fullName) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "UPDATE users SET full_name=@f WHERE username=@u";
        cmd.Parameters.AddWithValue("@u", username);
        cmd.Parameters.AddWithValue("@f", fullName);
        cmd.ExecuteNonQuery();
    }

    public string? GetFullName(string username) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT full_name FROM users WHERE username=@u";
        cmd.Parameters.AddWithValue("@u", username);
        return cmd.ExecuteScalar() as string;
    }

    public void SetUserRole(string username, long? roleId) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "UPDATE users SET role_id=@r WHERE username=@u";
        cmd.Parameters.AddWithValue("@u", username);
        cmd.Parameters.AddWithValue("@r", roleId.HasValue ? (object)roleId.Value : DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public long? GetUserRoleId(string username) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT role_id FROM users WHERE username=@u";
        cmd.Parameters.AddWithValue("@u", username);
        var result = cmd.ExecuteScalar();
        return result is DBNull or null ? null : Convert.ToInt64(result);
    }

    // Roles CRUD
    public List<Role> GetRoles() {
        var list = new List<Role>();
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT id,name,description FROM roles ORDER BY id";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new Role {
            Id = r.GetInt64(0), Name = r.GetString(1),
            Description = r.IsDBNull(2) ? "" : r.GetString(2)
        });
        return list;
    }

    public void AddRole(Role role) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "INSERT INTO roles(name,description) VALUES(@n,@d)";
        cmd.Parameters.AddWithValue("@n", role.Name);
        cmd.Parameters.AddWithValue("@d", role.Description);
        cmd.ExecuteNonQuery();
        role.Id = LastInsertId();
    }

    public void UpdateRole(Role role) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "UPDATE roles SET name=@n,description=@d WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", role.Id);
        cmd.Parameters.AddWithValue("@n", role.Name);
        cmd.Parameters.AddWithValue("@d", role.Description);
        cmd.ExecuteNonQuery();
    }

    public void DeleteRole(long id) { ExecWithId("DELETE FROM roles WHERE id=@id", id); }

    public List<RolePermission> GetRolePermissions(long roleId) {
        var list = new List<RolePermission>();
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT id,role_id,page_key,access_level FROM role_permissions WHERE role_id=@r";
        cmd.Parameters.AddWithValue("@r", roleId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new RolePermission {
            Id = r.GetInt64(0), RoleId = r.GetInt64(1),
            PageKey = r.GetString(2), AccessLevel = r.IsDBNull(3) ? "none" : r.GetString(3)
        });
        return list;
    }

    public void SetRolePermission(long roleId, string pageKey, string accessLevel) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = _dialect.UpsertRolePermission();
        cmd.Parameters.AddWithValue("@rid", roleId);
        cmd.Parameters.AddWithValue("@pk", pageKey);
        cmd.Parameters.AddWithValue("@a", accessLevel);
        cmd.ExecuteNonQuery();
    }

    public string GetUserAccessLevel(string username, string pageKey) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"SELECT rp.access_level FROM users u
            JOIN role_permissions rp ON rp.role_id=u.role_id
            WHERE u.username=@u AND rp.page_key=@p";
        cmd.Parameters.AddWithValue("@u", username);
        cmd.Parameters.AddWithValue("@p", pageKey);
        var result = cmd.ExecuteScalar();
        return result as string ?? "none";
    }

    public Dictionary<string, string> GetUserPermissions(string username) {
        var perms = new Dictionary<string, string>();
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"SELECT rp.page_key,rp.access_level FROM users u
            JOIN role_permissions rp ON rp.role_id=u.role_id
            WHERE u.username=@u";
        cmd.Parameters.AddWithValue("@u", username);
        using var r = cmd.ExecuteReader();
        while (r.Read()) perms[r.GetString(0)] = r.GetString(1);
        return perms;
    }

    // CRUD: Customers
    public List<Customer> GetCustomers() {
        var list = new List<Customer>();
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT id,company,contact_name,email,phone,street,zip_code,city,country,tax_id,status,notes,created_at FROM customers ORDER BY company,contact_name";
        using var r = cmd.ExecuteReader();
        while (r.Read()) {
            list.Add(new Customer {
                Id = r.GetInt64(0),
                Company = r.IsDBNull(1) ? "" : r.GetString(1),
                ContactName = r.IsDBNull(2) ? "" : r.GetString(2),
                Email = r.IsDBNull(3) ? "" : r.GetString(3),
                Phone = r.IsDBNull(4) ? "" : r.GetString(4),
                Street = r.IsDBNull(5) ? "" : r.GetString(5),
                ZipCode = r.IsDBNull(6) ? "" : r.GetString(6),
                City = r.IsDBNull(7) ? "" : r.GetString(7),
                Country = r.IsDBNull(8) ? "" : r.GetString(8),
                TaxId = r.IsDBNull(9) ? "" : r.GetString(9),
                Status = r.IsDBNull(10) ? "Aktiv" : r.GetString(10),
                Notes = r.IsDBNull(11) ? "" : r.GetString(11),
                CreatedAt = r.IsDBNull(12) ? "" : r.GetString(12)
            });
        }
        return list;
    }

    public void AddCustomer(Customer c) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO customers(company,contact_name,email,phone,street,zip_code,city,country,tax_id,status,notes,created_at)
            VALUES(@company,@contact,@email,@phone,@street,@zip,@city,@country,@tax,@status,@notes,CURRENT_TIMESTAMP)";
        cmd.Parameters.AddWithValue("@company", (object?)c.Company ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@contact", (object?)c.ContactName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@email", (object?)c.Email ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@phone", (object?)c.Phone ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@street", (object?)c.Street ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@zip", (object?)c.ZipCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@city", (object?)c.City ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@country", (object?)c.Country ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tax", (object?)c.TaxId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@status", (object?)c.Status ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@notes", (object?)c.Notes ?? DBNull.Value);
        cmd.ExecuteNonQuery();
        c.Id = LastInsertId();
    }

    public void UpdateCustomer(Customer c) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"UPDATE customers SET company=@company,contact_name=@contact,email=@email,phone=@phone,
            street=@street,zip_code=@zip,city=@city,country=@country,tax_id=@tax,status=@status,notes=@notes WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", c.Id);
        cmd.Parameters.AddWithValue("@company", (object?)c.Company ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@contact", (object?)c.ContactName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@email", (object?)c.Email ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@phone", (object?)c.Phone ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@street", (object?)c.Street ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@zip", (object?)c.ZipCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@city", (object?)c.City ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@country", (object?)c.Country ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tax", (object?)c.TaxId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@status", (object?)c.Status ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@notes", (object?)c.Notes ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void DeleteCustomer(long id) { ExecWithId("DELETE FROM customers WHERE id=@id", id); }

    // Helpers
    long LastInsertId() {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = _dialect.LastInsertIdSql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    void ExecWithId(string sql, long id) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    void EnsureDefaultRoles() {
        var allPages = new[] { "dashboard", "transactions", "fixkosten", "taxes", "invoices", "offers", "resources", "targets", "todos", "timetracking", "admin" };

        long adminRoleId = EnsureRole("Admin", "Vollzugriff auf alle Bereiche");
        foreach (var page in allPages)
            EnsureRolePermission(adminRoleId, page, "full");

        long engineerRoleId = EnsureRole("Ingenieur", "Projekt-, Ressourcen- und Aufgabenbearbeitung");
        EnsureRolePermission(engineerRoleId, "dashboard", "read");
        EnsureRolePermission(engineerRoleId, "offers", "read");
        EnsureRolePermission(engineerRoleId, "resources", "full");
        EnsureRolePermission(engineerRoleId, "todos", "full");
        EnsureRolePermission(engineerRoleId, "timetracking", "full");

        long accountingRoleId = EnsureRole("Accounting", "Finanzen, Rechnungen, Angebote und Steuern");
        EnsureRolePermission(accountingRoleId, "dashboard", "read");
        EnsureRolePermission(accountingRoleId, "transactions", "full");
        EnsureRolePermission(accountingRoleId, "fixkosten", "full");
        EnsureRolePermission(accountingRoleId, "taxes", "full");
        EnsureRolePermission(accountingRoleId, "invoices", "full");
        EnsureRolePermission(accountingRoleId, "offers", "full");
        EnsureRolePermission(accountingRoleId, "resources", "read");
        EnsureRolePermission(accountingRoleId, "targets", "full");
        EnsureRolePermission(accountingRoleId, "todos", "read");
        EnsureRolePermission(accountingRoleId, "timetracking", "read");

        long managementRoleId = EnsureRole("Management", "Lesender Zugriff auf relevante Bereiche");
        EnsureRolePermission(managementRoleId, "dashboard", "read");
        EnsureRolePermission(managementRoleId, "transactions", "read");
        EnsureRolePermission(managementRoleId, "fixkosten", "read");
        EnsureRolePermission(managementRoleId, "taxes", "read");
        EnsureRolePermission(managementRoleId, "invoices", "read");
        EnsureRolePermission(managementRoleId, "offers", "read");
        EnsureRolePermission(managementRoleId, "resources", "read");
        EnsureRolePermission(managementRoleId, "targets", "read");
        EnsureRolePermission(managementRoleId, "todos", "read");
        EnsureRolePermission(managementRoleId, "timetracking", "read");

        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "UPDATE users SET role_id=@rid WHERE username='admin' AND (role_id IS NULL OR role_id=0)";
        cmd.Parameters.AddWithValue("@rid", adminRoleId);
        cmd.ExecuteNonQuery();
    }

    long EnsureRole(string name, string description) {
        using (var insert = Conn.CreateCommand()) {
            insert.CommandText = _dialect.InsertOrIgnore("INSERT OR IGNORE INTO roles(name,description) VALUES(@n,@d)");
            insert.Parameters.AddWithValue("@n", name);
            insert.Parameters.AddWithValue("@d", description);
            insert.ExecuteNonQuery();
        }

        using var select = Conn.CreateCommand();
        select.CommandText = "SELECT id FROM roles WHERE name=@n";
        select.Parameters.AddWithValue("@n", name);
        return Convert.ToInt64(select.ExecuteScalar());
    }

    void EnsureRolePermission(long roleId, string pageKey, string accessLevel) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = _dialect.InsertOrIgnore(@"INSERT OR IGNORE INTO role_permissions(role_id,page_key,access_level)
            VALUES(@r,@p,@a)");
        cmd.Parameters.AddWithValue("@r", roleId);
        cmd.Parameters.AddWithValue("@p", pageKey);
        cmd.Parameters.AddWithValue("@a", accessLevel);
        cmd.ExecuteNonQuery();
    }
}
