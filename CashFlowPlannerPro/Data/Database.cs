using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using CashFlowPlannerPro.Models;

namespace CashFlowPlannerPro.Data;

public sealed class Database : IDisposable {
    private const string DatabaseInstanceIdSettingKey = "database_instance_id";
    private const string SchemaVersionSettingKey = "schema_version";
    private const string CurrentSchemaVersion = "2026.08.20.2";
    private static readonly string[] MariaDbCreatedAtTables = [
        "users", "transactions", "bank_accounts", "bank_transactions", "offers", "invoices",
        "resources", "projects", "resource_allocations", "hardware_resources",
        "hardware_allocations", "project_milestones", "user_todos", "time_entries", "customers"
    ];
    private const int SecurityStampByteCount = 32;
    private static readonly HashSet<string> KnownPermissionPages = new(StringComparer.Ordinal) {
        "dashboard", "transactions", "bank", "fixkosten", "taxes", "invoices", "offers",
        "resources", "targets", "todos", "timetracking", "kunden", "integrations", "admin"
    };
    internal static IReadOnlyList<string> DefaultCategoryNames { get; } = Array.AsReadOnly([
        "Lohn", "Kapitalsteuer", "Sozialversicherung", "Lohnsteuer", "Umsatzsteuer",
        "Versicherung", "Miete", "Strom", "Telefon", "Internet", "Fahrkosten", "Steuerberatung"
    ]);
    static readonly Lazy<Database> _instance = new(() => new Database());
    public static Database Instance => _instance.Value;
    DbConnection? _conn;
    IDbDialect _dialect = new MariaDbDialect();
    ConnectionConfig? _activeConfig;
    Database() { }

    public IDbDialect Dialect => _dialect;

    public void Open(ConnectionConfig config) {
        Close();
        _dialect = new MariaDbDialect();
        if (string.IsNullOrWhiteSpace(config.Host))
            throw new ArgumentException("Bitte einen MariaDB-Host eingeben.", nameof(config));
        if (config.Port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(config), "Der MariaDB-Port muss zwischen 1 und 65535 liegen.");
        if (string.IsNullOrWhiteSpace(config.DatabaseName))
            throw new ArgumentException("Bitte einen MariaDB-Datenbanknamen eingeben.", nameof(config));
        if (string.IsNullOrWhiteSpace(config.DbUsername))
            throw new ArgumentException("Bitte einen MariaDB-Benutzernamen eingeben.", nameof(config));
        if (string.IsNullOrEmpty(config.DbPassword))
            throw new ArgumentException("Bitte ein MariaDB-Passwort eingeben. MariaDB TLS benötigt ein nicht leeres Passwort.", nameof(config));

        OpenConnection(config.ToConnectionString());

        _activeConfig = config.Clone();
    }

    private void OpenConnection(string connectionString) {
        var connection = _dialect.CreateConnection(connectionString);
        try {
            connection.Open();
            _dialect.ConfigureConnection(connection);
            _conn = connection;
        }
        catch {
            connection.Dispose();
            throw;
        }
    }

    private void OpenSecondary(ConnectionConfig config) {
        Close();
        _dialect = new MariaDbDialect();

        var connection = _dialect.CreateConnection(config.ToConnectionString());
        try {
            connection.Open();
            _dialect.ConfigureConnection(connection);

            _conn = connection;
            _activeConfig = config.Clone();
        }
        catch {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Runs database work on a dedicated connection. DbConnection instances
    /// are not shared across threads, so callers may safely use this from a
    /// background worker while the UI keeps using the primary connection.
    /// </summary>
    public Task<TResult> RunWithIndependentConnectionAsync<TResult>(
        Func<Database, TResult> operation,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(operation);
        var config = _activeConfig?.Clone()
            ?? throw new InvalidOperationException("Database not open");

        return Task.Run(() => {
            cancellationToken.ThrowIfCancellationRequested();
            using var worker = new Database();
            worker.OpenSecondary(config);
            cancellationToken.ThrowIfCancellationRequested();
            return operation(worker);
        }, cancellationToken);
    }

    public void Close() {
        if (_conn != null) { _conn.Close(); _conn.Dispose(); _conn = null; }
        _activeConfig = null;
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

    private void EnsureMariaDbDocumentSnapshotStorage() {
        if (_dialect is not MariaDbDialect)
            return;

        string? dataType;
        using (var cmd = Conn.CreateCommand()) {
            cmd.CommandText = @"SELECT DATA_TYPE
                FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA=DATABASE()
                  AND TABLE_NAME='document_contents'
                  AND COLUMN_NAME='source_snapshot_json'
                LIMIT 1";
            dataType = cmd.ExecuteScalar()?.ToString();
        }

        if (string.IsNullOrWhiteSpace(dataType)) {
            Exec("ALTER TABLE document_contents ADD COLUMN IF NOT EXISTS source_snapshot_json LONGTEXT NULL");
            return;
        }

        if (!dataType.Equals("longtext", StringComparison.OrdinalIgnoreCase))
            Exec("ALTER TABLE document_contents MODIFY COLUMN source_snapshot_json LONGTEXT NULL");
    }

    private void EnsureMariaDbBankFixedCostForeignKey() {
        if (_dialect is not MariaDbDialect)
            return;

        using (var check = Conn.CreateCommand()) {
            check.CommandText = @"SELECT COUNT(*)
                FROM information_schema.KEY_COLUMN_USAGE
                WHERE TABLE_SCHEMA=DATABASE()
                  AND TABLE_NAME='bank_transactions'
                  AND COLUMN_NAME='fixed_cost_transaction_id'
                  AND REFERENCED_TABLE_NAME='transactions'
                  AND REFERENCED_COLUMN_NAME='id'";
            if (Convert.ToInt64(check.ExecuteScalar(), CultureInfo.InvariantCulture) > 0)
                return;
        }

        Exec(@"ALTER TABLE bank_transactions
            ADD CONSTRAINT fk_bank_transactions_fixed_cost_transaction
            FOREIGN KEY(fixed_cost_transaction_id) REFERENCES transactions(id) ON DELETE SET NULL");
    }

    private void EnsureUserSecurityColumns() {
        TryMigrate("ALTER TABLE users ADD COLUMN is_active INTEGER NOT NULL DEFAULT 1");
        // The temporary default makes the migration safe for existing rows.
        // Every empty legacy value is replaced below; all application inserts
        // provide a cryptographically random stamp.
        TryMigrate("ALTER TABLE users ADD COLUMN security_stamp VARCHAR(64) NOT NULL DEFAULT ''");

        using (var tx = BeginWriteTransaction()) {
            try {
                var users = new List<(long Id, string Username, int IsActive, string SecurityStamp)>();
                using (var select = Conn.CreateCommand()) {
                    select.Transaction = tx;
                    select.CommandText = "SELECT id,username,is_active,security_stamp FROM users ORDER BY id"
                        + (_dialect is MariaDbDialect ? " FOR UPDATE" : "");
                    using var reader = select.ExecuteReader();
                    while (reader.Read()) {
                        users.Add((
                            reader.GetInt64(0),
                            reader.IsDBNull(1) ? "" : reader.GetString(1),
                            reader.IsDBNull(2) ? 1 : Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture),
                            reader.IsDBNull(3) ? "" : reader.GetString(3)));
                    }
                }

                foreach (var user in users) {
                    var normalizedActive = string.Equals(user.Username, "admin", StringComparison.Ordinal)
                        ? 1
                        : user.IsActive == 0 ? 0 : 1;
                    var stamp = IsValidSecurityStamp(user.SecurityStamp)
                        ? user.SecurityStamp
                        : CreateSecurityStamp();
                    if (normalizedActive == user.IsActive && stamp == user.SecurityStamp)
                        continue;

                    using var update = Conn.CreateCommand();
                    update.Transaction = tx;
                    update.CommandText = "UPDATE users SET is_active=@active,security_stamp=@stamp WHERE id=@id";
                    update.Parameters.AddWithValue("@active", normalizedActive);
                    update.Parameters.AddWithValue("@stamp", stamp);
                    update.Parameters.AddWithValue("@id", user.Id);
                    if (update.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException($"Sicherheitsstatus für Benutzer #{user.Id} konnte nicht migriert werden.");
                }

                tx.Commit();
            }
            catch {
                TryRollback(tx);
                throw;
            }
        }

        if (_dialect is MariaDbDialect) {
            Exec("ALTER TABLE users MODIFY COLUMN is_active TINYINT(1) NOT NULL DEFAULT 1");
            Exec("ALTER TABLE users MODIFY COLUMN security_stamp VARCHAR(64) NOT NULL");
        }
        Exec("CREATE INDEX IF NOT EXISTS idx_users_is_active ON users(is_active)");
    }

    private static string CreateSecurityStamp() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(SecurityStampByteCount));

    private static bool IsValidSecurityStamp(string? value) =>
        value is { Length: SecurityStampByteCount * 2 }
        && value.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f');

    private bool HasCurrentSchemaVersion() {
        try {
            using var cmd = Conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM settings WHERE `key`=@key";
            cmd.Parameters.AddWithValue("@key", SchemaVersionSettingKey);
            return string.Equals(cmd.ExecuteScalar()?.ToString(), CurrentSchemaVersion, StringComparison.Ordinal);
        }
        catch (DbException) {
            // A database created by an older release may not have settings yet.
            // In that case the regular, one-time migration path must run.
            return false;
        }
    }

    private void SetCurrentSchemaVersion() {
        using var tx = BeginWriteTransaction();
        try {
            using var cmd = Conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = _dialect is MariaDbDialect
                ? "INSERT INTO settings(`key`,value) VALUES(@key,@value) ON DUPLICATE KEY UPDATE value=VALUES(value)"
                : "INSERT INTO settings(`key`,value) VALUES(@key,@value) ON CONFLICT(`key`) DO UPDATE SET value=excluded.value";
            cmd.Parameters.AddWithValue("@key", SchemaVersionSettingKey);
            cmd.Parameters.AddWithValue("@value", CurrentSchemaVersion);
            cmd.ExecuteNonQuery();
            tx.Commit();
        }
        catch {
            TryRollback(tx);
            throw;
        }
    }

    private void RefreshFirstRunState() {
        IsFirstRun = false;
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT username,password_hash,is_active FROM users";
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) {
            if (!string.Equals(reader.GetString(0), "admin", StringComparison.Ordinal))
                continue;

            var isActive = !reader.IsDBNull(2)
                && Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture) != 0;
            IsFirstRun = isActive
                && !reader.IsDBNull(1)
                && Services.PasswordHasher.Verify("admin", reader.GetString(1));
            return;
        }
    }

    private void EnsureMariaDbCreatedAtColumns() {
        if (_dialect is not MariaDbDialect)
            return;

        foreach (var table in MariaDbCreatedAtTables) {
            string? dataType;
            string? isNullable;
            string? defaultValue;
            using (var inspect = Conn.CreateCommand()) {
                inspect.CommandText = @"SELECT DATA_TYPE,IS_NULLABLE,COLUMN_DEFAULT
                    FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@table AND COLUMN_NAME='created_at'
                    LIMIT 1";
                inspect.Parameters.AddWithValue("@table", table);
                using var reader = inspect.ExecuteReader();
                if (!reader.Read())
                    continue;
                dataType = reader.IsDBNull(0) ? null : reader.GetString(0);
                isNullable = reader.IsDBNull(1) ? null : reader.GetString(1);
                defaultValue = reader.IsDBNull(2) ? null : reader.GetValue(2)?.ToString();
            }

            var alreadySafe = string.Equals(dataType, "datetime", StringComparison.OrdinalIgnoreCase)
                && string.Equals(isNullable, "NO", StringComparison.OrdinalIgnoreCase)
                && defaultValue?.Contains("current_timestamp", StringComparison.OrdinalIgnoreCase) == true;
            if (alreadySafe)
                continue;

            var quotedTable = QuoteMariaDbIdentifier(table);
            Exec($@"UPDATE {quotedTable} SET `created_at`=CURRENT_TIMESTAMP
                WHERE `created_at` IS NULL OR TRIM(CAST(`created_at` AS CHAR))=''");
            Exec($@"ALTER TABLE {quotedTable}
                MODIFY COLUMN `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP");
        }
    }

    private void EnsureMariaDbCustomerForeignKeys() {
        if (_dialect is not MariaDbDialect)
            return;

        EnsureMariaDbCustomerForeignKey("invoices", "fk_invoices_customer");
        EnsureMariaDbCustomerForeignKey("offers", "fk_offers_customer");
    }

    private void EnsureMariaDbCustomerForeignKey(string table, string desiredConstraintName) {
        var constraints = new List<(string Name, string DeleteRule)>();
        using (var inspect = Conn.CreateCommand()) {
            inspect.CommandText = @"SELECT k.CONSTRAINT_NAME,COALESCE(r.DELETE_RULE,'')
                FROM information_schema.KEY_COLUMN_USAGE k
                LEFT JOIN information_schema.REFERENTIAL_CONSTRAINTS r
                  ON r.CONSTRAINT_SCHEMA=k.CONSTRAINT_SCHEMA
                 AND r.CONSTRAINT_NAME=k.CONSTRAINT_NAME
                 AND r.TABLE_NAME=k.TABLE_NAME
                WHERE k.TABLE_SCHEMA=DATABASE()
                  AND k.TABLE_NAME=@table
                  AND k.COLUMN_NAME='customer_id'
                  AND k.REFERENCED_TABLE_NAME='customers'
                  AND k.REFERENCED_COLUMN_NAME='id'";
            inspect.Parameters.AddWithValue("@table", table);
            using var reader = inspect.ExecuteReader();
            while (reader.Read())
                constraints.Add((reader.GetString(0), reader.IsDBNull(1) ? "" : reader.GetString(1)));
        }

        if (constraints.Count == 1
            && string.Equals(constraints[0].DeleteRule, "SET NULL", StringComparison.OrdinalIgnoreCase))
            return;

        var quotedTable = QuoteMariaDbIdentifier(table);
        foreach (var constraint in constraints)
            Exec($"ALTER TABLE {quotedTable} DROP FOREIGN KEY {QuoteMariaDbIdentifier(constraint.Name)}");

        Exec($@"UPDATE {quotedTable} child
            LEFT JOIN `customers` parent ON parent.`id`=child.`customer_id`
            SET child.`customer_id`=NULL
            WHERE child.`customer_id` IS NOT NULL AND parent.`id` IS NULL");
        Exec($"ALTER TABLE {quotedTable} MODIFY COLUMN `customer_id` BIGINT NULL");
        Exec($@"ALTER TABLE {quotedTable}
            ADD CONSTRAINT {QuoteMariaDbIdentifier(desiredConstraintName)}
            FOREIGN KEY(`customer_id`) REFERENCES `customers`(`id`) ON DELETE SET NULL");
    }

    private static string QuoteMariaDbIdentifier(string identifier) {
        if (string.IsNullOrWhiteSpace(identifier)
            || identifier.Any(character => !(char.IsLetterOrDigit(character) || character is '_' or '$')))
            throw new InvalidOperationException($"Ungültiger MariaDB-Bezeichner: {identifier}");
        return $"`{identifier}`";
    }

    private void EnsureOfferNumberConstraint() {
        // Old databases allowed NULL, blank and duplicate numbers. Normalize
        // them under the database's own comparison rules before adding the
        // unique index. The lowest ID in every non-empty duplicate group keeps
        // the original number; later rows receive deterministic, traceable
        // suffixes. Candidate checks go through the database so this also
        // respects a MariaDB installation's configured collation.
        using (var tx = BeginWriteTransaction()) {
            try {
                var rows = new List<(long Id, string Number)>();
                using (var select = Conn.CreateCommand()) {
                    select.Transaction = tx;
                    select.CommandText = "SELECT id,offer_number FROM offers ORDER BY id"
                        + (_dialect is MariaDbDialect ? " FOR UPDATE" : "");
                    using var reader = select.ExecuteReader();
                    while (reader.Read()) {
                        rows.Add((
                            reader.GetInt64(0),
                            reader.IsDBNull(1) ? "" : reader.GetString(1)));
                    }
                }

                foreach (var row in rows) {
                    if (string.IsNullOrWhiteSpace(row.Number)) {
                        UpdateLegacyOfferNumber(
                            row.Id,
                            CreateUniqueLegacyOfferNumber("LEGACY", row.Id, duplicate: false, tx),
                            tx);
                        continue;
                    }

                    using var duplicate = Conn.CreateCommand();
                    duplicate.Transaction = tx;
                    duplicate.CommandText = @"SELECT COUNT(*) FROM offers
                        WHERE id<@id AND offer_number=@number";
                    duplicate.Parameters.AddWithValue("@id", row.Id);
                    duplicate.Parameters.AddWithValue("@number", row.Number);
                    if (Convert.ToInt64(duplicate.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
                        continue;

                    UpdateLegacyOfferNumber(
                        row.Id,
                        CreateUniqueLegacyOfferNumber(row.Number.Trim(), row.Id, duplicate: true, tx),
                        tx);
                }

                tx.Commit();
            }
            catch {
                TryRollback(tx);
                throw;
            }
        }

        if (_dialect is MariaDbDialect)
            Exec("ALTER TABLE offers MODIFY COLUMN offer_number VARCHAR(191) NOT NULL");

        Exec("CREATE UNIQUE INDEX IF NOT EXISTS ux_offers_offer_number ON offers(offer_number)");
    }

    private string CreateUniqueLegacyOfferNumber(
        string stem,
        long id,
        bool duplicate,
        DbTransaction transaction) {
        stem = string.IsNullOrWhiteSpace(stem) ? "LEGACY" : stem;
        for (long attempt = 0; ; attempt++) {
            var suffix = duplicate
                ? attempt == 0
                    ? $"-DUP-{id}"
                    : $"-DUP-{id}-{attempt}"
                : attempt == 0
                    ? $"-{id}"
                    : $"-{id}-{attempt}";
            var maxStemLength = 100 - suffix.Length;
            if (maxStemLength <= 0)
                throw new InvalidOperationException("Für die Angebotsnummer konnte kein eindeutiger Migrationswert erzeugt werden.");

            var candidate = TruncateWithoutSplittingSurrogate(stem, maxStemLength) + suffix;
            using var exists = Conn.CreateCommand();
            exists.Transaction = transaction;
            exists.CommandText = "SELECT COUNT(*) FROM offers WHERE id<>@id AND offer_number=@number";
            exists.Parameters.AddWithValue("@id", id);
            exists.Parameters.AddWithValue("@number", candidate);
            if (Convert.ToInt64(exists.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
                return candidate;
        }
    }

    private void UpdateLegacyOfferNumber(long id, string number, DbTransaction transaction) {
        using var update = Conn.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE offers SET offer_number=@number WHERE id=@id";
        update.Parameters.AddWithValue("@number", number);
        update.Parameters.AddWithValue("@id", id);
        if (update.ExecuteNonQuery() != 1)
            throw new InvalidOperationException($"Angebotsnummer für Angebot #{id} konnte nicht migriert werden.");
    }

    private static string TruncateWithoutSplittingSurrogate(string value, int maxLength) {
        if (value.Length <= maxLength)
            return value;

        var length = maxLength;
        if (length > 0
            && char.IsHighSurrogate(value[length - 1])
            && length < value.Length
            && char.IsLowSurrogate(value[length]))
            length--;
        return value[..length];
    }

    void Exec(string sql) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
    void ExecDdl(string sql) {
        var rewritten = _dialect.RewriteDdl(sql);
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

    static IEnumerable<DateTime> EnumerateRecurrenceDates(
        DateTime start,
        DateTime windowStart,
        DateTime windowEnd,
        int stepMonths,
        int stepDays) {
        start = start.Date;
        windowStart = windowStart.Date;
        windowEnd = windowEnd.Date;
        if (windowEnd < windowStart || (stepMonths <= 0 && stepDays <= 0))
            yield break;

        if (stepDays > 0) {
            long occurrence = 0;
            if (start < windowStart) {
                var daysBehind = (windowStart - start).Days;
                occurrence = (daysBehind + (long)stepDays - 1) / stepDays;
            }

            for (var current = start.AddDays(occurrence * stepDays);
                 current <= windowEnd;
                 current = current.AddDays(stepDays)) {
                if (current >= windowStart)
                    yield return current;
                if (current > DateTime.MaxValue.Date.AddDays(-stepDays))
                    yield break;
            }
            yield break;
        }

        // Advance by whole recurrence periods from the original anchor. This
        // is both efficient for very old plans and avoids Jan-31 -> Feb-28 ->
        // Mar-28 drift from repeatedly adding clamped months.
        long monthDistance = (windowStart.Year - (long)start.Year) * 12
            + windowStart.Month - start.Month;
        long occurrenceIndex = Math.Max(0, monthDistance / stepMonths);
        var currentMonth = AddMonthsClamped(start, checked((int)(occurrenceIndex * stepMonths)));
        while (currentMonth < windowStart) {
            occurrenceIndex++;
            currentMonth = AddMonthsClamped(start, checked((int)(occurrenceIndex * stepMonths)));
        }

        while (currentMonth <= windowEnd) {
            yield return currentMonth;
            occurrenceIndex++;
            currentMonth = AddMonthsClamped(start, checked((int)(occurrenceIndex * stepMonths)));
        }
    }

    public void EnsureSchema() {
        IsFirstRun = false;
        if (HasCurrentSchemaVersion()) {
            RefreshFirstRunState();
            return;
        }

        ExecDdl(@"CREATE TABLE IF NOT EXISTS users(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            username TEXT UNIQUE NOT NULL,
            password_hash TEXT NOT NULL,
            full_name TEXT,
            is_active INTEGER NOT NULL DEFAULT 1,
            security_stamp VARCHAR(64) NOT NULL,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP)");
        EnsureUserSecurityColumns();
        using (var cmd = Conn.CreateCommand()) {
            cmd.CommandText = "SELECT COUNT(*) FROM users";
            if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            {
                // First-time setup: create admin with hashed default password
                // The login dialog will detect legacy format and prompt for change
                var hashedPw = Services.PasswordHasher.Hash("admin");
                using var ins = Conn.CreateCommand();
                ins.CommandText = @"INSERT INTO users(
                        username,password_hash,full_name,is_active,security_stamp)
                    VALUES('admin',@p,'Administrator',1,@stamp)";
                ins.Parameters.AddWithValue("@p", hashedPw);
                ins.Parameters.AddWithValue("@stamp", CreateSecurityStamp());
                ins.ExecuteNonQuery();
                IsFirstRun = true;
            }
            else
            {
                RefreshFirstRunState();
            }
        }
        ExecDdl("CREATE TABLE IF NOT EXISTS categories(id INTEGER PRIMARY KEY AUTOINCREMENT,name TEXT UNIQUE NOT NULL)");
        ExecDdl("CREATE TABLE IF NOT EXISTS persons(id INTEGER PRIMARY KEY AUTOINCREMENT,name TEXT UNIQUE NOT NULL)");
        ExecDdl(@"CREATE TABLE IF NOT EXISTS transactions(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            date VARCHAR(50) NOT NULL, description TEXT, amount REAL NOT NULL,
            category_id INTEGER, person_id INTEGER, interval TEXT, notes TEXT,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP, updated_at TEXT,
            FOREIGN KEY(category_id) REFERENCES categories(id),
            FOREIGN KEY(person_id) REFERENCES persons(id))");
        ExecDdl(@"CREATE TABLE IF NOT EXISTS bank_accounts(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            source_provider VARCHAR(32) NOT NULL,
            external_account_id VARCHAR(191) NOT NULL,
            account_name TEXT,
            iban_masked VARCHAR(100),
            currency VARCHAR(3) NOT NULL,
            balance REAL,
            last_sync TEXT,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP,
            updated_at TEXT,
            UNIQUE(source_provider, external_account_id))");
        Exec("CREATE INDEX IF NOT EXISTS idx_bank_accounts_provider ON bank_accounts(source_provider)");
        ExecDdl(@"CREATE TABLE IF NOT EXISTS bank_transactions(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            bank_account_id INTEGER NOT NULL,
            source_provider VARCHAR(32) NOT NULL,
            source_external_id VARCHAR(191) NOT NULL,
            entry_date VARCHAR(10) NOT NULL,
            value_date VARCHAR(10),
            amount REAL NOT NULL,
            currency VARCHAR(3) NOT NULL,
            purpose TEXT,
            payee TEXT,
            status VARCHAR(50) NOT NULL,
            fixed_cost_transaction_id INTEGER,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP,
            updated_at TEXT,
            FOREIGN KEY(bank_account_id) REFERENCES bank_accounts(id) ON DELETE CASCADE,
            FOREIGN KEY(fixed_cost_transaction_id) REFERENCES transactions(id) ON DELETE SET NULL,
            UNIQUE(bank_account_id, source_external_id))");
        if (_dialect is MariaDbDialect) {
            TryMigrate("ALTER TABLE bank_transactions ADD COLUMN fixed_cost_transaction_id BIGINT NULL");
            EnsureMariaDbBankFixedCostForeignKey();
        }
        else {
            TryMigrate(@"ALTER TABLE bank_transactions ADD COLUMN fixed_cost_transaction_id INTEGER
                REFERENCES transactions(id) ON DELETE SET NULL");
        }
        Exec("CREATE INDEX IF NOT EXISTS idx_bank_transactions_account_date ON bank_transactions(bank_account_id,entry_date)");
        Exec("CREATE INDEX IF NOT EXISTS idx_bank_transactions_entry_date ON bank_transactions(entry_date)");
        Exec("CREATE UNIQUE INDEX IF NOT EXISTS ux_bank_transactions_fixed_cost_transaction_id ON bank_transactions(fixed_cost_transaction_id)");
        ExecDdl(@"CREATE TABLE IF NOT EXISTS offers(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            offer_number VARCHAR(100) NOT NULL UNIQUE, offer_date TEXT, date_expected TEXT,
            customer TEXT, amount_before_discount REAL DEFAULT 0, discount_percent REAL DEFAULT 0,
            amount REAL, probability REAL, description TEXT, status TEXT,
            payment_delay INTEGER DEFAULT 30, pdf_path TEXT,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP)");
        EnsureOfferNumberConstraint();
        TryMigrate("ALTER TABLE offers ADD COLUMN amount_before_discount REAL DEFAULT 0");
        TryMigrate("ALTER TABLE offers ADD COLUMN discount_percent REAL DEFAULT 0");
        Exec("UPDATE offers SET amount_before_discount=amount WHERE (amount_before_discount IS NULL OR amount_before_discount=0) AND COALESCE(amount,0)<>0");
        ExecDdl(@"CREATE TABLE IF NOT EXISTS invoices(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            invoice_number TEXT, issue_date TEXT, due_date TEXT, customer TEXT, amount REAL,
            net_amount REAL DEFAULT 0, vat_amount REAL DEFAULT 0, vat_rate REAL DEFAULT 19,
            description TEXT, paid_date TEXT, paid_amount REAL, status TEXT,
            pdf_path TEXT, created_at TEXT DEFAULT CURRENT_TIMESTAMP)");
        TryMigrate("ALTER TABLE invoices ADD COLUMN invoice_number TEXT");
        TryMigrate("ALTER TABLE invoices ADD COLUMN net_amount REAL DEFAULT 0");
        TryMigrate("ALTER TABLE invoices ADD COLUMN vat_amount REAL DEFAULT 0");
        TryMigrate("ALTER TABLE invoices ADD COLUMN vat_rate REAL DEFAULT 19");
        var snapshotJsonType = _dialect is MariaDbDialect ? "LONGTEXT" : "TEXT";
        ExecDdl($@"CREATE TABLE IF NOT EXISTS document_contents(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            offer_id INTEGER UNIQUE,
            invoice_id INTEGER UNIQUE,
            header TEXT,
            pre_text TEXT,
            post_text TEXT,
            internal_note TEXT,
            source_provider VARCHAR(32),
            source_entity_type VARCHAR(32),
            source_external_id VARCHAR(191),
            source_snapshot_json {snapshotJsonType},
            last_imported_at TEXT,
            FOREIGN KEY(offer_id) REFERENCES offers(id) ON DELETE CASCADE,
            FOREIGN KEY(invoice_id) REFERENCES invoices(id) ON DELETE CASCADE,
            UNIQUE(source_provider, source_entity_type, source_external_id),
            CHECK((offer_id IS NOT NULL AND invoice_id IS NULL) OR
                  (offer_id IS NULL AND invoice_id IS NOT NULL)))");
        EnsureMariaDbDocumentSnapshotStorage();
        ExecDdl(@"CREATE TABLE IF NOT EXISTS document_line_items(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            document_content_id INTEGER NOT NULL,
            source_item_id VARCHAR(191),
            sort_order INTEGER NOT NULL DEFAULT 0,
            position_number VARCHAR(50),
            name TEXT,
            description TEXT,
            quantity REAL NOT NULL DEFAULT 1,
            unit VARCHAR(50),
            unit_price REAL NOT NULL DEFAULT 0,
            discount_percent REAL NOT NULL DEFAULT 0,
            tax_rate REAL NOT NULL DEFAULT 0,
            net_amount REAL NOT NULL DEFAULT 0,
            gross_amount REAL NOT NULL DEFAULT 0,
            is_optional INTEGER NOT NULL DEFAULT 0,
            FOREIGN KEY(document_content_id) REFERENCES document_contents(id) ON DELETE CASCADE,
            UNIQUE(document_content_id, source_item_id))");
        Exec("CREATE INDEX IF NOT EXISTS idx_document_line_items_content_sort ON document_line_items(document_content_id,sort_order,id)");
        ExecDdl(@"CREATE TABLE IF NOT EXISTS targets(
            id INTEGER PRIMARY KEY AUTOINCREMENT, year INTEGER, month INTEGER, amount REAL)");
        ExecDdl("CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT)");
        EnsureDatabaseInstanceId();
        ExecDdl(@"CREATE TABLE IF NOT EXISTS resources(
            id INTEGER PRIMARY KEY AUTOINCREMENT, user_id INTEGER, name TEXT NOT NULL, role TEXT,
            availability REAL DEFAULT 1.0, hourly_rate REAL DEFAULT 0,
            work_start_hour INTEGER DEFAULT 8, work_end_hour INTEGER DEFAULT 17,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY(user_id) REFERENCES users(id) ON DELETE SET NULL)");
        TryMigrate("ALTER TABLE resources ADD COLUMN user_id INTEGER REFERENCES users(id) ON DELETE SET NULL");
        Exec("CREATE UNIQUE INDEX IF NOT EXISTS ux_resources_user_id ON resources(user_id)");
        TryMigrate("ALTER TABLE resources ADD COLUMN work_start_hour INTEGER DEFAULT 8");
        TryMigrate("ALTER TABLE resources ADD COLUMN work_end_hour INTEGER DEFAULT 17");
        ExecDdl(@"CREATE TABLE IF NOT EXISTS projects(
            id INTEGER PRIMARY KEY AUTOINCREMENT, project_number TEXT, name TEXT NOT NULL,
            client TEXT DEFAULT '', color TEXT DEFAULT '#3498db', start_date TEXT, end_date TEXT,
            original_budget REAL DEFAULT 0, discount_percent REAL DEFAULT 0,
            budget REAL DEFAULT 0, status TEXT DEFAULT 'active',
            created_at TEXT DEFAULT CURRENT_TIMESTAMP)");
        TryMigrate("ALTER TABLE projects ADD COLUMN client TEXT DEFAULT ''");
        TryMigrate("ALTER TABLE projects ADD COLUMN original_budget REAL DEFAULT 0");
        TryMigrate("ALTER TABLE projects ADD COLUMN discount_percent REAL DEFAULT 0");
        Exec("UPDATE projects SET original_budget=budget WHERE (original_budget IS NULL OR original_budget=0) AND COALESCE(budget,0)<>0");
        TryMigrate("ALTER TABLE offers ADD COLUMN project_id INTEGER REFERENCES projects(id) ON DELETE SET NULL");
        Exec("CREATE UNIQUE INDEX IF NOT EXISTS ux_offers_project_id ON offers(project_id)");
        ExecDdl(@"CREATE TABLE IF NOT EXISTS resource_allocations(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            resource_id INTEGER NOT NULL, project_id INTEGER NOT NULL,
            date VARCHAR(50) NOT NULL, hours REAL DEFAULT 8.0, notes TEXT,
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
            date VARCHAR(50) NOT NULL, hours REAL DEFAULT 8.0, notes TEXT,
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
            role_id INTEGER NOT NULL, page_key VARCHAR(191) NOT NULL,
            access_level TEXT DEFAULT 'none',
            FOREIGN KEY(role_id) REFERENCES roles(id) ON DELETE CASCADE,
            UNIQUE(role_id, page_key))");
        TryMigrate("ALTER TABLE users ADD COLUMN role_id INTEGER REFERENCES roles(id)");
        TryMigrate("ALTER TABLE users ADD COLUMN avatar_data TEXT");
        TryMigrate("ALTER TABLE resources ADD COLUMN avatar_data TEXT");
        BackfillResourceUserLinks();
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
            `key` VARCHAR(191) NOT NULL,
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
            customer_number VARCHAR(100) DEFAULT '',
            company TEXT, contact_name TEXT, email TEXT, phone TEXT,
            street TEXT, zip_code TEXT, city TEXT, country TEXT DEFAULT 'Deutschland',
            tax_id TEXT, status TEXT DEFAULT 'Aktiv', notes TEXT,
            created_at TEXT DEFAULT CURRENT_TIMESTAMP)");
        TryMigrate("ALTER TABLE customers ADD COLUMN customer_number VARCHAR(100) DEFAULT ''");
        Exec("CREATE INDEX IF NOT EXISTS idx_customers_customer_number ON customers(customer_number)");
        TryMigrate("ALTER TABLE invoices ADD COLUMN customer_id INTEGER REFERENCES customers(id) ON DELETE SET NULL");
        TryMigrate("ALTER TABLE offers ADD COLUMN customer_id INTEGER REFERENCES customers(id) ON DELETE SET NULL");
        EnsureMariaDbCustomerForeignKeys();
        EnsureMariaDbCreatedAtColumns();

        // Migration: ensure "kunden" permission exists for all roles that have
        // "invoices" access. Failure must abort schema completion; silently setting
        // the schema version here could otherwise leave authorization incomplete.
        using (var kcmd = Conn.CreateCommand()) {
            kcmd.CommandText = _dialect.InsertOrIgnore(@"INSERT OR IGNORE INTO role_permissions(role_id,page_key,access_level)
                SELECT role_id,'kunden',access_level FROM role_permissions WHERE page_key='invoices'");
            kcmd.ExecuteNonQuery();
        }

        foreach (var c in DefaultCategoryNames) {
            using var cmd = Conn.CreateCommand();
            cmd.CommandText = _dialect.InsertOrIgnore("INSERT OR IGNORE INTO categories(name) VALUES(@n)");
            cmd.Parameters.AddWithValue("@n", c);
            cmd.ExecuteNonQuery();
        }
        SetCurrentSchemaVersion();
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

    public string? GetSetting(string key) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE `key`=@k";
        cmd.Parameters.AddWithValue("@k", key);
        var r = cmd.ExecuteScalar();
        return r != null && r != DBNull.Value ? r.ToString() : null;
    }

    public void SaveSetting(string key, string? value) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = _dialect is MariaDbDialect
            ? "INSERT INTO settings(`key`,value) VALUES(@k,@v) ON DUPLICATE KEY UPDATE value=@v"
            : "INSERT INTO settings(`key`,value) VALUES(@k,@v) ON CONFLICT(`key`) DO UPDATE SET value=excluded.value";
        cmd.Parameters.AddWithValue("@k", key);
        cmd.Parameters.AddWithValue("@v", (object?)value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    // Avatar methods
    public void SaveUserAvatar(string username, string? base64Data) {
        username = NormalizeUsernameForLookup(username);
        var user = username.Length == 0 ? null : FindUserExact(username);
        if (user is not { IsActive: true })
            throw new InvalidOperationException($"Benutzer '{username}' wurde nicht gefunden oder ist deaktiviert.");
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "UPDATE users SET avatar_data=@d WHERE id=@id";
        cmd.Parameters.AddWithValue("@d", (object?)base64Data ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", user.Id);
        cmd.ExecuteNonQuery();
    }

    public string? GetUserAvatar(string username) {
        username = NormalizeUsernameForLookup(username);
        var user = username.Length == 0 ? null : FindUserExact(username);
        if (user is not { IsActive: true })
            return null;
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT avatar_data FROM users WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", user.Id);
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
                var d = ParseDate(r.GetInvariantText(0, DbTextKind.Date));
                if (d == null) continue;
                double a = r.GetDouble(1);
                string it = r.IsDBNull(2) ? "" : r.GetString(2).ToLower().Trim();
                string notes = r.IsDBNull(3) ? "" : r.GetString(3);
                bool isFixkosten = notes.StartsWith("FIXKOSTEN:");
                bool isSteuer = notes.StartsWith("STEUER:");
                if (!includeRecurring || string.IsNullOrEmpty(it) || it == "once" || it == "einmalig") {
                    evs.Add((d.Value, a));
                } else {
                    int stepM = 0;
                    if (it == "monthly" || it == "monatlich") stepM = 1;
                    else if (it == "quarterly" || it == "vierteljährlich") stepM = 3;
                    else if (it == "semiannual" || it == "semi-annually" || it == "halbjahr" || it == "halbjährlich") stepM = 6;
                    else if (it == "yearly" || it == "jährlich") stepM = 12;
                    else if (isFixkosten) stepM = 1;
                    else if (isSteuer) stepM = 12;
                    int stepD = it == "daily" || it == "täglich"
                        ? 1
                        : it == "biweekly"
                            ? 14
                            : it == "weekly" || it == "wöchentlich"
                                ? 7
                                : 0;
                    var end = AddMonthsClamped(DateTime.Today, horizonMonths);
                    end = new DateTime(end.Year, end.Month, DateTime.DaysInMonth(end.Year, end.Month));
                    if (stepM == 0 && stepD == 0)
                        evs.Add((d.Value, a));
                    else
                        foreach (var occurrence in EnumerateRecurrenceDates(
                                     d.Value, DateTime.Today, end, stepM, stepD))
                            evs.Add((occurrence, a));
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
                        var due = ParseDate(r.GetInvariantText(1, DbTextKind.Date));
                        double totalAmt = r.IsDBNull(2) ? 0 : r.GetDouble(2);
                        double paidAmt = r.IsDBNull(4) ? 0 : r.GetDouble(4);
                        double remaining = totalAmt - paidAmt;
                        if (due.HasValue && remaining != 0)
                        {
                            var forecastDate = due.Value.Date < DateTime.Today ? DateTime.Today : due.Value;
                            evs.Add((forecastDate, remaining));
                        }
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
                var de = ParseDate(r.GetInvariantText(0, DbTextKind.Date));
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
            if (e.d.Date < today) continue;
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
        return FindNextOfferNumber(prefix, transaction: null);
    }

    string FindNextOfferNumber(string prefix, DbTransaction? transaction) {
        using var cmd = Conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT offer_number FROM offers WHERE offer_number LIKE @p";
        cmd.Parameters.AddWithValue("@p", prefix + "%");
        long maximum = 0;
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) {
            var value = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim();
            if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            var suffix = value[prefix.Length..];
            if (long.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence)
                && sequence > maximum)
                maximum = sequence;
        }

        return prefix + checked(maximum + 1).ToString("D4", CultureInfo.InvariantCulture);
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
        cmd.CommandText += " ORDER BY date DESC, id DESC";
        using var r = cmd.ExecuteReader();
        while (r.Read()) {
            list.Add(new Transaction {
                Id = r.GetInt64(0),
                Date = r.GetInvariantText(1, DbTextKind.Date) ?? "",
                Description = r.IsDBNull(2) ? "" : r.GetString(2),
                Amount = r.GetDouble(3),
                CategoryId = r.IsDBNull(4) ? null : r.GetInt64(4),
                PersonId = r.IsDBNull(5) ? null : r.GetInt64(5),
                Interval = r.IsDBNull(6) ? "" : r.GetString(6),
                Notes = r.IsDBNull(7) ? "" : r.GetString(7),
                CreatedAt = r.GetInvariantText(8) ?? "",
                UpdatedAt = r.GetInvariantText(9) ?? ""
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

    // Bank data is kept separate from manually entered transactions. Importing
    // an account statement therefore never changes forecasts or invoice state
    // without a later, explicit reconciliation step.
    public List<BankAccount> GetBankAccounts() {
        var list = new List<BankAccount>();
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"SELECT id,source_provider,external_account_id,account_name,iban_masked,
            currency,balance,last_sync,created_at,updated_at
            FROM bank_accounts ORDER BY account_name,external_account_id";
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) {
            list.Add(new BankAccount {
                Id = reader.GetInt64(0),
                SourceProvider = reader.IsDBNull(1) ? "" : reader.GetString(1),
                ExternalAccountId = reader.IsDBNull(2) ? "" : reader.GetString(2),
                AccountName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                IbanMasked = reader.IsDBNull(4) ? "" : reader.GetString(4),
                Currency = reader.IsDBNull(5) ? "EUR" : reader.GetString(5),
                Balance = reader.IsDBNull(6)
                    ? null
                    : Convert.ToDouble(reader.GetValue(6), CultureInfo.InvariantCulture),
                LastSync = reader.GetInvariantText(7) ?? "",
                CreatedAt = reader.GetInvariantText(8),
                UpdatedAt = reader.GetInvariantText(9)
            });
        }
        return list;
    }

    public string GetDatabaseInstanceId() {
        var value = GetSetting(DatabaseInstanceIdSettingKey)?.Trim();
        if (string.IsNullOrWhiteSpace(value)) {
            EnsureDatabaseInstanceId();
            value = GetSetting(DatabaseInstanceIdSettingKey)?.Trim();
        }

        if (value == null || !Guid.TryParseExact(value, "N", out var parsed))
            throw new InvalidOperationException("Die Datenbankkennung ist ungültig.");

        return parsed.ToString("N");
    }

    private void EnsureDatabaseInstanceId() {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = _dialect is MariaDbDialect
            ? "INSERT IGNORE INTO settings(`key`,value) VALUES(@k,@v)"
            : "INSERT OR IGNORE INTO settings(`key`,value) VALUES(@k,@v)";
        cmd.Parameters.AddWithValue("@k", DatabaseInstanceIdSettingKey);
        cmd.Parameters.AddWithValue("@v", Guid.NewGuid().ToString("N"));
        cmd.ExecuteNonQuery();
    }

    public List<BankTransaction> GetBankTransactions(long? bankAccountId = null) {
        var list = new List<BankTransaction>();
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"SELECT bt.id,bt.bank_account_id,bt.source_provider,bt.source_external_id,
            bt.entry_date,bt.value_date,bt.amount,bt.currency,bt.purpose,bt.payee,bt.status,
            bt.fixed_cost_transaction_id,ba.account_name,bt.created_at,bt.updated_at
            FROM bank_transactions bt
            INNER JOIN bank_accounts ba ON ba.id=bt.bank_account_id";
        if (bankAccountId != null) {
            cmd.CommandText += " WHERE bt.bank_account_id=@accountId";
            cmd.Parameters.AddWithValue("@accountId", bankAccountId.Value);
        }
        cmd.CommandText += " ORDER BY bt.entry_date DESC,bt.id DESC";

        using var reader = cmd.ExecuteReader();
        while (reader.Read()) {
            list.Add(new BankTransaction {
                Id = reader.GetInt64(0),
                BankAccountId = reader.GetInt64(1),
                SourceProvider = reader.IsDBNull(2) ? "" : reader.GetString(2),
                SourceExternalId = reader.IsDBNull(3) ? "" : reader.GetString(3),
                EntryDate = reader.GetInvariantText(4, DbTextKind.Date) ?? "",
                ValueDate = reader.GetInvariantText(5, DbTextKind.Date) ?? "",
                Amount = reader.IsDBNull(6)
                    ? 0
                    : Convert.ToDouble(reader.GetValue(6), CultureInfo.InvariantCulture),
                Currency = reader.IsDBNull(7) ? "EUR" : reader.GetString(7),
                Purpose = reader.IsDBNull(8) ? "" : reader.GetString(8),
                Payee = reader.IsDBNull(9) ? "" : reader.GetString(9),
                Status = reader.IsDBNull(10) ? "booked" : reader.GetString(10),
                FixedCostTransactionId = reader.IsDBNull(11) ? null : reader.GetInt64(11),
                AccountName = reader.IsDBNull(12) ? "" : reader.GetString(12),
                CreatedAt = reader.GetInvariantText(13),
                UpdatedAt = reader.GetInvariantText(14),
                IsSelected = true
            });
        }
        return list;
    }

    /// <summary>
    /// Converts one imported debit into a recurring-cost transaction exactly once.
    /// Repeating the same request returns the already linked transaction.
    /// </summary>
    public Transaction CreateFixedCostFromBankTransaction(
        string provider,
        string externalAccountId,
        string externalTransactionId,
        string interval,
        string description,
        long? categoryId) {
        var normalizedProvider = RequireBankIdentifier(provider, nameof(provider), 32).ToLowerInvariant();
        var normalizedAccountId = RequireBankIdentifier(
            externalAccountId, nameof(externalAccountId), 191);
        var normalizedTransactionId = RequireBankIdentifier(
            externalTransactionId, nameof(externalTransactionId), 191);
        var normalizedInterval = NormalizeFixedCostInterval(interval);

        if (categoryId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(categoryId), "Die Kategorie-ID muss positiv sein.");

        using var tx = BeginWriteTransaction();
        try {
            if (categoryId.HasValue) {
                using var category = Conn.CreateCommand();
                category.Transaction = tx;
                category.CommandText = "SELECT COUNT(*) FROM categories WHERE id=@id";
                category.Parameters.AddWithValue("@id", categoryId.Value);
                if (Convert.ToInt64(category.ExecuteScalar(), CultureInfo.InvariantCulture) != 1)
                    throw new InvalidOperationException("Die ausgewählte Fixkosten-Kategorie existiert nicht mehr.");
            }

            BankFixedCostSource source;
            using (var select = Conn.CreateCommand()) {
                select.Transaction = tx;
                select.CommandText = @"SELECT bt.id,bt.entry_date,bt.value_date,bt.amount,bt.currency,bt.purpose,bt.payee,
                        bt.fixed_cost_transaction_id
                    FROM bank_transactions bt
                    INNER JOIN bank_accounts ba ON ba.id=bt.bank_account_id
                    WHERE ba.source_provider=@provider
                      AND ba.external_account_id=@externalAccountId
                      AND bt.source_external_id=@externalTransactionId"
                    + (_dialect is MariaDbDialect ? " FOR UPDATE" : "");
                select.Parameters.AddWithValue("@provider", normalizedProvider);
                select.Parameters.AddWithValue("@externalAccountId", normalizedAccountId);
                select.Parameters.AddWithValue("@externalTransactionId", normalizedTransactionId);
                using var reader = select.ExecuteReader();
                if (!reader.Read())
                    throw new InvalidOperationException("Die ausgewählte Bankbewegung wurde nicht gefunden.");

                source = new BankFixedCostSource(
                    reader.GetInt64(0),
                    reader.GetInvariantText(1, DbTextKind.Date) ?? "",
                    reader.GetInvariantText(2, DbTextKind.Date) ?? "",
                    reader.IsDBNull(3)
                        ? 0
                        : Convert.ToDouble(reader.GetValue(3), CultureInfo.InvariantCulture),
                    reader.IsDBNull(4) ? "" : reader.GetString(4),
                    reader.IsDBNull(5) ? "" : reader.GetString(5),
                    reader.IsDBNull(6) ? "" : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetInt64(7));
            }

            if (source.FixedCostTransactionId.HasValue) {
                var existing = ReadTransaction(source.FixedCostTransactionId.Value, tx)
                    ?? throw new InvalidOperationException(
                        "Die Bankbewegung verweist auf einen nicht mehr vorhandenen Fixkosten-Eintrag.");
                tx.Commit();
                return existing;
            }

            if (!double.IsFinite(source.Amount) || source.Amount >= 0)
                throw new InvalidOperationException(
                    "Nur eine Bankabbuchung mit einem negativen Betrag kann als Fixkosten übernommen werden.");
            if (!string.Equals(source.Currency.Trim(), "EUR", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Nur EUR-Bankbewegungen können als Fixkosten übernommen werden.");

            var transactionDescription = NormalizeBankText(description);
            if (string.IsNullOrWhiteSpace(transactionDescription))
                transactionDescription = !string.IsNullOrWhiteSpace(source.Purpose)
                    ? source.Purpose
                    : !string.IsNullOrWhiteSpace(source.Payee)
                        ? source.Payee
                        : $"Bankabbuchung {normalizedTransactionId}";
            if (transactionDescription.Length > 10_000)
                throw new ArgumentException("Die Fixkosten-Beschreibung darf höchstens 10.000 Zeichen enthalten.", nameof(description));

            var created = new Transaction {
                Date = SelectBankFixedCostDate(source.ValueDate, source.EntryDate),
                Description = transactionDescription,
                // Expenses are negative throughout the Transaction/forecast model.
                Amount = source.Amount,
                CategoryId = categoryId,
                Interval = normalizedInterval,
                Notes = "FIXKOSTEN:Bankimport"
            };

            using (var insert = Conn.CreateCommand()) {
                insert.Transaction = tx;
                insert.CommandText = @"INSERT INTO transactions(
                        date,description,amount,category_id,person_id,`interval`,notes,created_at,updated_at)
                    VALUES(@date,@description,@amount,@categoryId,NULL,@interval,@notes,
                        CURRENT_TIMESTAMP,CURRENT_TIMESTAMP)";
                insert.Parameters.AddWithValue("@date", created.Date);
                insert.Parameters.AddWithValue("@description", created.Description);
                insert.Parameters.AddWithValue("@amount", created.Amount);
                insert.Parameters.AddWithValue("@categoryId", (object?)created.CategoryId ?? DBNull.Value);
                insert.Parameters.AddWithValue("@interval", created.Interval);
                insert.Parameters.AddWithValue("@notes", created.Notes);
                insert.ExecuteNonQuery();
                created.Id = LastInsertId(tx);
            }

            using (var link = Conn.CreateCommand()) {
                link.Transaction = tx;
                link.CommandText = @"UPDATE bank_transactions
                    SET fixed_cost_transaction_id=@transactionId,updated_at=CURRENT_TIMESTAMP
                    WHERE id=@bankTransactionId AND fixed_cost_transaction_id IS NULL";
                link.Parameters.AddWithValue("@transactionId", created.Id);
                link.Parameters.AddWithValue("@bankTransactionId", source.Id);
                if (link.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException(
                        "Die Bankbewegung wurde inzwischen bereits als Fixkosten übernommen.");
            }

            var stored = ReadTransaction(created.Id, tx)
                ?? throw new InvalidOperationException("Der Fixkosten-Eintrag konnte nicht wiedergefunden werden.");
            tx.Commit();
            return stored;
        }
        catch {
            TryRollback(tx);
            throw;
        }
    }

    private Transaction? ReadTransaction(long id, DbTransaction tx) {
        using var cmd = Conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"SELECT id,date,description,amount,category_id,person_id,`interval`,notes,
                created_at,updated_at
            FROM transactions WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;
        return new Transaction {
            Id = reader.GetInt64(0),
            Date = reader.GetInvariantText(1, DbTextKind.Date) ?? "",
            Description = reader.IsDBNull(2) ? "" : reader.GetString(2),
            Amount = reader.IsDBNull(3)
                ? 0
                : Convert.ToDouble(reader.GetValue(3), CultureInfo.InvariantCulture),
            CategoryId = reader.IsDBNull(4) ? null : reader.GetInt64(4),
            PersonId = reader.IsDBNull(5) ? null : reader.GetInt64(5),
            Interval = reader.IsDBNull(6) ? "" : reader.GetString(6),
            Notes = reader.IsDBNull(7) ? "" : reader.GetString(7),
            CreatedAt = reader.GetInvariantText(8),
            UpdatedAt = reader.GetInvariantText(9)
        };
    }

    private static string NormalizeFixedCostInterval(string? value) {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        return normalized switch {
            "once" or "einmalig" => "einmalig",
            "monthly" or "monatlich" => "monatlich",
            "quarterly" or "vierteljährlich" => "vierteljährlich",
            "semiannual" or "semi-annually" or "halbjahr" or "halbjährlich" => "halbjährlich",
            "yearly" or "jährlich" => "jährlich",
            _ => throw new ArgumentException(
                "Das Fixkosten-Intervall muss einmalig, monatlich, vierteljährlich, halbjährlich oder jährlich sein.",
                nameof(value))
        };
    }

    public HashSet<string> GetBankTransactionSourceIds(
        string sourceProvider,
        string externalAccountId) {
        var provider = RequireBankIdentifier(sourceProvider, nameof(sourceProvider), 32).ToLowerInvariant();
        var accountExternalId = RequireBankIdentifier(
            externalAccountId,
            nameof(externalAccountId),
            191);
        var result = new HashSet<string>(StringComparer.Ordinal);

        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"SELECT bt.source_external_id
            FROM bank_transactions bt
            INNER JOIN bank_accounts ba ON ba.id=bt.bank_account_id
            WHERE ba.source_provider=@provider AND ba.external_account_id=@externalAccountId";
        cmd.Parameters.AddWithValue("@provider", provider);
        cmd.Parameters.AddWithValue("@externalAccountId", accountExternalId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) {
            if (!reader.IsDBNull(0))
                result.Add(reader.GetString(0));
        }

        return result;
    }

    public Task<HashSet<string>> GetBankTransactionSourceIdsAsync(
        string sourceProvider,
        string externalAccountId,
        CancellationToken cancellationToken = default) {
        var config = _activeConfig?.Clone()
            ?? throw new InvalidOperationException("Database not open");
        var providerSnapshot = sourceProvider;
        var accountIdSnapshot = externalAccountId;

        return Task.Run(() => {
            cancellationToken.ThrowIfCancellationRequested();
            using var worker = new Database();
            worker.OpenSecondary(config);
            cancellationToken.ThrowIfCancellationRequested();
            return worker.GetBankTransactionSourceIds(providerSnapshot, accountIdSnapshot);
        }, cancellationToken);
    }

    /// <summary>
    /// Atomically stores the account snapshot and every selected movement.
    /// Re-importing the same provider transaction updates changed data or skips
    /// an identical row; it can never create a second row for the same account.
    /// </summary>
    public BankImportResult ImportBankTransactions(
        BankAccount account,
        IEnumerable<BankTransaction> transactions) {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(transactions);

        var normalizedAccount = NormalizeBankAccount(account);
        var selected = transactions.Where(item => item?.IsSelected == true).ToList();
        var normalizedItems = new List<NormalizedBankTransaction>(selected.Count);
        var uniqueItems = new Dictionary<string, NormalizedBankTransaction>(StringComparer.Ordinal);
        var duplicateCount = 0;

        // Validate the complete batch before making any database change.
        foreach (var item in selected) {
            var normalized = NormalizeBankTransaction(item!, normalizedAccount.Currency);
            if (uniqueItems.TryGetValue(normalized.SourceExternalId, out var duplicate)) {
                if (duplicate != normalized) {
                    throw new InvalidOperationException(
                        $"Die Bankbewegung {normalized.SourceExternalId} ist im Import mehrfach mit unterschiedlichen Daten enthalten.");
                }
                duplicateCount++;
                continue;
            }

            uniqueItems.Add(normalized.SourceExternalId, normalized);
            normalizedItems.Add(normalized);
        }

        var result = new BankImportResult {
            Selected = selected.Count,
            Skipped = duplicateCount
        };

        using var tx = BeginWriteTransaction();
        try {
            var accountId = UpsertBankAccount(normalizedAccount, tx);
            result.BankAccountId = accountId;

            foreach (var item in normalizedItems) {
                var existing = ReadNormalizedBankTransaction(accountId, item.SourceExternalId, tx);
                if (existing == item) {
                    result.Skipped++;
                    continue;
                }

                if (existing == null) {
                    if (InsertBankTransaction(accountId, normalizedAccount.SourceProvider, item, tx))
                        result.Added++;
                    else {
                        // A concurrent MariaDB client may have inserted the key
                        // after our read. Updating it preserves idempotence.
                        UpdateBankTransaction(accountId, normalizedAccount.SourceProvider, item, tx);
                        result.Updated++;
                    }
                }
                else {
                    UpdateBankTransaction(accountId, normalizedAccount.SourceProvider, item, tx);
                    result.Updated++;
                }
            }

            tx.Commit();

            account.Id = accountId;
            account.SourceProvider = normalizedAccount.SourceProvider;
            account.ExternalAccountId = normalizedAccount.ExternalAccountId;
            account.AccountName = normalizedAccount.AccountName;
            account.IbanMasked = normalizedAccount.IbanMasked;
            account.Currency = normalizedAccount.Currency;
            account.Balance = normalizedAccount.Balance;
            account.LastSync = normalizedAccount.LastSync;
            return result;
        }
        catch {
            TryRollback(tx);
            throw;
        }
    }

    public Task<BankImportResult> ImportBankTransactionsAsync(
        BankAccount account,
        IEnumerable<BankTransaction> transactions,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(transactions);

        var config = _activeConfig?.Clone()
            ?? throw new InvalidOperationException("Database not open");
        var accountSnapshot = new BankAccount {
            SourceProvider = account.SourceProvider,
            ExternalAccountId = account.ExternalAccountId,
            AccountName = account.AccountName,
            IbanMasked = account.IbanMasked,
            Currency = account.Currency,
            Balance = account.Balance,
            LastSync = account.LastSync
        };
        var transactionSnapshots = transactions.Select(item => new BankTransaction {
            SourceProvider = item.SourceProvider,
            SourceExternalId = item.SourceExternalId,
            EntryDate = item.EntryDate,
            ValueDate = item.ValueDate,
            Amount = item.Amount,
            Currency = item.Currency,
            Purpose = item.Purpose,
            Payee = item.Payee,
            Status = item.Status,
            IsSelected = item.IsSelected
        }).ToList();

        return Task.Run(() => {
            cancellationToken.ThrowIfCancellationRequested();
            using var worker = new Database();
            worker.OpenSecondary(config);
            cancellationToken.ThrowIfCancellationRequested();
            return worker.ImportBankTransactions(accountSnapshot, transactionSnapshots);
        }, cancellationToken);
    }

    long UpsertBankAccount(NormalizedBankAccount account, DbTransaction tx) {
        using (var upsert = Conn.CreateCommand()) {
            upsert.Transaction = tx;
            upsert.CommandText = _dialect is MariaDbDialect
                ? @"INSERT INTO bank_accounts(source_provider,external_account_id,account_name,iban_masked,
                        currency,balance,last_sync,created_at,updated_at)
                    VALUES(@provider,@externalId,@name,@iban,@currency,@balance,@lastSync,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP)
                    ON DUPLICATE KEY UPDATE account_name=VALUES(account_name),iban_masked=VALUES(iban_masked),
                        currency=VALUES(currency),balance=COALESCE(VALUES(balance),balance),
                        last_sync=VALUES(last_sync),updated_at=CURRENT_TIMESTAMP"
                : @"INSERT INTO bank_accounts(source_provider,external_account_id,account_name,iban_masked,
                        currency,balance,last_sync,created_at,updated_at)
                    VALUES(@provider,@externalId,@name,@iban,@currency,@balance,@lastSync,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP)
                    ON CONFLICT(source_provider,external_account_id) DO UPDATE SET
                        account_name=excluded.account_name,iban_masked=excluded.iban_masked,
                        currency=excluded.currency,balance=COALESCE(excluded.balance,bank_accounts.balance),
                        last_sync=excluded.last_sync,updated_at=CURRENT_TIMESTAMP";
            AddBankAccountParameters(upsert, account);
            upsert.ExecuteNonQuery();
        }

        using var select = Conn.CreateCommand();
        select.Transaction = tx;
        select.CommandText = @"SELECT id FROM bank_accounts
            WHERE source_provider=@provider AND external_account_id=@externalId";
        select.Parameters.AddWithValue("@provider", account.SourceProvider);
        select.Parameters.AddWithValue("@externalId", account.ExternalAccountId);
        return Convert.ToInt64(select.ExecuteScalar()
            ?? throw new InvalidOperationException("Das importierte Bankkonto konnte nicht wiedergefunden werden."),
            CultureInfo.InvariantCulture);
    }

    static void AddBankAccountParameters(DbCommand cmd, NormalizedBankAccount account) {
        cmd.Parameters.AddWithValue("@provider", account.SourceProvider);
        cmd.Parameters.AddWithValue("@externalId", account.ExternalAccountId);
        cmd.Parameters.AddWithValue("@name", account.AccountName);
        cmd.Parameters.AddWithValue("@iban", account.IbanMasked);
        cmd.Parameters.AddWithValue("@currency", account.Currency);
        cmd.Parameters.AddWithValue("@balance", (object?)account.Balance ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@lastSync", account.LastSync);
    }

    NormalizedBankTransaction? ReadNormalizedBankTransaction(
        long accountId,
        string sourceExternalId,
        DbTransaction tx) {
        using var cmd = Conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"SELECT source_external_id,entry_date,value_date,amount,currency,purpose,payee,status
            FROM bank_transactions WHERE bank_account_id=@accountId AND source_external_id=@externalId";
        cmd.Parameters.AddWithValue("@accountId", accountId);
        cmd.Parameters.AddWithValue("@externalId", sourceExternalId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return new NormalizedBankTransaction(
            reader.GetString(0),
            reader.GetInvariantText(1, DbTextKind.Date) ?? "",
            reader.GetInvariantText(2, DbTextKind.Date) ?? "",
            Convert.ToDouble(reader.GetValue(3), CultureInfo.InvariantCulture),
            reader.GetString(4),
            reader.IsDBNull(5) ? "" : reader.GetString(5),
            reader.IsDBNull(6) ? "" : reader.GetString(6),
            reader.GetString(7));
    }

    bool InsertBankTransaction(
        long accountId,
        string sourceProvider,
        NormalizedBankTransaction item,
        DbTransaction tx) {
        using var cmd = Conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = _dialect.InsertOrIgnore(@"INSERT OR IGNORE INTO bank_transactions(
            bank_account_id,source_provider,source_external_id,entry_date,value_date,amount,
            currency,purpose,payee,status,created_at,updated_at)
            VALUES(@accountId,@provider,@externalId,@entryDate,@valueDate,@amount,
                @currency,@purpose,@payee,@status,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP)");
        AddBankTransactionParameters(cmd, accountId, sourceProvider, item);
        return cmd.ExecuteNonQuery() > 0;
    }

    void UpdateBankTransaction(
        long accountId,
        string sourceProvider,
        NormalizedBankTransaction item,
        DbTransaction tx) {
        using var cmd = Conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"UPDATE bank_transactions SET source_provider=@provider,entry_date=@entryDate,
            value_date=@valueDate,amount=@amount,currency=@currency,purpose=@purpose,payee=@payee,
            status=@status,updated_at=CURRENT_TIMESTAMP
            WHERE bank_account_id=@accountId AND source_external_id=@externalId";
        AddBankTransactionParameters(cmd, accountId, sourceProvider, item);
        if (cmd.ExecuteNonQuery() != 1)
            throw new InvalidOperationException($"Die Bankbewegung {item.SourceExternalId} konnte nicht aktualisiert werden.");
    }

    static void AddBankTransactionParameters(
        DbCommand cmd,
        long accountId,
        string sourceProvider,
        NormalizedBankTransaction item) {
        cmd.Parameters.AddWithValue("@accountId", accountId);
        cmd.Parameters.AddWithValue("@provider", sourceProvider);
        cmd.Parameters.AddWithValue("@externalId", item.SourceExternalId);
        cmd.Parameters.AddWithValue("@entryDate", item.EntryDate);
        cmd.Parameters.AddWithValue("@valueDate", string.IsNullOrEmpty(item.ValueDate)
            ? DBNull.Value
            : item.ValueDate);
        cmd.Parameters.AddWithValue("@amount", item.Amount);
        cmd.Parameters.AddWithValue("@currency", item.Currency);
        cmd.Parameters.AddWithValue("@purpose", item.Purpose);
        cmd.Parameters.AddWithValue("@payee", item.Payee);
        cmd.Parameters.AddWithValue("@status", item.Status);
    }

    static NormalizedBankAccount NormalizeBankAccount(BankAccount account) {
        var provider = RequireBankIdentifier(account.SourceProvider, nameof(account.SourceProvider), 32)
            .ToLowerInvariant();
        var externalId = RequireBankIdentifier(account.ExternalAccountId, nameof(account.ExternalAccountId), 191);
        var currency = NormalizeBankCurrency(account.Currency, "EUR");
        if (account.Balance is { } balance && !double.IsFinite(balance))
            throw new ArgumentOutOfRangeException(nameof(account), "Der Kontostand muss eine endliche Zahl sein.");

        return new NormalizedBankAccount(
            provider,
            externalId,
            NormalizeBankText(account.AccountName),
            NormalizeMaskedIban(account.IbanMasked),
            currency,
            account.Balance is 0d ? 0d : account.Balance,
            NormalizeBankTimestamp(account.LastSync));
    }

    static NormalizedBankTransaction NormalizeBankTransaction(BankTransaction item, string accountCurrency) {
        var externalId = RequireBankIdentifier(item.SourceExternalId, nameof(item.SourceExternalId), 191);
        if (!double.IsFinite(item.Amount))
            throw new ArgumentOutOfRangeException(nameof(item),
                $"Der Betrag der Bankbewegung {externalId} muss eine endliche Zahl sein.");

        var status = NormalizeBankText(item.Status).ToLowerInvariant();
        if (string.IsNullOrEmpty(status)) status = "booked";
        if (status.Length > 50)
            throw new ArgumentException($"Der Status der Bankbewegung {externalId} ist zu lang.", nameof(item));

        return new NormalizedBankTransaction(
            externalId,
            NormalizeBankDate(item.EntryDate, required: true, $"Buchungsdatum der Bankbewegung {externalId}"),
            NormalizeBankDate(item.ValueDate, required: false, $"Wertstellungsdatum der Bankbewegung {externalId}"),
            item.Amount == 0d ? 0d : item.Amount,
            NormalizeBankCurrency(item.Currency, accountCurrency),
            NormalizeBankText(item.Purpose),
            NormalizeBankText(item.Payee),
            status);
    }

    static string RequireBankIdentifier(string? value, string parameterName, int maximumLength) {
        var normalized = (value ?? "").Trim();
        if (normalized.Length == 0)
            throw new ArgumentException("Eine stabile externe ID ist für einen sicheren Bankimport erforderlich.", parameterName);
        if (normalized.Length > maximumLength)
            throw new ArgumentException($"Die externe ID darf höchstens {maximumLength} Zeichen enthalten.", parameterName);
        return normalized;
    }

    static string NormalizeBankCurrency(string? value, string fallback) {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToUpperInvariant();
        if (normalized.Length != 3 || normalized.Any(c => c is < 'A' or > 'Z'))
            throw new ArgumentException($"'{normalized}' ist kein gültiger dreistelliger Währungscode.");
        return normalized;
    }

    static string NormalizeBankDate(string? value, bool required, string fieldName) {
        var normalized = (value ?? "").Trim();
        if (normalized.Length == 0) {
            if (required) throw new ArgumentException($"{fieldName} fehlt.");
            return "";
        }

        string[] formats = ["yyyy-MM-dd", "dd.MM.yyyy", "yyyyMMdd"];
        if (DateTime.TryParseExact(normalized, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var date))
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (DateTimeOffset.TryParse(normalized, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var timestamp))
            return timestamp.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        throw new ArgumentException($"{fieldName} '{normalized}' ist ungültig.");
    }

    internal static string SelectBankFixedCostDate(string? valueDate, string? entryDate) =>
        NormalizeBankDate(
            string.IsNullOrWhiteSpace(valueDate) ? entryDate : valueDate,
            required: true,
            "Wertstellungs- oder Buchungsdatum der Bankbewegung");

    static string NormalizeBankTimestamp(string? value) {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        if (!DateTimeOffset.TryParse(value.Trim(), CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var timestamp))
            throw new ArgumentException($"Der Synchronisierungszeitpunkt '{value}' ist ungültig.");

        return timestamp.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    static string NormalizeBankText(string? value) =>
        (value ?? "").Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n').Trim();

    static string NormalizeMaskedIban(string? value) {
        var compact = new string((value ?? "")
            .Where(c => char.IsLetterOrDigit(c) || c == '*')
            .Select(char.ToUpperInvariant)
            .ToArray());
        if (compact.Length == 0) return "";
        if (compact.Length > 100)
            throw new ArgumentException("Die maskierte IBAN darf höchstens 100 Zeichen enthalten.", nameof(value));
        if (compact.Contains('*')) return compact;

        var suffix = compact.Length <= 4 ? compact : compact[^4..];
        var country = compact.Length >= 2 && char.IsLetter(compact[0]) && char.IsLetter(compact[1])
            ? compact[..2] + " "
            : "";
        return $"{country}**** {suffix}";
    }

    sealed record NormalizedBankAccount(
        string SourceProvider,
        string ExternalAccountId,
        string AccountName,
        string IbanMasked,
        string Currency,
        double? Balance,
        string LastSync);

    sealed record NormalizedBankTransaction(
        string SourceExternalId,
        string EntryDate,
        string ValueDate,
        double Amount,
        string Currency,
        string Purpose,
        string Payee,
        string Status);

    sealed record BankFixedCostSource(
        long Id,
        string EntryDate,
        string ValueDate,
        double Amount,
        string Currency,
        string Purpose,
        string Payee,
        long? FixedCostTransactionId);

    // CRUD: Invoices
    public List<Invoice> GetInvoices() {
        var list = new List<Invoice>();
        using (var cmd = Conn.CreateCommand()) {
            cmd.CommandText = @"SELECT id,invoice_number,issue_date,due_date,customer,amount,
                net_amount,vat_amount,vat_rate,description,paid_date,paid_amount,status,pdf_path,created_at,customer_id
                FROM invoices";
            using var r = cmd.ExecuteReader();
            while (r.Read()) {
                list.Add(new Invoice {
                    Id = r.GetInt64(0),
                    InvoiceNumber = r.IsDBNull(1) ? "" : r.GetString(1),
                    IssueDate = r.GetInvariantText(2, DbTextKind.Date) ?? "",
                    DueDate = r.GetInvariantText(3, DbTextKind.Date) ?? "",
                    Customer = r.IsDBNull(4) ? "" : r.GetString(4),
                    Amount = r.IsDBNull(5) ? 0 : r.GetDouble(5),
                    NetAmount = r.IsDBNull(6) ? 0 : r.GetDouble(6),
                    VatAmount = r.IsDBNull(7) ? 0 : r.GetDouble(7),
                    VatRate = r.IsDBNull(8) ? 19 : r.GetDouble(8),
                    Description = r.IsDBNull(9) ? "" : r.GetString(9),
                    PaidDate = r.GetInvariantText(10, DbTextKind.Date) ?? "",
                    PaidAmount = r.IsDBNull(11) ? 0 : r.GetDouble(11),
                    Status = r.IsDBNull(12) ? "" : r.GetString(12),
                    PdfPath = r.IsDBNull(13) ? "" : r.GetString(13),
                    CreatedAt = r.GetInvariantText(14) ?? "",
                    CustomerId = r.IsDBNull(15) ? null : r.GetInt64(15)
                });
            }
        }

        foreach (var invoice in list)
            invoice.Content = LoadDocumentContent(offerId: null, invoiceId: invoice.Id);

        return list;
    }

    public void AddInvoice(Invoice i) {
        NormalizeInvoiceCustomerReference(i);
        i.Content ??= new DocumentContent();
        var originalInvoiceId = i.Id;
        var identity = CaptureDocumentIdentity(i.Content);
        using var tx = BeginWriteTransaction();
        try {
            using var cmd = Conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO invoices(invoice_number,issue_date,due_date,customer,customer_id,amount,net_amount,vat_amount,vat_rate,description,paid_date,paid_amount,status,pdf_path,created_at)
                VALUES(@number,@issue,@due,@cust,@customerId,@amt,@net,@vat,@vat_rate,@desc,@paid_d,@paid_a,@status,@pdf,CURRENT_TIMESTAMP)";
            AddInvoiceParameters(cmd, i);
            cmd.ExecuteNonQuery();
            i.Id = LastInsertId(tx);
            SaveDocumentContent(i.Content, offerId: null, invoiceId: i.Id, tx, forceInsert: true);
            tx.Commit();
        }
        catch {
            TryRollback(tx);
            i.Id = originalInvoiceId;
            RestoreDocumentIdentity(i.Content, identity);
            throw;
        }
    }

    public void UpdateInvoice(Invoice i) {
        if (i.Id <= 0)
            throw new ArgumentOutOfRangeException(nameof(i), "Die Rechnung wurde noch nicht gespeichert.");

        NormalizeInvoiceCustomerReference(i);
        i.Content ??= new DocumentContent();
        var identity = CaptureDocumentIdentity(i.Content);
        using var tx = BeginWriteTransaction();
        try {
            using var cmd = Conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"UPDATE invoices SET invoice_number=@number,issue_date=@issue,due_date=@due,customer=@cust,customer_id=@customerId,amount=@amt,
                net_amount=@net,vat_amount=@vat,vat_rate=@vat_rate,
                description=@desc,paid_date=@paid_d,paid_amount=@paid_a,status=@status,pdf_path=@pdf WHERE id=@id";
            cmd.Parameters.AddWithValue("@id", i.Id);
            AddInvoiceParameters(cmd, i);
            if (cmd.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("Die Rechnung wurde nicht gefunden.");

            SaveDocumentContent(i.Content, offerId: null, invoiceId: i.Id, tx, forceInsert: false);
            tx.Commit();
        }
        catch {
            TryRollback(tx);
            RestoreDocumentIdentity(i.Content, identity);
            throw;
        }
    }

    public void DeleteInvoice(long id) { ExecWithId("DELETE FROM invoices WHERE id=@id", id); }

    // CRUD: Offers
    public List<Offer> GetOffers() => GetOffers(Conn);

    static List<Offer> GetOffers(DbConnection connection) {
        var list = new List<Offer>();
        var offersById = new Dictionary<long, Offer>();
        using (var cmd = connection.CreateCommand()) {
            cmd.CommandText = @"SELECT o.id,o.offer_number,o.offer_date,o.date_expected,o.customer,
                o.amount_before_discount,o.discount_percent,o.amount,o.probability,
                o.description,o.status,o.payment_delay,o.pdf_path,o.created_at,o.project_id,p.project_number,o.customer_id
                FROM offers o LEFT JOIN projects p ON p.id=o.project_id";
            using var r = cmd.ExecuteReader();
            while (r.Read()) {
                var offer = new Offer {
                    Id = r.GetInt64(0),
                    OfferNumber = r.IsDBNull(1) ? "" : r.GetString(1),
                    OfferDate = r.GetInvariantText(2, DbTextKind.Date) ?? "",
                    DateExpected = r.GetInvariantText(3, DbTextKind.Date) ?? "",
                    Customer = r.IsDBNull(4) ? "" : r.GetString(4),
                    AmountBeforeDiscount = r.IsDBNull(5) ? 0 : r.GetDouble(5),
                    DiscountPercent = r.IsDBNull(6) ? 0 : r.GetDouble(6),
                    Amount = r.IsDBNull(7) ? 0 : r.GetDouble(7),
                    Probability = r.IsDBNull(8) ? 0 : r.GetDouble(8),
                    Description = r.IsDBNull(9) ? "" : r.GetString(9),
                    Status = r.IsDBNull(10) ? "" : r.GetString(10),
                    PaymentDelay = r.IsDBNull(11) ? 30 : r.GetInt32(11),
                    PdfPath = r.IsDBNull(12) ? "" : r.GetString(12),
                    CreatedAt = r.GetInvariantText(13) ?? "",
                    ProjectId = r.IsDBNull(14) ? null : r.GetInt64(14),
                    ProjectNumber = r.IsDBNull(15) ? "" : r.GetString(15),
                    CustomerId = r.IsDBNull(16) ? null : r.GetInt64(16)
                };
                list.Add(offer);
                offersById[offer.Id] = offer;
            }
        }

        var contentsById = new Dictionary<long, DocumentContent>();
        using (var cmd = connection.CreateCommand()) {
            cmd.CommandText = @"SELECT offer_id,id,header,pre_text,post_text,internal_note,source_provider,
                source_entity_type,source_external_id,source_snapshot_json,last_imported_at
                FROM document_contents
                WHERE offer_id IS NOT NULL AND invoice_id IS NULL
                ORDER BY id";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) {
                var offerId = reader.GetInt64(0);
                if (!offersById.TryGetValue(offerId, out var offer))
                    continue;

                var content = ReadDocumentContent(reader, 1);
                offer.Content = content;
                contentsById[content.Id] = content;
            }
        }

        using (var cmd = connection.CreateCommand()) {
            cmd.CommandText = @"SELECT li.document_content_id,li.id,li.source_item_id,li.sort_order,
                li.position_number,li.name,li.description,li.quantity,li.unit,li.unit_price,
                li.discount_percent,li.tax_rate,li.net_amount,li.gross_amount,li.is_optional
                FROM document_line_items li
                INNER JOIN document_contents dc ON dc.id=li.document_content_id
                WHERE dc.offer_id IS NOT NULL AND dc.invoice_id IS NULL
                ORDER BY li.document_content_id,li.sort_order,li.id";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) {
                var contentId = reader.GetInt64(0);
                if (contentsById.TryGetValue(contentId, out var content))
                    content.LineItems.Add(ReadDocumentLineItem(reader, 1));
            }
        }

        return list;
    }

    public Task<(List<Offer> Offers, List<Customer> Customers)> GetOffersPageDataAsync(
        CancellationToken cancellationToken = default) {
        var config = _activeConfig?.Clone()
            ?? throw new InvalidOperationException("Database not open");

        return Task.Run(() => {
            cancellationToken.ThrowIfCancellationRequested();
            IDbDialect dialect = new MariaDbDialect();

            using var connection = dialect.CreateConnection(config.ToConnectionString());
            connection.Open();
            dialect.ConfigureConnection(connection);
            cancellationToken.ThrowIfCancellationRequested();

            var offers = GetOffers(connection);
            cancellationToken.ThrowIfCancellationRequested();
            var customers = GetCustomers(connection);
            return (offers, customers);
        }, cancellationToken);
    }

    public void AddOffer(Offer o) {
        NormalizeOfferDiscount(o);
        NormalizeOfferNumber(o);
        NormalizeOfferCustomerReference(o);
        o.Content ??= new DocumentContent();
        var originalOfferId = o.Id;
        var originalOfferNumber = o.OfferNumber;
        var identity = CaptureDocumentIdentity(o.Content);
        using var tx = BeginWriteTransaction();
        try {
            PrepareOfferNumberForInsert(o, tx);
            using var cmd = Conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO offers(offer_number,offer_date,date_expected,customer,customer_id,amount_before_discount,discount_percent,amount,probability,description,status,payment_delay,pdf_path,created_at)
                VALUES(@onum,@odate,@dexp,@cust,@customerId,@before,@discount,@amt,@prob,@desc,@status,@delay,@pdf,CURRENT_TIMESTAMP)";
            AddOfferParameters(cmd, o);
            cmd.ExecuteNonQuery();
            o.Id = LastInsertId(tx);
            SaveDocumentContent(o.Content, offerId: o.Id, invoiceId: null, tx, forceInsert: true);
            tx.Commit();
        }
        catch {
            TryRollback(tx);
            o.Id = originalOfferId;
            o.OfferNumber = originalOfferNumber;
            RestoreDocumentIdentity(o.Content, identity);
            throw;
        }
    }

    public void UpdateOffer(Offer o) {
        if (o.Id <= 0)
            throw new ArgumentOutOfRangeException(nameof(o), "Das Angebot wurde noch nicht gespeichert.");

        NormalizeOfferDiscount(o);
        NormalizeOfferNumber(o);
        NormalizeOfferCustomerReference(o);
        o.Content ??= new DocumentContent();
        var identity = CaptureDocumentIdentity(o.Content);
        using var tx = BeginWriteTransaction();
        try {
            using var cmd = Conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"UPDATE offers SET offer_number=@onum,offer_date=@odate,date_expected=@dexp,customer=@cust,customer_id=@customerId,
                amount_before_discount=@before,discount_percent=@discount,amount=@amt,probability=@prob,
                description=@desc,status=@status,payment_delay=@delay,pdf_path=@pdf WHERE id=@id";
            cmd.Parameters.AddWithValue("@id", o.Id);
            AddOfferParameters(cmd, o);
            if (cmd.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("Das Angebot wurde nicht gefunden.");

            SaveDocumentContent(o.Content, offerId: o.Id, invoiceId: null, tx, forceInsert: false);
            tx.Commit();
        }
        catch {
            TryRollback(tx);
            RestoreDocumentIdentity(o.Content, identity);
            throw;
        }
    }

    static void AddInvoiceParameters(DbCommand cmd, Invoice invoice) {
        cmd.Parameters.AddWithValue("@number", (object?)invoice.InvoiceNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@issue", (object?)invoice.IssueDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@due", (object?)invoice.DueDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cust", (object?)invoice.Customer ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@customerId", (object?)invoice.CustomerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@amt", invoice.Amount);
        cmd.Parameters.AddWithValue("@net", invoice.NetAmount);
        cmd.Parameters.AddWithValue("@vat", invoice.VatAmount);
        cmd.Parameters.AddWithValue("@vat_rate", invoice.VatRate);
        cmd.Parameters.AddWithValue("@desc", (object?)invoice.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@paid_d", (object?)invoice.PaidDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@paid_a", invoice.PaidAmount);
        cmd.Parameters.AddWithValue("@status", (object?)invoice.Status ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pdf", (object?)invoice.PdfPath ?? DBNull.Value);
    }

    static void AddOfferParameters(DbCommand cmd, Offer offer) {
        cmd.Parameters.AddWithValue("@onum", (object?)offer.OfferNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@odate", (object?)offer.OfferDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@dexp", (object?)offer.DateExpected ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cust", (object?)offer.Customer ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@customerId", (object?)offer.CustomerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@before", offer.AmountBeforeDiscount);
        cmd.Parameters.AddWithValue("@discount", offer.DiscountPercent);
        cmd.Parameters.AddWithValue("@amt", offer.Amount);
        cmd.Parameters.AddWithValue("@prob", offer.Probability);
        cmd.Parameters.AddWithValue("@desc", (object?)offer.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@status", (object?)offer.Status ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@delay", offer.PaymentDelay);
        cmd.Parameters.AddWithValue("@pdf", (object?)offer.PdfPath ?? DBNull.Value);
    }

    DocumentContent LoadDocumentContent(long? offerId, long? invoiceId) {
        ValidateDocumentOwner(offerId, invoiceId);
        var content = new DocumentContent();

        using (var cmd = Conn.CreateCommand()) {
            cmd.CommandText = offerId.HasValue
                ? @"SELECT id,header,pre_text,post_text,internal_note,source_provider,source_entity_type,
                    source_external_id,source_snapshot_json,last_imported_at
                    FROM document_contents WHERE offer_id=@ownerId"
                : @"SELECT id,header,pre_text,post_text,internal_note,source_provider,source_entity_type,
                    source_external_id,source_snapshot_json,last_imported_at
                    FROM document_contents WHERE invoice_id=@ownerId";
            cmd.Parameters.AddWithValue("@ownerId", (object?)offerId ?? invoiceId!.Value);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return content;

            content = ReadDocumentContent(reader);
        }

        using (var cmd = Conn.CreateCommand()) {
            cmd.CommandText = @"SELECT id,source_item_id,sort_order,position_number,name,description,quantity,unit,
                unit_price,discount_percent,tax_rate,net_amount,gross_amount,is_optional
                FROM document_line_items
                WHERE document_content_id=@contentId
                ORDER BY sort_order,id";
            cmd.Parameters.AddWithValue("@contentId", content.Id);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                content.LineItems.Add(ReadDocumentLineItem(reader));
        }

        return content;
    }

    static DocumentContent ReadDocumentContent(DbDataReader reader, int offset = 0) {
        return new DocumentContent {
            Id = reader.GetInt64(offset),
            Header = reader.IsDBNull(offset + 1) ? "" : reader.GetString(offset + 1),
            PreText = reader.IsDBNull(offset + 2) ? "" : reader.GetString(offset + 2),
            PostText = reader.IsDBNull(offset + 3) ? "" : reader.GetString(offset + 3),
            InternalNote = reader.IsDBNull(offset + 4) ? "" : reader.GetString(offset + 4),
            SourceProvider = reader.IsDBNull(offset + 5) ? null : reader.GetString(offset + 5),
            SourceEntityType = reader.IsDBNull(offset + 6) ? null : reader.GetString(offset + 6),
            SourceExternalId = reader.IsDBNull(offset + 7) ? null : reader.GetString(offset + 7),
            SourceSnapshotJson = reader.IsDBNull(offset + 8) ? null : reader.GetString(offset + 8),
            LastImportedAt = reader.GetInvariantText(offset + 9)
        };
    }

    static DocumentLineItem ReadDocumentLineItem(DbDataReader reader, int offset = 0) {
        return new DocumentLineItem {
            Id = reader.GetInt64(offset),
            SourceItemId = reader.IsDBNull(offset + 1) ? null : reader.GetString(offset + 1),
            SortOrder = reader.IsDBNull(offset + 2) ? 0 : reader.GetInt32(offset + 2),
            PositionNumber = reader.IsDBNull(offset + 3) ? "" : reader.GetString(offset + 3),
            Name = reader.IsDBNull(offset + 4) ? "" : reader.GetString(offset + 4),
            Description = reader.IsDBNull(offset + 5) ? "" : reader.GetString(offset + 5),
            Quantity = reader.IsDBNull(offset + 6) ? 1 : reader.GetDouble(offset + 6),
            Unit = reader.IsDBNull(offset + 7) ? "" : reader.GetString(offset + 7),
            UnitPrice = reader.IsDBNull(offset + 8) ? 0 : reader.GetDouble(offset + 8),
            DiscountPercent = reader.IsDBNull(offset + 9) ? 0 : reader.GetDouble(offset + 9),
            TaxRate = reader.IsDBNull(offset + 10) ? 0 : reader.GetDouble(offset + 10),
            NetAmount = reader.IsDBNull(offset + 11) ? 0 : reader.GetDouble(offset + 11),
            GrossAmount = reader.IsDBNull(offset + 12) ? 0 : reader.GetDouble(offset + 12),
            IsOptional = !reader.IsDBNull(offset + 13) &&
                Convert.ToInt32(reader.GetValue(offset + 13), CultureInfo.InvariantCulture) != 0
        };
    }

    void SaveDocumentContent(
        DocumentContent content,
        long? offerId,
        long? invoiceId,
        DbTransaction tx,
        bool forceInsert) {
        ValidateDocumentOwner(offerId, invoiceId);
        content.LineItems ??= [];

        long? persistedContentId = forceInsert
            ? null
            : FindDocumentContentId(offerId, invoiceId, tx);
        bool updateExistingItems = persistedContentId.HasValue;

        if (persistedContentId.HasValue) {
            if (content.Id > 0 && content.Id != persistedContentId.Value)
                throw new InvalidOperationException("Der Dokumentinhalt gehört nicht zu diesem Angebot bzw. dieser Rechnung.");

            content.Id = persistedContentId.Value;
            using var update = Conn.CreateCommand();
            update.Transaction = tx;
            update.CommandText = @"UPDATE document_contents SET
                header=@header,pre_text=@pre,post_text=@post,internal_note=@note,
                source_provider=@provider,source_entity_type=@entityType,source_external_id=@externalId,
                source_snapshot_json=@snapshot,last_imported_at=@lastImported
                WHERE id=@id";
            update.Parameters.AddWithValue("@id", content.Id);
            AddDocumentContentParameters(update, content);
            if (update.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("Der Dokumentinhalt wurde nicht gefunden.");
        }
        else {
            using var insert = Conn.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = @"INSERT INTO document_contents(
                offer_id,invoice_id,header,pre_text,post_text,internal_note,
                source_provider,source_entity_type,source_external_id,source_snapshot_json,last_imported_at)
                VALUES(@offerId,@invoiceId,@header,@pre,@post,@note,
                    @provider,@entityType,@externalId,@snapshot,@lastImported)";
            insert.Parameters.AddWithValue("@offerId", offerId.HasValue ? offerId.Value : DBNull.Value);
            insert.Parameters.AddWithValue("@invoiceId", invoiceId.HasValue ? invoiceId.Value : DBNull.Value);
            AddDocumentContentParameters(insert, content);
            insert.ExecuteNonQuery();
            content.Id = LastInsertId(tx);
        }

        SaveDocumentLineItems(content, tx, updateExistingItems);
    }

    long? FindDocumentContentId(long? offerId, long? invoiceId, DbTransaction tx) {
        using var cmd = Conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = offerId.HasValue
            ? "SELECT id FROM document_contents WHERE offer_id=@ownerId"
            : "SELECT id FROM document_contents WHERE invoice_id=@ownerId";
        cmd.Parameters.AddWithValue("@ownerId", (object?)offerId ?? invoiceId!.Value);
        var result = cmd.ExecuteScalar();
        return result == null || result == DBNull.Value ? null : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    static void AddDocumentContentParameters(DbCommand cmd, DocumentContent content) {
        cmd.Parameters.AddWithValue("@header", (object?)content.Header ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pre", (object?)content.PreText ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@post", (object?)content.PostText ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@note", (object?)content.InternalNote ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@provider", DbNullIfWhiteSpace(content.SourceProvider));
        cmd.Parameters.AddWithValue("@entityType", DbNullIfWhiteSpace(content.SourceEntityType));
        cmd.Parameters.AddWithValue("@externalId", DbNullIfWhiteSpace(content.SourceExternalId));
        cmd.Parameters.AddWithValue("@snapshot", DbNullIfWhiteSpace(content.SourceSnapshotJson));
        cmd.Parameters.AddWithValue("@lastImported", DbNullIfWhiteSpace(content.LastImportedAt));
    }

    void SaveDocumentLineItems(DocumentContent content, DbTransaction tx, bool updateExistingItems) {
        var persistedIds = updateExistingItems
            ? GetDocumentLineItemIds(content.Id, tx)
            : [];
        var incomingExistingIds = new HashSet<long>();
        var seenInstances = new HashSet<DocumentLineItem>(ReferenceEqualityComparer.Instance);
        var sourceItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in content.LineItems) {
            if (item == null)
                throw new InvalidOperationException("Eine Dokumentposition darf nicht null sein.");
            if (!seenInstances.Add(item))
                throw new InvalidOperationException("Dieselbe Dokumentposition ist mehrfach in der Liste enthalten.");

            if (!string.IsNullOrWhiteSpace(item.SourceItemId) && !sourceItemIds.Add(item.SourceItemId.Trim()))
                throw new InvalidOperationException($"Die Quellpositions-ID '{item.SourceItemId.Trim()}' ist mehrfach vorhanden.");

            if (!updateExistingItems || item.Id <= 0)
                continue;
            if (!incomingExistingIds.Add(item.Id))
                throw new InvalidOperationException("Eine Dokumentpositions-ID ist mehrfach vorhanden.");
            if (!persistedIds.Contains(item.Id))
                throw new InvalidOperationException("Eine Dokumentposition gehört nicht zu diesem Dokument.");
        }

        if (updateExistingItems) {
            foreach (var removedId in persistedIds.Except(incomingExistingIds)) {
                using var delete = Conn.CreateCommand();
                delete.Transaction = tx;
                delete.CommandText = "DELETE FROM document_line_items WHERE id=@id AND document_content_id=@contentId";
                delete.Parameters.AddWithValue("@id", removedId);
                delete.Parameters.AddWithValue("@contentId", content.Id);
                delete.ExecuteNonQuery();
            }

            // Release source keys before applying the complete new set. This
            // permits legitimate key swaps and replacement rows in one atomic save.
            using var releaseSourceKeys = Conn.CreateCommand();
            releaseSourceKeys.Transaction = tx;
            releaseSourceKeys.CommandText = "UPDATE document_line_items SET source_item_id=NULL WHERE document_content_id=@contentId";
            releaseSourceKeys.Parameters.AddWithValue("@contentId", content.Id);
            releaseSourceKeys.ExecuteNonQuery();
        }

        foreach (var item in content.LineItems) {
            var incomingId = item.Id;
            if (updateExistingItems && incomingId > 0) {
                using var update = Conn.CreateCommand();
                update.Transaction = tx;
                update.CommandText = @"UPDATE document_line_items SET
                    source_item_id=@sourceItemId,sort_order=@sortOrder,position_number=@positionNumber,
                    name=@name,description=@description,quantity=@quantity,unit=@unit,unit_price=@unitPrice,
                    discount_percent=@discountPercent,tax_rate=@taxRate,net_amount=@netAmount,
                    gross_amount=@grossAmount,is_optional=@isOptional
                    WHERE id=@id AND document_content_id=@contentId";
                update.Parameters.AddWithValue("@id", incomingId);
                update.Parameters.AddWithValue("@contentId", content.Id);
                AddDocumentLineItemParameters(update, item);
                if (update.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("Eine Dokumentposition wurde nicht gefunden.");
            }
            else {
                using var insert = Conn.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = @"INSERT INTO document_line_items(
                    document_content_id,source_item_id,sort_order,position_number,name,description,
                    quantity,unit,unit_price,discount_percent,tax_rate,net_amount,gross_amount,is_optional)
                    VALUES(@contentId,@sourceItemId,@sortOrder,@positionNumber,@name,@description,
                    @quantity,@unit,@unitPrice,@discountPercent,@taxRate,@netAmount,@grossAmount,@isOptional)";
                insert.Parameters.AddWithValue("@contentId", content.Id);
                AddDocumentLineItemParameters(insert, item);
                insert.ExecuteNonQuery();
                item.Id = LastInsertId(tx);
            }
        }
    }

    HashSet<long> GetDocumentLineItemIds(long contentId, DbTransaction tx) {
        var ids = new HashSet<long>();
        using var cmd = Conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id FROM document_line_items WHERE document_content_id=@contentId";
        cmd.Parameters.AddWithValue("@contentId", contentId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            ids.Add(reader.GetInt64(0));
        return ids;
    }

    static void AddDocumentLineItemParameters(DbCommand cmd, DocumentLineItem item) {
        cmd.Parameters.AddWithValue("@sourceItemId", DbNullIfWhiteSpace(item.SourceItemId));
        cmd.Parameters.AddWithValue("@sortOrder", item.SortOrder);
        cmd.Parameters.AddWithValue("@positionNumber", (object?)item.PositionNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@name", (object?)item.Name ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@description", (object?)item.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@quantity", item.Quantity);
        cmd.Parameters.AddWithValue("@unit", (object?)item.Unit ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@unitPrice", item.UnitPrice);
        cmd.Parameters.AddWithValue("@discountPercent", item.DiscountPercent);
        cmd.Parameters.AddWithValue("@taxRate", item.TaxRate);
        cmd.Parameters.AddWithValue("@netAmount", item.NetAmount);
        cmd.Parameters.AddWithValue("@grossAmount", item.GrossAmount);
        cmd.Parameters.AddWithValue("@isOptional", item.IsOptional ? 1 : 0);
    }

    static object DbNullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    static void ValidateDocumentOwner(long? offerId, long? invoiceId) {
        if (offerId.HasValue == invoiceId.HasValue)
            throw new ArgumentException("Ein Dokumentinhalt muss genau einem Angebot oder einer Rechnung zugeordnet sein.");
    }

    static (long ContentId, long[] ItemIds) CaptureDocumentIdentity(DocumentContent content) {
        content.LineItems ??= [];
        return (content.Id, content.LineItems.Select(item => item?.Id ?? 0).ToArray());
    }

    static void RestoreDocumentIdentity(DocumentContent content, (long ContentId, long[] ItemIds) identity) {
        content.Id = identity.ContentId;
        for (var index = 0; index < content.LineItems.Count && index < identity.ItemIds.Length; index++) {
            if (content.LineItems[index] != null)
                content.LineItems[index].Id = identity.ItemIds[index];
        }
    }

    static void TryRollback(DbTransaction tx) {
        try { tx.Rollback(); }
        catch { }
    }

    static void NormalizeOfferDiscount(Offer offer) {
        if (offer.DiscountPercent == 0)
            offer.AmountBeforeDiscount = offer.Amount;
    }

    static void NormalizeOfferNumber(Offer offer) {
        offer.OfferNumber = (offer.OfferNumber ?? "").Trim();
        if (offer.OfferNumber.Length == 0)
            throw new InvalidOperationException("Bitte geben Sie eine Angebotsnummer ein.");
        if (offer.OfferNumber.Length > 191)
            throw new InvalidOperationException("Die Angebotsnummer darf höchstens 191 Zeichen lang sein.");
    }

    void PrepareOfferNumberForInsert(Offer offer, DbTransaction tx) {
        if (!TryParseGeneratedOfferNumber(offer.OfferNumber, out var prefix, out var requestedSequence))
            return;

        var counterKey = "offer_number_counter:" + prefix;
        using (var ensureCounter = Conn.CreateCommand()) {
            ensureCounter.Transaction = tx;
            ensureCounter.CommandText = _dialect.InsertOrIgnore(
                "INSERT OR IGNORE INTO settings(`key`,value) VALUES(@key,'0')");
            ensureCounter.Parameters.AddWithValue("@key", counterKey);
            ensureCounter.ExecuteNonQuery();
        }

        long counterValue;
        using (var readCounter = Conn.CreateCommand()) {
            readCounter.Transaction = tx;
            readCounter.CommandText = "SELECT value FROM settings WHERE `key`=@key"
                + (_dialect is MariaDbDialect ? " FOR UPDATE" : "");
            readCounter.Parameters.AddWithValue("@key", counterKey);
            var raw = readCounter.ExecuteScalar()?.ToString();
            counterValue = long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;
        }

        var nextNumber = FindNextOfferNumber(prefix, tx);
        if (!TryParseGeneratedOfferNumber(nextNumber, out _, out var nextSequence))
            throw new InvalidOperationException("Die nächste Angebotsnummer konnte nicht ermittelt werden.");
        var maximumExisting = nextSequence - 1;

        bool alreadyExists;
        using (var duplicate = Conn.CreateCommand()) {
            duplicate.Transaction = tx;
            duplicate.CommandText = "SELECT COUNT(*) FROM offers WHERE offer_number=@number";
            duplicate.Parameters.AddWithValue("@number", offer.OfferNumber);
            alreadyExists = Convert.ToInt64(duplicate.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
        }

        var finalSequence = alreadyExists
            ? checked(Math.Max(counterValue, maximumExisting) + 1)
            : requestedSequence;
        if (alreadyExists)
            offer.OfferNumber = prefix + finalSequence.ToString("D4", CultureInfo.InvariantCulture);

        using var updateCounter = Conn.CreateCommand();
        updateCounter.Transaction = tx;
        updateCounter.CommandText = "UPDATE settings SET value=@value WHERE `key`=@key";
        updateCounter.Parameters.AddWithValue("@key", counterKey);
        updateCounter.Parameters.AddWithValue("@value",
            Math.Max(Math.Max(counterValue, maximumExisting), finalSequence)
                .ToString(CultureInfo.InvariantCulture));
        if (updateCounter.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("Der Angebotsnummernzähler konnte nicht aktualisiert werden.");
    }

    static bool TryParseGeneratedOfferNumber(string? value, out string prefix, out long sequence) {
        prefix = "";
        sequence = 0;
        var normalized = value?.Trim() ?? "";
        if (normalized.Length <= 9
            || !normalized.StartsWith("ANG-", StringComparison.OrdinalIgnoreCase)
            || normalized[8] != '-'
            || !normalized.AsSpan(4, 4).ToString().All(char.IsDigit)
            || !normalized.AsSpan(9).ToString().All(char.IsDigit))
            return false;

        if (!long.TryParse(normalized.AsSpan(9), NumberStyles.None,
                CultureInfo.InvariantCulture, out sequence))
            return false;

        prefix = normalized[..9];
        return true;
    }

    public void DeleteOffer(long id) { ExecWithId("DELETE FROM offers WHERE id=@id", id); }

    /// <summary>
    /// Creates and persistently links a project from an accepted offer.
    /// The offer row is locked for the duration of the transaction so that
    /// concurrent callers cannot create more than one project for an offer.
    /// </summary>
    public Project CreateProjectFromOffer(long offerId, string projectName) {
        projectName = projectName?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(projectName))
            throw new InvalidOperationException("Bitte geben Sie einen Projektnamen ein.");

        using var tx = BeginWriteTransaction();
        try {
            string offerNumber;
            string offerDate;
            string dateExpected;
            string customer;
            string status;
            double amountBeforeDiscount;
            double discountPercent;
            double amount;
            long? existingProjectId;

            using (var select = Conn.CreateCommand()) {
                select.Transaction = tx;
                select.CommandText = @"SELECT offer_number,offer_date,date_expected,customer,amount_before_discount,discount_percent,
                    amount,status,project_id
                    FROM offers WHERE id=@id" + (_dialect is MariaDbDialect ? " FOR UPDATE" : "");
                select.Parameters.AddWithValue("@id", offerId);

                using var reader = select.ExecuteReader();
                if (!reader.Read())
                    throw new InvalidOperationException($"Angebot #{offerId} wurde nicht gefunden.");

                offerNumber = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim();
                offerDate = (reader.GetInvariantText(1, DbTextKind.Date) ?? "").Trim();
                dateExpected = (reader.GetInvariantText(2, DbTextKind.Date) ?? "").Trim();
                customer = reader.IsDBNull(3) ? "" : reader.GetString(3).Trim();
                amountBeforeDiscount = reader.IsDBNull(4) ? 0 : reader.GetDouble(4);
                discountPercent = reader.IsDBNull(5) ? 0 : reader.GetDouble(5);
                amount = reader.IsDBNull(6) ? 0 : reader.GetDouble(6);
                status = reader.IsDBNull(7) ? "" : reader.GetString(7);
                existingProjectId = reader.IsDBNull(8) ? null : reader.GetInt64(8);
            }

            if (existingProjectId.HasValue)
                throw new InvalidOperationException($"Angebot #{offerId} ist bereits mit Projekt #{existingProjectId.Value} verkn\u00fcpft.");

            if (!string.Equals(status, "Beauftragt", StringComparison.Ordinal))
                throw new InvalidOperationException($"Angebot #{offerId} muss den Status 'Beauftragt' haben.");

            var project = new Project {
                ProjectNumber = offerNumber,
                Name = projectName,
                Client = customer,
                Color = "#3498db",
                StartDate = (ParseDate(dateExpected) ?? ParseDate(offerDate))?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                EndDate = null,
                OriginalBudget = amountBeforeDiscount == 0 && discountPercent == 0 ? amount : amountBeforeDiscount,
                DiscountPercent = discountPercent,
                Budget = amount,
                Status = "active"
            };

            using (var insert = Conn.CreateCommand()) {
                insert.Transaction = tx;
                insert.CommandText = @"INSERT INTO projects(project_number,name,client,color,start_date,end_date,original_budget,discount_percent,budget,status,created_at)
                    VALUES(@pn,@n,@cl,@c,@sd,@ed,@ob,@discount,@b,@s,CURRENT_TIMESTAMP)";
                insert.Parameters.AddWithValue("@pn", (object?)project.ProjectNumber ?? DBNull.Value);
                insert.Parameters.AddWithValue("@n", project.Name);
                insert.Parameters.AddWithValue("@cl", (object?)project.Client ?? "");
                insert.Parameters.AddWithValue("@c", project.Color);
                insert.Parameters.AddWithValue("@sd", (object?)project.StartDate ?? DBNull.Value);
                insert.Parameters.AddWithValue("@ed", DBNull.Value);
                insert.Parameters.AddWithValue("@ob", project.OriginalBudget);
                insert.Parameters.AddWithValue("@discount", project.DiscountPercent);
                insert.Parameters.AddWithValue("@b", project.Budget);
                insert.Parameters.AddWithValue("@s", project.Status);
                insert.ExecuteNonQuery();
            }
            project.Id = LastInsertId(tx);

            using (var link = Conn.CreateCommand()) {
                link.Transaction = tx;
                link.CommandText = @"UPDATE offers SET project_id=@projectId
                    WHERE id=@offerId AND project_id IS NULL";
                link.Parameters.AddWithValue("@projectId", project.Id);
                link.Parameters.AddWithValue("@offerId", offerId);
                if (link.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException($"Angebot #{offerId} wurde zwischenzeitlich ge\u00e4ndert oder bereits konvertiert.");
            }

            tx.Commit();
            return project;
        }
        catch {
            try { tx.Rollback(); } catch { }
            throw;
        }
    }

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
        cmd.CommandText = @"SELECT r.id,r.user_id,r.name,r.role,r.availability,r.hourly_rate,
            r.work_start_hour,r.work_end_hour,r.created_at,
            COALESCE(NULLIF(r.avatar_data,''),u.avatar_data)
            FROM resources r LEFT JOIN users u ON u.id=r.user_id";
        using var r = cmd.ExecuteReader();
        while (r.Read()) {
            list.Add(new Resource {
                Id = r.GetInt64(0),
                UserId = r.IsDBNull(1) ? null : r.GetInt64(1),
                Name = r.GetString(2),
                Role = r.IsDBNull(3) ? "" : r.GetString(3),
                Availability = r.IsDBNull(4) ? 1.0 : r.GetDouble(4),
                HourlyRate = r.IsDBNull(5) ? 0 : r.GetDouble(5),
                WorkStartHour = r.IsDBNull(6) ? 8 : r.GetInt32(6),
                WorkEndHour = r.IsDBNull(7) ? 17 : r.GetInt32(7),
                CreatedAt = r.GetInvariantText(8) ?? "",
                AvatarData = r.IsDBNull(9) ? null : r.GetString(9)
            });
        }
        return list;
    }

    public void AddResource(Resource res) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "INSERT INTO resources(user_id,name,role,availability,hourly_rate,work_start_hour,work_end_hour,created_at) VALUES(@uid,@n,@r,@a,@hr,@ws,@we,CURRENT_TIMESTAMP)";
        cmd.Parameters.AddWithValue("@uid", res.UserId.HasValue ? (object)res.UserId.Value : DBNull.Value);
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
        cmd.CommandText = "SELECT id,project_number,name,client,color,start_date,end_date,original_budget,discount_percent,budget,status,created_at FROM projects";
        using var r = cmd.ExecuteReader();
        while (r.Read()) {
            list.Add(new Project {
                Id = r.GetInt64(0),
                ProjectNumber = r.IsDBNull(1) ? "" : r.GetString(1),
                Name = r.GetString(2),
                Client = r.IsDBNull(3) ? "" : r.GetString(3),
                Color = r.IsDBNull(4) ? "#3498db" : r.GetString(4),
                StartDate = r.GetInvariantText(5, DbTextKind.Date) ?? "",
                EndDate = r.GetInvariantText(6, DbTextKind.Date) ?? "",
                OriginalBudget = r.IsDBNull(7) ? 0 : r.GetDouble(7),
                DiscountPercent = r.IsDBNull(8) ? 0 : r.GetDouble(8),
                Budget = r.IsDBNull(9) ? 0 : r.GetDouble(9),
                Status = r.IsDBNull(10) ? "active" : r.GetString(10),
                CreatedAt = r.GetInvariantText(11) ?? ""
            });
        }
        return list;
    }

    public void AddProject(Project p) {
        NormalizeProjectDiscount(p);
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO projects(project_number,name,client,color,start_date,end_date,original_budget,discount_percent,budget,status,created_at)
            VALUES(@pn,@n,@cl,@c,@sd,@ed,@ob,@discount,@b,@s,CURRENT_TIMESTAMP)";
        cmd.Parameters.AddWithValue("@pn", (object?)p.ProjectNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@n", p.Name);
        cmd.Parameters.AddWithValue("@cl", (object?)p.Client ?? "");
        cmd.Parameters.AddWithValue("@c", (object?)p.Color ?? "#3498db");
        cmd.Parameters.AddWithValue("@sd", (object?)p.StartDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ed", (object?)p.EndDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ob", p.OriginalBudget);
        cmd.Parameters.AddWithValue("@discount", p.DiscountPercent);
        cmd.Parameters.AddWithValue("@b", p.Budget);
        cmd.Parameters.AddWithValue("@s", (object?)p.Status ?? "active");
        cmd.ExecuteNonQuery();
        p.Id = LastInsertId();
    }

    public void UpdateProject(Project p) {
        NormalizeProjectDiscount(p);
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"UPDATE projects SET project_number=@pn,name=@n,client=@cl,color=@c,start_date=@sd,
            end_date=@ed,original_budget=@ob,discount_percent=@discount,budget=@b,status=@s WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", p.Id);
        cmd.Parameters.AddWithValue("@pn", (object?)p.ProjectNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@n", p.Name);
        cmd.Parameters.AddWithValue("@cl", (object?)p.Client ?? "");
        cmd.Parameters.AddWithValue("@c", (object?)p.Color ?? "#3498db");
        cmd.Parameters.AddWithValue("@sd", (object?)p.StartDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ed", (object?)p.EndDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ob", p.OriginalBudget);
        cmd.Parameters.AddWithValue("@discount", p.DiscountPercent);
        cmd.Parameters.AddWithValue("@b", p.Budget);
        cmd.Parameters.AddWithValue("@s", (object?)p.Status ?? "active");
        cmd.ExecuteNonQuery();
    }

    static void NormalizeProjectDiscount(Project project) {
        if (project.DiscountPercent == 0)
            project.OriginalBudget = project.Budget;
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
                Date = r.GetInvariantText(3, DbTextKind.Date) ?? "", Hours = r.IsDBNull(4) ? 8.0 : r.GetDouble(4),
                Notes = r.IsDBNull(5) ? "" : r.GetString(5),
                CreatedAt = r.GetInvariantText(6) ?? ""
            });
        }
        return list;
    }

    public (DateTime Start, DateTime End, double StartHours, double EndHours)? GetAllocationRange(
        long resourceId, long projectId, DateTime anchorDate) {
        return GetContiguousAllocationRange(
            "resource_allocations",
            "resource_id=@rid AND project_id=@pid",
            anchorDate,
            ("@rid", resourceId),
            ("@pid", projectId));
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

    public void MoveAllocations(
        long fromResourceId,
        long toResourceId,
        long projectId,
        DateTime from,
        DateTime to) {
        if (fromResourceId <= 0) throw new ArgumentOutOfRangeException(nameof(fromResourceId));
        if (toResourceId <= 0) throw new ArgumentOutOfRangeException(nameof(toResourceId));
        if (projectId <= 0) throw new ArgumentOutOfRangeException(nameof(projectId));
        if (fromResourceId == toResourceId) return;

        var start = from.Date;
        var end = to.Date;
        if (end < start) return;

        using var tx = BeginWriteTransaction();
        try {
            using (var conflicts = Conn.CreateCommand()) {
                conflicts.Transaction = tx;
                conflicts.CommandText = @"SELECT COUNT(*) FROM resource_allocations target
                    WHERE target.resource_id=@toResourceId
                      AND target.project_id=@projectId
                      AND target.date>=@from AND target.date<=@to
                      AND EXISTS(
                          SELECT 1 FROM resource_allocations source
                          WHERE source.resource_id=@fromResourceId
                            AND source.project_id=target.project_id
                            AND source.date=target.date)" +
                    (_dialect is MariaDbDialect ? " FOR UPDATE" : "");
                conflicts.Parameters.AddWithValue("@fromResourceId", fromResourceId);
                conflicts.Parameters.AddWithValue("@toResourceId", toResourceId);
                conflicts.Parameters.AddWithValue("@projectId", projectId);
                conflicts.Parameters.AddWithValue("@from", start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                conflicts.Parameters.AddWithValue("@to", end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                if (Convert.ToInt64(conflicts.ExecuteScalar(), CultureInfo.InvariantCulture) != 0)
                    throw new InvalidOperationException(
                        "Die Zielressource enthält im gewählten Zeitraum bereits Zuordnungen für dieses Projekt.");
            }

            using (var move = Conn.CreateCommand()) {
                move.Transaction = tx;
                move.CommandText = @"UPDATE resource_allocations SET resource_id=@toResourceId
                    WHERE resource_id=@fromResourceId AND project_id=@projectId
                      AND date>=@from AND date<=@to";
                move.Parameters.AddWithValue("@fromResourceId", fromResourceId);
                move.Parameters.AddWithValue("@toResourceId", toResourceId);
                move.Parameters.AddWithValue("@projectId", projectId);
                move.Parameters.AddWithValue("@from", start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                move.Parameters.AddWithValue("@to", end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                move.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch {
            TryRollback(tx);
            throw;
        }
    }

    public void AddAllocationsRange(long resourceId, long projectId, DateTime from, DateTime to, double hours = 8.0) {
        var start = from.Date;
        var end = to.Date;
        if (end < start) return;
        ValidateAllocationHours(hours);

        using var tx = BeginWriteTransaction();
        try {
            const int batchSize = 100;
            var batchStart = start;
            while (true) {
                var dates = BuildDateBatch(batchStart, end, batchSize);
                using var cmd = Conn.CreateCommand();
                cmd.Transaction = tx;
                var values = new List<string>(dates.Count);
                cmd.Parameters.AddWithValue("@rid", resourceId);
                cmd.Parameters.AddWithValue("@pid", projectId);
                cmd.Parameters.AddWithValue("@h", hours);
                for (int i = 0; i < dates.Count; i++) {
                    var parameterName = $"@d{i}";
                    values.Add($"(@rid,@pid,{parameterName},@h,NULL,CURRENT_TIMESTAMP)");
                    cmd.Parameters.AddWithValue(parameterName,
                        dates[i].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                }
                cmd.CommandText = _dialect.InsertOrIgnore(
                    "INSERT OR IGNORE INTO resource_allocations(resource_id,project_id,date,hours,notes,created_at) VALUES "
                    + string.Join(',', values));
                cmd.ExecuteNonQuery();

                if (dates[^1] == end) break;
                batchStart = dates[^1].AddDays(1);
            }

            tx.Commit();
        }
        catch {
            TryRollback(tx);
            throw;
        }
    }

    public void DeleteAllocationsRange(long resourceId, long projectId, DateTime from, DateTime to) {
        var start = from.Date;
        var end = to.Date;
        if (end < start) return;

        using var tx = BeginWriteTransaction();
        try {
            using var cmd = Conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"DELETE FROM resource_allocations
                WHERE resource_id=@rid AND project_id=@pid AND date>=@from AND date<=@to";
            cmd.Parameters.AddWithValue("@rid", resourceId);
            cmd.Parameters.AddWithValue("@pid", projectId);
            cmd.Parameters.AddWithValue("@from", start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@to", end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            cmd.ExecuteNonQuery();
            tx.Commit();
        }
        catch {
            TryRollback(tx);
            throw;
        }
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
                CreatedAt = r.GetInvariantText(6) ?? ""
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
                ProjectId = r.GetInt64(3), Date = r.GetInvariantText(4, DbTextKind.Date) ?? "",
                Hours = r.IsDBNull(5) ? 8.0 : r.GetDouble(5),
                Notes = r.IsDBNull(6) ? "" : r.GetString(6),
                HardwareName = r.GetString(7), HardwareColor = r.GetString(8),
                ProjectName = r.GetString(9)
            });
        }
        return list;
    }

    public (DateTime Start, DateTime End, double StartHours, double EndHours)? GetHardwareAllocationRange(
        long resourceId, long hardwareId, long projectId, DateTime anchorDate) {
        return GetContiguousAllocationRange(
            "hardware_allocations",
            "resource_id=@rid AND hardware_id=@hid AND project_id=@pid",
            anchorDate,
            ("@rid", resourceId),
            ("@hid", hardwareId),
            ("@pid", projectId));
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

    public void AddHardwareAllocationsRange(long resourceId, long hardwareId, long projectId, DateTime from, DateTime to, double hours = 8.0) {
        var start = from.Date;
        var end = to.Date;
        if (end < start) return;
        ValidateAllocationHours(hours);

        using var tx = BeginWriteTransaction();
        try {
            const int batchSize = 100;
            var batchStart = start;
            while (true) {
                var dates = BuildDateBatch(batchStart, end, batchSize);
                using var cmd = Conn.CreateCommand();
                cmd.Transaction = tx;
                var values = new List<string>(dates.Count);
                cmd.Parameters.AddWithValue("@rid", resourceId);
                cmd.Parameters.AddWithValue("@hid", hardwareId);
                cmd.Parameters.AddWithValue("@pid", projectId);
                cmd.Parameters.AddWithValue("@h", hours);
                for (int i = 0; i < dates.Count; i++) {
                    var parameterName = $"@d{i}";
                    values.Add($"(@rid,@hid,@pid,{parameterName},@h,NULL,CURRENT_TIMESTAMP)");
                    cmd.Parameters.AddWithValue(parameterName,
                        dates[i].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                }
                cmd.CommandText = _dialect.InsertOrIgnore(
                    "INSERT OR IGNORE INTO hardware_allocations(resource_id,hardware_id,project_id,date,hours,notes,created_at) VALUES "
                    + string.Join(',', values));
                cmd.ExecuteNonQuery();

                if (dates[^1] == end) break;
                batchStart = dates[^1].AddDays(1);
            }

            tx.Commit();
        }
        catch {
            TryRollback(tx);
            throw;
        }
    }

    public void DeleteHardwareAllocationsRange(long resourceId, long hardwareId, long projectId, DateTime from, DateTime to) {
        var start = from.Date;
        var end = to.Date;
        if (end < start) return;

        using var tx = BeginWriteTransaction();
        try {
            using var cmd = Conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"DELETE FROM hardware_allocations
                WHERE resource_id=@rid AND hardware_id=@hid AND project_id=@pid AND date>=@from AND date<=@to";
            cmd.Parameters.AddWithValue("@rid", resourceId);
            cmd.Parameters.AddWithValue("@hid", hardwareId);
            cmd.Parameters.AddWithValue("@pid", projectId);
            cmd.Parameters.AddWithValue("@from", start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@to", end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            cmd.ExecuteNonQuery();
            tx.Commit();
        }
        catch {
            TryRollback(tx);
            throw;
        }
    }

    public void DeleteHardwareAllocation(long id) { ExecWithId("DELETE FROM hardware_allocations WHERE id=@id", id); }

    static void ValidateAllocationHours(double hours) {
        if (!double.IsFinite(hours) || hours < 0)
            throw new ArgumentOutOfRangeException(nameof(hours), "Zuordnungsstunden müssen eine endliche, nicht negative Zahl sein.");
    }

    static List<DateTime> BuildDateBatch(DateTime start, DateTime end, int maximumCount) {
        var dates = new List<DateTime>(maximumCount);
        var date = start.Date;
        while (dates.Count < maximumCount) {
            dates.Add(date);
            if (date == end.Date) break;
            date = date.AddDays(1);
        }
        return dates;
    }

    (DateTime Start, DateTime End, double StartHours, double EndHours)? GetContiguousAllocationRange(
        string tableName,
        string keyPredicate,
        DateTime anchorDate,
        params (string Name, object Value)[] keyParameters) {
        var anchor = anchorDate.Date;
        using var tx = Conn.BeginTransaction();
        try {
            var startBoundary = ReadContiguousAllocationBoundary(
                tx, tableName, keyPredicate, anchor, true, keyParameters);
            if (startBoundary == null) {
                tx.Commit();
                return null;
            }

            var endBoundary = ReadContiguousAllocationBoundary(
                tx, tableName, keyPredicate, anchor, false, keyParameters);
            if (endBoundary == null)
                throw new InvalidOperationException("Die Zuordnung am Ankerdatum wurde während der Bereichsabfrage inkonsistent gelesen.");

            tx.Commit();
            return (startBoundary.Value.Date, endBoundary.Value.Date,
                startBoundary.Value.Hours, endBoundary.Value.Hours);
        }
        catch {
            TryRollback(tx);
            throw;
        }
    }

    (DateTime Date, double Hours)? ReadContiguousAllocationBoundary(
        DbTransaction tx,
        string tableName,
        string keyPredicate,
        DateTime anchor,
        bool searchBackwards,
        params (string Name, object Value)[] keyParameters) {
        using var cmd = Conn.CreateCommand();
        cmd.Transaction = tx;
        var comparison = searchBackwards ? "<=" : ">=";
        var order = searchBackwards ? "DESC" : "ASC";
        cmd.CommandText = $@"SELECT date,hours FROM {tableName}
            WHERE {keyPredicate} AND date{comparison}@anchor
            ORDER BY date {order}";
        foreach (var (name, value) in keyParameters)
            cmd.Parameters.AddWithValue(name, value);
        cmd.Parameters.AddWithValue("@anchor", anchor.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        using var reader = cmd.ExecuteReader();
        var expected = anchor;
        (DateTime Date, double Hours)? boundary = null;
        while (reader.Read()) {
            if (!DateTime.TryParseExact(reader.GetInvariantText(0, DbTextKind.Date), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date) || date != expected)
                break;

            boundary = (date, reader.IsDBNull(1) ? 8.0 : reader.GetDouble(1));
            if ((searchBackwards && date == DateTime.MinValue.Date) ||
                (!searchBackwards && date == DateTime.MaxValue.Date))
                break;
            expected = date.AddDays(searchBackwards ? -1 : 1);
        }

        return boundary;
    }

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
                Deadline = r.GetInvariantText(4, DbTextKind.Date),
                Responsible = r.IsDBNull(5) ? null : r.GetString(5),
                HoursBudget = r.IsDBNull(6) ? 0 : r.GetDouble(6),
                Priority = r.IsDBNull(7) ? 2 : r.GetInt32(7),
                Dependencies = r.IsDBNull(8) ? null : r.GetString(8),
                Notes = r.IsDBNull(9) ? null : r.GetString(9),
                SortOrder = r.IsDBNull(10) ? 0 : r.GetInt32(10),
                CreatedAt = r.GetInvariantText(11)
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
                Deadline = r.GetInvariantText(4, DbTextKind.Date),
                Responsible = r.IsDBNull(5) ? null : r.GetString(5),
                HoursBudget = r.IsDBNull(6) ? 0 : r.GetDouble(6),
                Priority = r.IsDBNull(7) ? 2 : r.GetInt32(7),
                Dependencies = r.IsDBNull(8) ? null : r.GetString(8),
                Notes = r.IsDBNull(9) ? null : r.GetString(9),
                SortOrder = r.IsDBNull(10) ? 0 : r.GetInt32(10),
                CreatedAt = r.GetInvariantText(11)
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
            DueDate = r.GetInvariantText(6, DbTextKind.Date),
            ProjectId = r.IsDBNull(7) ? null : r.GetInt64(7),
            MilestoneId = r.IsDBNull(8) ? null : r.GetInt64(8),
            CreatedAt = r.GetInvariantText(9)
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
            DueDate = r.GetInvariantText(6, DbTextKind.Date),
            ProjectId = r.IsDBNull(7) ? null : r.GetInt64(7),
            MilestoneId = r.IsDBNull(8) ? null : r.GetInt64(8),
            CreatedAt = r.GetInvariantText(9)
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
            EntryDate = r.GetInvariantText(6, DbTextKind.Date) ?? "",
            StartTime = r.GetInvariantText(7),
            EndTime = r.GetInvariantText(8),
            DurationHours = r.IsDBNull(9) ? 0 : r.GetDouble(9),
            IsRunning = !r.IsDBNull(10) && r.GetInt64(10) == 1,
            CreatedAt = r.GetInvariantText(11)
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
                EntryDate = r.GetInvariantText(6, DbTextKind.Date) ?? "",
                StartTime = r.GetInvariantText(7),
                EndTime = r.GetInvariantText(8),
                DurationHours = r.IsDBNull(9) ? 0 : r.GetDouble(9),
                IsRunning = !r.IsDBNull(10) && r.GetInt64(10) == 1,
                CreatedAt = r.GetInvariantText(11)
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

    public double GetHoursBookedThisMonth() {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"SELECT COALESCE(SUM(duration_hours),0)
            FROM time_entries
            WHERE is_running=0
              AND entry_date >= @start
              AND entry_date <= @end";
        var now = DateTime.Today;
        var start = new DateTime(now.Year, now.Month, 1).ToString("yyyy-MM-dd");
        var end = new DateTime(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month)).ToString("yyyy-MM-dd");
        cmd.Parameters.AddWithValue("@start", start);
        cmd.Parameters.AddWithValue("@end", end);
        var result = cmd.ExecuteScalar();
        return result != null ? Convert.ToDouble(result, CultureInfo.InvariantCulture) : 0;
    }

    public int CountRunningTimeEntries() {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM time_entries WHERE is_running=1";
        var result = cmd.ExecuteScalar();
        return result != null ? Convert.ToInt32(result) : 0;
    }

    public long StartTimeEntry(long userId, long projectId, string activityType, string? description) {
        var nowUtc = DateTimeOffset.UtcNow;
        var storedNow = FormatUtcTime(nowUtc);
        using var tx = BeginWriteTransaction();
        try {
            var running = new List<(long Id, string? StartTime)>();
            using (var select = Conn.CreateCommand()) {
                select.Transaction = tx;
                select.CommandText = @"SELECT id,start_time FROM time_entries
                    WHERE user_id=@uid AND is_running=1"
                    + (_dialect is MariaDbDialect ? " FOR UPDATE" : "");
                select.Parameters.AddWithValue("@uid", userId);
                using var reader = select.ExecuteReader();
                while (reader.Read())
                    running.Add((reader.GetInt64(0), reader.GetInvariantText(1)));
            }

            foreach (var entry in running) {
                using var stop = Conn.CreateCommand();
                stop.Transaction = tx;
                stop.CommandText = @"UPDATE time_entries
                    SET is_running=0,end_time=@now,duration_hours=@duration
                    WHERE id=@id AND is_running=1";
                stop.Parameters.AddWithValue("@id", entry.Id);
                stop.Parameters.AddWithValue("@now", storedNow);
                stop.Parameters.AddWithValue("@duration", CalculateStoredDurationHours(entry.StartTime, nowUtc));
                stop.ExecuteNonQuery();
            }

            using (var cmd = Conn.CreateCommand()) {
                cmd.Transaction = tx;
                cmd.CommandText = @"INSERT INTO time_entries(user_id,project_id,activity_type,description,entry_date,start_time,is_running,created_at)
                    VALUES(@uid,@pid,@act,@desc,@date,@start,1,CURRENT_TIMESTAMP)";
                cmd.Parameters.AddWithValue("@uid", userId);
                cmd.Parameters.AddWithValue("@pid", projectId);
                cmd.Parameters.AddWithValue("@act", activityType);
                cmd.Parameters.AddWithValue("@desc", (object?)description ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@date", nowUtc.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("@start", storedNow);
                cmd.ExecuteNonQuery();
            }

            var id = LastInsertId(tx);
            tx.Commit();
            return id;
        }
        catch {
            TryRollback(tx);
            throw;
        }
    }

    public void StopTimeEntry(long id) {
        var nowUtc = DateTimeOffset.UtcNow;
        using var tx = BeginWriteTransaction();
        try {
            string? startTime;
            using (var select = Conn.CreateCommand()) {
                select.Transaction = tx;
                select.CommandText = "SELECT start_time FROM time_entries WHERE id=@id AND is_running=1"
                    + (_dialect is MariaDbDialect ? " FOR UPDATE" : "");
                select.Parameters.AddWithValue("@id", id);
                var value = select.ExecuteScalar();
                if (value == null || value == DBNull.Value) {
                    tx.Commit();
                    return;
                }
                startTime = DbExtensions.FormatInvariantText(value);
            }

            using var cmd = Conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"UPDATE time_entries
                SET is_running=0,end_time=@now,duration_hours=@duration
                WHERE id=@id AND is_running=1";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@now", FormatUtcTime(nowUtc));
            cmd.Parameters.AddWithValue("@duration", CalculateStoredDurationHours(startTime, nowUtc));
            cmd.ExecuteNonQuery();
            tx.Commit();
        }
        catch {
            TryRollback(tx);
            throw;
        }
    }

    static string FormatUtcTime(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    static double CalculateStoredDurationHours(string? storedStart, DateTimeOffset endUtc) {
        if (!TryParseStoredTimeUtc(storedStart, out var startUtc))
            return 0;
        return Math.Max(0, (endUtc.ToUniversalTime() - startUtc).TotalHours);
    }

    internal static bool TryParseStoredTimeUtc(string? value, out DateTimeOffset utcValue) {
        utcValue = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim();
        if (normalized.EndsWith('Z')
            && DateTimeOffset.TryParse(normalized, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal |
                DateTimeStyles.AdjustToUniversal, out var explicitUtc)) {
            utcValue = explicitUtc.ToUniversalTime();
            return true;
        }

        // Legacy rows were stored as local wall-clock time without an offset.
        if (!DateTime.TryParse(normalized, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var legacyLocal))
            return false;

        legacyLocal = DateTime.SpecifyKind(legacyLocal, DateTimeKind.Unspecified);
        try {
            utcValue = new DateTimeOffset(
                TimeZoneInfo.ConvertTimeToUtc(legacyLocal, TimeZoneInfo.Local),
                TimeSpan.Zero);
            return true;
        }
        catch (ArgumentException) {
            // A legacy timestamp can fall into the local DST gap. Treat it as
            // local using the applicable offset rather than breaking timers.
            utcValue = new DateTimeOffset(legacyLocal, TimeZoneInfo.Local.GetUtcOffset(legacyLocal))
                .ToUniversalTime();
            return true;
        }
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

    public long? GetCategoryId(string name) {
        if (string.IsNullOrWhiteSpace(name)) return null;
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM categories WHERE name=@name";
        cmd.Parameters.AddWithValue("@name", name);
        var value = cmd.ExecuteScalar();
        return value != null && value != DBNull.Value ? Convert.ToInt64(value) : null;
    }

    public string? GetCategoryName(long? id) {
        if (id == null) return null;
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM categories WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", id.Value);
        var value = cmd.ExecuteScalar();
        return value != null && value != DBNull.Value ? value.ToString() : null;
    }

    // Users
    sealed record AuthUser(
        long Id,
        string Username,
        string PasswordHash,
        bool IsActive,
        string SecurityStamp,
        long? RoleId);

    sealed record AuthorizationUser(long Id, string Username, bool IsActive, long? RoleId);
    sealed record AuthorizationPermission(long RoleId, string PageKey, string AccessLevel);

    static string NormalizeUsernameForLookup(string? username) => (username ?? "").Trim();

    static string ValidateNewUsername(string? username) {
        var normalized = NormalizeUsernameForLookup(username);
        if (normalized.Length == 0)
            throw new ArgumentException("Benutzername darf nicht leer sein.", nameof(username));
        if (normalized.Length > 191)
            throw new ArgumentException("Der Benutzername darf höchstens 191 Zeichen lang sein.", nameof(username));
        if (normalized.Any(char.IsControl))
            throw new ArgumentException("Der Benutzername enthält unzulässige Steuerzeichen.", nameof(username));
        if (string.Equals(normalized, "admin", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Der Benutzername 'admin' ist reserviert.");
        return normalized;
    }

    static void ValidatePasswordPolicy(string password, string username) {
        if (!Services.PasswordPolicy.TryValidate(password, username, out var error))
            throw new ArgumentException(error, nameof(password));
    }

    AuthUser? FindUserExact(string username, DbTransaction? tx = null, bool forUpdate = false) {
        using var cmd = Conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"SELECT id,username,password_hash,is_active,security_stamp,role_id
            FROM users WHERE username=@u" +
            (forUpdate && _dialect is MariaDbDialect ? " FOR UPDATE" : "");
        cmd.Parameters.AddWithValue("@u", username);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) {
            var storedUsername = reader.IsDBNull(1) ? "" : reader.GetString(1);
            if (!string.Equals(storedUsername, username, StringComparison.Ordinal))
                continue;

            return new AuthUser(
                reader.GetInt64(0),
                storedUsername,
                reader.IsDBNull(2) ? "" : reader.GetString(2),
                !reader.IsDBNull(3) && Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture) != 0,
                reader.IsDBNull(4) ? "" : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetInt64(5));
        }
        return null;
    }

    AuthUser? FindUserById(long userId, DbTransaction? tx = null, bool forUpdate = false) {
        if (userId <= 0)
            return null;

        using var cmd = Conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"SELECT id,username,password_hash,is_active,security_stamp,role_id
            FROM users WHERE id=@id" +
            (forUpdate && _dialect is MariaDbDialect ? " FOR UPDATE" : "");
        cmd.Parameters.AddWithValue("@id", userId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;

        return new AuthUser(
            reader.GetInt64(0),
            reader.IsDBNull(1) ? "" : reader.GetString(1),
            reader.IsDBNull(2) ? "" : reader.GetString(2),
            !reader.IsDBNull(3) && Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture) != 0,
            reader.IsDBNull(4) ? "" : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetInt64(5));
    }

    UserSessionState BuildSessionState(AuthUser user, DbTransaction transaction) => new() {
        UserId = user.Id,
        Username = user.Username,
        IsActive = user.IsActive,
        SecurityStamp = user.SecurityStamp,
        Permissions = LoadRolePermissionMap(user.RoleId, transaction)
    };

    void EnsureUsernameAvailable(string username, DbTransaction tx) {
        using var cmd = Conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT username FROM users ORDER BY id" +
            (_dialect is MariaDbDialect ? " FOR UPDATE" : "");
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) {
            if (string.Equals(reader.GetString(0), username, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Der Benutzername '{username}' ist bereits vergeben.");
        }
    }

    public UserSessionState? GetUserSessionState(long userId) {
        if (userId <= 0)
            return null;

        using var tx = Conn.BeginTransaction();
        try {
            long? roleId;
            string username;
            bool isActive;
            string securityStamp;
            using (var cmd = Conn.CreateCommand()) {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT username,is_active,security_stamp,role_id FROM users WHERE id=@id";
                cmd.Parameters.AddWithValue("@id", userId);
                using var reader = cmd.ExecuteReader();
                if (!reader.Read()) {
                    tx.Commit();
                    return null;
                }
                username = reader.IsDBNull(0) ? "" : reader.GetString(0);
                isActive = !reader.IsDBNull(1)
                    && Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture) != 0;
                securityStamp = reader.IsDBNull(2) ? "" : reader.GetString(2);
                roleId = reader.IsDBNull(3) ? null : reader.GetInt64(3);
            }

            var state = new UserSessionState {
                UserId = userId,
                Username = username,
                IsActive = isActive,
                SecurityStamp = securityStamp,
                Permissions = LoadRolePermissionMap(roleId, tx)
            };
            tx.Commit();
            return state;
        }
        catch {
            TryRollback(tx);
            throw;
        }
    }

    public UserSessionState? AuthenticateUser(string username, string password) {
        username = NormalizeUsernameForLookup(username);
        if (username.Length == 0 || password is null)
            return null;

        AuthUser? authenticatedUser;
        UserSessionState? state;
        using (var tx = Conn.BeginTransaction()) {
            try {
                authenticatedUser = FindUserExact(username, tx);
                if (authenticatedUser is null || !authenticatedUser.IsActive ||
                    !Services.PasswordHasher.Verify(password, authenticatedUser.PasswordHash)) {
                    tx.Commit();
                    return null;
                }

                // Password, active flag, security stamp and effective permissions are
                // captured from one database snapshot. A concurrent credential or
                // permission change therefore invalidates this returned stamp instead
                // of silently granting the new session state to an old password.
                state = BuildSessionState(authenticatedUser, tx);
                tx.Commit();
            }
            catch {
                TryRollback(tx);
                throw;
            }
        }

        // Preserve the legacy-password migration without changing the authenticated
        // security stamp. The conditional update refuses to overwrite a concurrent
        // password change.
        if (Services.PasswordHasher.IsLegacyFormat(authenticatedUser.PasswordHash))
            RehashLegacyPassword(authenticatedUser.Id, authenticatedUser.PasswordHash, password);
        return state;
    }

    public bool ValidateUser(string username, string password) =>
        AuthenticateUser(username, password) is not null;

    void RehashLegacyPassword(long userId, string expectedHash, string password) {
        var replacementHash = Services.PasswordHasher.Hash(password);
        using var tx = BeginWriteTransaction();
        try {
            string? currentHash = null;
            bool isActive = false;
            using (var find = Conn.CreateCommand()) {
                find.Transaction = tx;
                find.CommandText = "SELECT password_hash,is_active FROM users WHERE id=@id" +
                    (_dialect is MariaDbDialect ? " FOR UPDATE" : "");
                find.Parameters.AddWithValue("@id", userId);
                using var reader = find.ExecuteReader();
                if (reader.Read()) {
                    currentHash = reader.IsDBNull(0) ? "" : reader.GetString(0);
                    isActive = !reader.IsDBNull(1)
                        && Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture) != 0;
                }
            }

            if (isActive
                && currentHash is not null
                && string.Equals(currentHash, expectedHash, StringComparison.Ordinal)
                && Services.PasswordHasher.IsLegacyFormat(currentHash)) {
                using var update = Conn.CreateCommand();
                update.Transaction = tx;
                update.CommandText = "UPDATE users SET password_hash=@hash WHERE id=@id";
                update.Parameters.AddWithValue("@hash", replacementHash);
                update.Parameters.AddWithValue("@id", userId);
                update.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch {
            TryRollback(tx);
            throw;
        }
    }

    public long GetUserId(string username) {
        username = NormalizeUsernameForLookup(username);
        var user = username.Length == 0 ? null : FindUserExact(username);
        return user is { IsActive: true } ? user.Id : 0;
    }

    public List<string> GetUsernames() {
        var list = new List<string>();
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT username FROM users WHERE is_active=1 ORDER BY username";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    public UserSessionState ChangePassword(
        long userId,
        string expectedSecurityStamp,
        string currentPassword,
        string newPassword) {
        if (userId <= 0)
            throw new ArgumentOutOfRangeException(nameof(userId));
        if (string.IsNullOrWhiteSpace(expectedSecurityStamp))
            throw new UnauthorizedAccessException("Die Sitzung ist nicht mehr gültig.");
        if (currentPassword is null)
            throw new ArgumentNullException(nameof(currentPassword));

        var passwordHash = Services.PasswordHasher.Hash(newPassword);

        using var tx = BeginWriteTransaction();
        try {
            var user = FindUserById(userId, tx, forUpdate: true)
                ?? throw new UnauthorizedAccessException("Die Sitzung ist nicht mehr gültig.");
            if (!user.IsActive ||
                !string.Equals(user.SecurityStamp, expectedSecurityStamp, StringComparison.Ordinal) ||
                !Services.PasswordHasher.Verify(currentPassword, user.PasswordHash))
                throw new UnauthorizedAccessException("Die Sitzung oder das aktuelle Passwort ist nicht mehr gültig.");

            ValidatePasswordPolicy(newPassword, user.Username);
            var newStamp = CreateSecurityStamp();
            using var cmd = Conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"UPDATE users SET password_hash=@password,security_stamp=@stamp
                WHERE id=@id AND is_active=1 AND security_stamp=@expectedStamp";
            cmd.Parameters.AddWithValue("@password", passwordHash);
            cmd.Parameters.AddWithValue("@stamp", newStamp);
            cmd.Parameters.AddWithValue("@id", user.Id);
            cmd.Parameters.AddWithValue("@expectedStamp", expectedSecurityStamp);
            if (cmd.ExecuteNonQuery() != 1)
                throw new UnauthorizedAccessException("Die Sitzung ist nicht mehr gültig.");

            var state = BuildSessionState(user with {
                PasswordHash = passwordHash,
                SecurityStamp = newStamp
            }, tx);
            tx.Commit();
            return state;
        }
        catch {
            TryRollback(tx);
            throw;
        }
    }

    public void AddUser(string username, string password, string fullName) {
        username = ValidateNewUsername(username);
        AddUserCore(username, password, fullName, passwordPolicyBypass: false);
    }

    void AddSeedUser(string username, string password, string fullName) {
        username = ValidateNewUsername(username);
        AddUserCore(username, password, fullName, passwordPolicyBypass: true);
    }

    void AddUserCore(
        string username,
        string password,
        string? fullName,
        bool passwordPolicyBypass) {
        if (!passwordPolicyBypass)
            ValidatePasswordPolicy(password, username);
        if (password is null)
            throw new ArgumentNullException(nameof(password));

        var passwordHash = Services.PasswordHasher.Hash(password);
        using var tx = BeginWriteTransaction();
        try {
            EnsureUsernameAvailable(username, tx);
            using var cmd = Conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO users(
                    username,password_hash,full_name,is_active,security_stamp)
                VALUES(@username,@password,@fullName,1,@stamp)";
            cmd.Parameters.AddWithValue("@username", username);
            cmd.Parameters.AddWithValue("@password", passwordHash);
            cmd.Parameters.AddWithValue("@fullName", (fullName ?? "").Trim());
            cmd.Parameters.AddWithValue("@stamp", CreateSecurityStamp());
            cmd.ExecuteNonQuery();
            tx.Commit();
        }
        catch {
            TryRollback(tx);
            throw;
        }
    }

    public Resource AddUserWithResource(string username, string password, string fullName) {
        username = ValidateNewUsername(username);
        fullName = fullName?.Trim() ?? "";
        ValidatePasswordPolicy(password, username);

        var passwordHash = Services.PasswordHasher.Hash(password);
        var resourceName = string.IsNullOrWhiteSpace(fullName) ? username : fullName;

        using var tx = BeginWriteTransaction();
        try {
            EnsureUsernameAvailable(username, tx);

            var matchingResources = LoadUnlinkedResources(tx)
                .Where(resource => string.Equals(
                    resource.Name.Trim(), resourceName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matchingResources.Count > 1) {
                throw new InvalidOperationException(
                    $"Mehrere nicht verknüpfte Mitarbeiter heißen '{resourceName}'. " +
                    "Bitte bereinigen Sie die doppelten Mitarbeitereinträge vor der Benutzeranlage.");
            }

            long userId;
            using (var insertUser = Conn.CreateCommand()) {
                insertUser.Transaction = tx;
                insertUser.CommandText = @"INSERT INTO users(
                        username,password_hash,full_name,is_active,security_stamp)
                    VALUES(@u,@p,@f,1,@stamp)";
                insertUser.Parameters.AddWithValue("@u", username);
                insertUser.Parameters.AddWithValue("@p", passwordHash);
                insertUser.Parameters.AddWithValue("@f", fullName);
                insertUser.Parameters.AddWithValue("@stamp", CreateSecurityStamp());
                insertUser.ExecuteNonQuery();
                userId = LastInsertId(tx);
            }

            var resource = new Resource {
                UserId = userId,
                Name = resourceName,
                Role = "",
                Availability = 1.0,
                HourlyRate = 0,
                WorkStartHour = 8,
                WorkEndHour = 17
            };

            if (matchingResources.Count == 1) {
                resource = matchingResources[0];
                using var linkResource = Conn.CreateCommand();
                linkResource.Transaction = tx;
                linkResource.CommandText = "UPDATE resources SET user_id=@uid WHERE id=@rid AND user_id IS NULL";
                linkResource.Parameters.AddWithValue("@uid", userId);
                linkResource.Parameters.AddWithValue("@rid", resource.Id);
                if (linkResource.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("Der vorhandene Mitarbeiter wurde zwischenzeitlich verknüpft.");
                resource.UserId = userId;
            }
            else {
                using var insertResource = Conn.CreateCommand();
                insertResource.Transaction = tx;
                insertResource.CommandText = @"INSERT INTO resources(user_id,name,role,availability,hourly_rate,work_start_hour,work_end_hour,created_at)
                    VALUES(@uid,@n,@r,@a,@hr,@ws,@we,CURRENT_TIMESTAMP)";
                insertResource.Parameters.AddWithValue("@uid", userId);
                insertResource.Parameters.AddWithValue("@n", resource.Name);
                insertResource.Parameters.AddWithValue("@r", resource.Role);
                insertResource.Parameters.AddWithValue("@a", resource.Availability);
                insertResource.Parameters.AddWithValue("@hr", resource.HourlyRate);
                insertResource.Parameters.AddWithValue("@ws", resource.WorkStartHour);
                insertResource.Parameters.AddWithValue("@we", resource.WorkEndHour);
                insertResource.ExecuteNonQuery();
                resource.Id = LastInsertId(tx);
            }

            tx.Commit();
            return resource;
        }
        catch {
            try { tx.Rollback(); } catch { }
            throw;
        }
    }

    private List<Resource> LoadUnlinkedResources(DbTransaction tx) {
        var resources = new List<Resource>();
        using var cmd = Conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"SELECT id,name,role,availability,hourly_rate,work_start_hour,work_end_hour,created_at,avatar_data
            FROM resources WHERE user_id IS NULL" + (_dialect is MariaDbDialect ? " FOR UPDATE" : "");
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) {
            resources.Add(new Resource {
                Id = reader.GetInt64(0),
                Name = reader.GetString(1),
                Role = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Availability = reader.IsDBNull(3) ? 1.0 : reader.GetDouble(3),
                HourlyRate = reader.IsDBNull(4) ? 0 : reader.GetDouble(4),
                WorkStartHour = reader.IsDBNull(5) ? 8 : reader.GetInt32(5),
                WorkEndHour = reader.IsDBNull(6) ? 17 : reader.GetInt32(6),
                CreatedAt = reader.GetInvariantText(7) ?? "",
                AvatarData = reader.IsDBNull(8) ? null : reader.GetString(8)
            });
        }
        return resources;
    }

    /// <summary>
    /// Idempotently links legacy users to existing employees by an unambiguous
    /// display-name match. If no employee with that name exists, each user gets
    /// a distinct linked employee. Ambiguous existing matches are deliberately
    /// left untouched for manual resolution.
    /// Existing rows and their allocation history are never replaced or deleted.
    /// </summary>
    private void BackfillResourceUserLinks() {
        using var tx = BeginWriteTransaction();
        try {
            var users = new List<(long Id, string Username, string DisplayName)>();
            using (var usersCmd = Conn.CreateCommand()) {
                usersCmd.Transaction = tx;
                usersCmd.CommandText = "SELECT id,username,full_name FROM users" +
                    (_dialect is MariaDbDialect ? " FOR UPDATE" : "");
                using var reader = usersCmd.ExecuteReader();
                while (reader.Read()) {
                    var username = reader.GetString(1).Trim();
                    var fullName = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim();
                    users.Add((reader.GetInt64(0), username,
                        string.IsNullOrWhiteSpace(fullName) ? username : fullName));
                }
            }

            var linkedUserIds = new HashSet<long>();
            using (var linkedCmd = Conn.CreateCommand()) {
                linkedCmd.Transaction = tx;
                linkedCmd.CommandText = "SELECT user_id FROM resources WHERE user_id IS NOT NULL" +
                    (_dialect is MariaDbDialect ? " FOR UPDATE" : "");
                using var reader = linkedCmd.ExecuteReader();
                while (reader.Read())
                    linkedUserIds.Add(reader.GetInt64(0));
            }

            var usersByName = users
                .Where(user => !linkedUserIds.Contains(user.Id) &&
                    !user.Username.Equals("admin", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(user.DisplayName))
                .GroupBy(user => user.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

            var unlinkedResources = LoadUnlinkedResources(tx);
            var resourcesByName = unlinkedResources
                .Where(resource => !string.IsNullOrWhiteSpace(resource.Name))
                .GroupBy(resource => resource.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

            foreach (var (displayName, matchingUsers) in usersByName) {
                if (resourcesByName.TryGetValue(displayName, out var matches)) {
                    // Only a single user and a single existing employee form an
                    // unambiguous legacy match. Every other existing-name case
                    // needs explicit administrative resolution.
                    if (matchingUsers.Count != 1 || matches.Count != 1)
                        continue;

                    using var link = Conn.CreateCommand();
                    link.Transaction = tx;
                    link.CommandText = "UPDATE resources SET user_id=@uid WHERE id=@rid AND user_id IS NULL";
                    link.Parameters.AddWithValue("@uid", matchingUsers[0].Id);
                    link.Parameters.AddWithValue("@rid", matches[0].Id);
                    if (link.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException($"Mitarbeiter #{matches[0].Id} konnte nicht mit Benutzer #{matchingUsers[0].Id} verknüpft werden.");
                    continue;
                }

                // No legacy employee uses this name. Provision one distinct
                // linked employee per user, even when several users intentionally
                // share the same display name.
                foreach (var user in matchingUsers) {
                    using var insert = Conn.CreateCommand();
                    insert.Transaction = tx;
                    insert.CommandText = @"INSERT INTO resources(user_id,name,role,availability,hourly_rate,work_start_hour,work_end_hour,created_at)
                        VALUES(@uid,@n,'',1.0,0,8,17,CURRENT_TIMESTAMP)";
                    insert.Parameters.AddWithValue("@uid", user.Id);
                    insert.Parameters.AddWithValue("@n", displayName);
                    insert.ExecuteNonQuery();
                }
            }

            tx.Commit();
        }
        catch {
            try { tx.Rollback(); } catch { }
            throw;
        }
    }

    public void DeleteUser(string username) {
        DeactivateUser(username);
    }

    public void DeactivateUser(string username) {
        username = NormalizeUsernameForLookup(username);
        if (username.Length == 0)
            throw new ArgumentException("Benutzername darf nicht leer sein.", nameof(username));
        if (string.Equals(username, "admin", StringComparison.Ordinal))
            throw new InvalidOperationException("Der reservierte Administrator kann nicht deaktiviert werden.");
        if (string.Equals(username, global::CashFlowPlannerPro.App.CurrentUsername, StringComparison.Ordinal))
            throw new InvalidOperationException("Sie können Ihr eigenes Benutzerkonto nicht deaktivieren.");

        using var tx = BeginWriteTransaction();
        try {
            var (users, permissions) = LoadAuthorizationState(tx);
            var user = users.FirstOrDefault(candidate =>
                string.Equals(candidate.Username, username, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Benutzer '{username}' wurde nicht gefunden.");
            if (!user.IsActive) {
                tx.Commit();
                return;
            }

            var resultingUsers = users
                .Select(candidate => candidate.Id == user.Id ? candidate with { IsActive = false } : candidate)
                .ToList();
            EnsureActiveAdministratorRemains(resultingUsers, permissions);

            using var update = Conn.CreateCommand();
            update.Transaction = tx;
            update.CommandText = "UPDATE users SET is_active=0,security_stamp=@stamp WHERE id=@id";
            update.Parameters.AddWithValue("@stamp", CreateSecurityStamp());
            update.Parameters.AddWithValue("@id", user.Id);
            if (update.ExecuteNonQuery() != 1)
                throw new InvalidOperationException($"Benutzer '{username}' konnte nicht deaktiviert werden.");
            tx.Commit();
        }
        catch {
            TryRollback(tx);
            throw;
        }
    }

    public void UpdateUserFullName(string username, string fullName) {
        username = username?.Trim() ?? "";
        fullName = fullName?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Benutzername darf nicht leer sein.", nameof(username));
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("Der vollständige Name darf nicht leer sein.", nameof(fullName));

        using var tx = BeginWriteTransaction();
        try {
            var userId = FindUserExact(username, tx, forUpdate: true)?.Id
                ?? throw new InvalidOperationException($"Benutzer '{username}' wurde nicht gefunden.");

            using (var updateUser = Conn.CreateCommand()) {
                updateUser.Transaction = tx;
                updateUser.CommandText = "UPDATE users SET full_name=@f WHERE id=@id";
                updateUser.Parameters.AddWithValue("@id", userId);
                updateUser.Parameters.AddWithValue("@f", fullName);
                if (updateUser.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException($"Benutzer '{username}' konnte nicht aktualisiert werden.");
            }

            using (var updateResource = Conn.CreateCommand()) {
                updateResource.Transaction = tx;
                updateResource.CommandText = "UPDATE resources SET name=@n WHERE user_id=@uid";
                updateResource.Parameters.AddWithValue("@uid", userId);
                updateResource.Parameters.AddWithValue("@n", fullName);
                updateResource.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch {
            try { tx.Rollback(); } catch { }
            throw;
        }
    }

    public string? GetFullName(string username) {
        username = NormalizeUsernameForLookup(username);
        var user = username.Length == 0 ? null : FindUserExact(username);
        if (user is not { IsActive: true })
            return null;
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT full_name FROM users WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", user.Id);
        return cmd.ExecuteScalar() as string;
    }

    public void SetUserRole(string username, long? roleId) {
        username = NormalizeUsernameForLookup(username);
        if (username.Length == 0)
            throw new ArgumentException("Benutzername darf nicht leer sein.", nameof(username));
        if (roleId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(roleId));

        using var tx = BeginWriteTransaction();
        try {
            var (users, permissions) = LoadAuthorizationState(tx);
            var user = users.FirstOrDefault(candidate =>
                string.Equals(candidate.Username, username, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Benutzer '{username}' wurde nicht gefunden.");
            EnsureRoleExists(roleId, tx);

            var oldPermissions = BuildRolePermissionMap(user.RoleId, permissions);
            var newPermissions = BuildRolePermissionMap(roleId, permissions);
            if (string.Equals(username, global::CashFlowPlannerPro.App.CurrentUsername, StringComparison.Ordinal)
                && HasPermissionReduction(oldPermissions, newPermissions)) {
                throw new InvalidOperationException("Sie können Ihre eigenen Berechtigungen nicht herabsetzen.");
            }
            if (string.Equals(username, "admin", StringComparison.Ordinal)
                && !HasFullAdminPermission(roleId, permissions)) {
                throw new InvalidOperationException("Der reservierte Administrator muss vollständigen Admin-Zugriff behalten.");
            }

            var resultingUsers = users
                .Select(candidate => candidate.Id == user.Id ? candidate with { RoleId = roleId } : candidate)
                .ToList();
            EnsureActiveAdministratorRemains(resultingUsers, permissions);

            using var cmd = Conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE users SET role_id=@role,security_stamp=@stamp WHERE id=@id";
            cmd.Parameters.AddWithValue("@id", user.Id);
            cmd.Parameters.AddWithValue("@role", roleId.HasValue ? roleId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@stamp", CreateSecurityStamp());
            if (cmd.ExecuteNonQuery() != 1)
                throw new InvalidOperationException($"Die Rolle für '{username}' konnte nicht geändert werden.");
            tx.Commit();
        }
        catch {
            TryRollback(tx);
            throw;
        }
    }

    public long? GetUserRoleId(string username) {
        username = NormalizeUsernameForLookup(username);
        var user = username.Length == 0 ? null : FindUserExact(username);
        return user is { IsActive: true } ? user.RoleId : null;
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
        ArgumentNullException.ThrowIfNull(role);
        var normalizedName = ValidateRoleName(role.Name);
        if (string.Equals(normalizedName, "Admin", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Der Rollenname 'Admin' ist reserviert.");
        var description = (role.Description ?? "").Trim();

        using var tx = BeginWriteTransaction();
        try {
            var roles = LoadRoleIdentities(tx);
            EnsureRoleNameAvailable(normalizedName, roles, exceptRoleId: null);
            using var cmd = Conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO roles(name,description) VALUES(@name,@description)";
            cmd.Parameters.AddWithValue("@name", normalizedName);
            cmd.Parameters.AddWithValue("@description", description);
            cmd.ExecuteNonQuery();
            var roleId = LastInsertId(tx);
            tx.Commit();
            role.Id = roleId;
            role.Name = normalizedName;
            role.Description = description;
        }
        catch {
            TryRollback(tx);
            throw;
        }
    }

    public void UpdateRole(Role role) {
        ArgumentNullException.ThrowIfNull(role);
        if (role.Id <= 0)
            throw new ArgumentOutOfRangeException(nameof(role));
        var normalizedName = ValidateRoleName(role.Name);
        var description = (role.Description ?? "").Trim();

        using var tx = BeginWriteTransaction();
        try {
            var roles = LoadRoleIdentities(tx);
            var existingRole = roles.FirstOrDefault(candidate => candidate.Id == role.Id)
                ?? throw new InvalidOperationException($"Rolle #{role.Id} wurde nicht gefunden.");
            if (string.Equals(existingRole.Name, "Admin", StringComparison.Ordinal)
                && !string.Equals(normalizedName, "Admin", StringComparison.Ordinal)) {
                throw new InvalidOperationException("Die reservierte Adminrolle kann nicht umbenannt werden.");
            }
            if (!string.Equals(existingRole.Name, "Admin", StringComparison.Ordinal)
                && string.Equals(normalizedName, "Admin", StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException("Der Rollenname 'Admin' ist reserviert.");
            }
            EnsureRoleNameAvailable(normalizedName, roles, role.Id);

            using var cmd = Conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE roles SET name=@name,description=@description WHERE id=@id";
            cmd.Parameters.AddWithValue("@id", role.Id);
            cmd.Parameters.AddWithValue("@name", normalizedName);
            cmd.Parameters.AddWithValue("@description", description);
            if (cmd.ExecuteNonQuery() != 1)
                throw new InvalidOperationException($"Rolle #{role.Id} konnte nicht aktualisiert werden.");
            tx.Commit();
            role.Name = normalizedName;
            role.Description = description;
        }
        catch {
            TryRollback(tx);
            throw;
        }
    }

    public void DeleteRole(long id) {
        if (id <= 0)
            throw new ArgumentOutOfRangeException(nameof(id));

        using var tx = BeginWriteTransaction();
        try {
            var (users, permissions) = LoadAuthorizationState(tx);
            var roles = LoadRoleIdentities(tx);
            var role = roles.FirstOrDefault(candidate => candidate.Id == id)
                ?? throw new InvalidOperationException($"Rolle #{id} wurde nicht gefunden.");
            if (string.Equals(role.Name, "Admin", StringComparison.Ordinal))
                throw new InvalidOperationException("Die reservierte Adminrolle kann nicht gelöscht werden.");

            var affectedUsers = users.Where(user => user.RoleId == id).ToList();
            if (affectedUsers.Any(user => user.IsActive
                && string.Equals(user.Username, global::CashFlowPlannerPro.App.CurrentUsername, StringComparison.Ordinal))) {
                throw new InvalidOperationException("Sie können Ihre eigene aktive Rolle nicht löschen.");
            }

            var resultingUsers = users
                .Select(user => user.RoleId == id ? user with { RoleId = null } : user)
                .ToList();
            var resultingPermissions = permissions
                .Where(permission => permission.RoleId != id)
                .ToList();
            EnsureActiveAdministratorRemains(resultingUsers, resultingPermissions);

            foreach (var affectedUser in affectedUsers) {
                using var unlink = Conn.CreateCommand();
                unlink.Transaction = tx;
                unlink.CommandText = "UPDATE users SET role_id=NULL,security_stamp=@stamp WHERE id=@id";
                unlink.Parameters.AddWithValue("@stamp", CreateSecurityStamp());
                unlink.Parameters.AddWithValue("@id", affectedUser.Id);
                if (unlink.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException($"Benutzer '{affectedUser.Username}' konnte nicht sicher von der Rolle getrennt werden.");
            }

            using (var delete = Conn.CreateCommand()) {
                delete.Transaction = tx;
                delete.CommandText = "DELETE FROM roles WHERE id=@id";
                delete.Parameters.AddWithValue("@id", id);
                if (delete.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException($"Rolle #{id} konnte nicht gelöscht werden.");
            }
            tx.Commit();
        }
        catch {
            TryRollback(tx);
            throw;
        }
    }

    sealed record RoleIdentity(long Id, string Name);

    List<RoleIdentity> LoadRoleIdentities(DbTransaction transaction) {
        var roles = new List<RoleIdentity>();
        using var cmd = Conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT id,name FROM roles ORDER BY id" +
            (_dialect is MariaDbDialect ? " FOR UPDATE" : "");
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            roles.Add(new RoleIdentity(reader.GetInt64(0), reader.IsDBNull(1) ? "" : reader.GetString(1)));
        return roles;
    }

    static string ValidateRoleName(string? roleName) {
        var normalized = (roleName ?? "").Trim();
        if (normalized.Length == 0)
            throw new ArgumentException("Der Rollenname darf nicht leer sein.", nameof(roleName));
        if (normalized.Length > 191)
            throw new ArgumentException("Der Rollenname darf höchstens 191 Zeichen lang sein.", nameof(roleName));
        if (normalized.Any(char.IsControl))
            throw new ArgumentException("Der Rollenname enthält unzulässige Steuerzeichen.", nameof(roleName));
        return normalized;
    }

    static void EnsureRoleNameAvailable(
        string roleName,
        IEnumerable<RoleIdentity> roles,
        long? exceptRoleId) {
        if (roles.Any(role => role.Id != exceptRoleId
            && string.Equals(role.Name, roleName, StringComparison.OrdinalIgnoreCase))) {
            throw new InvalidOperationException($"Der Rollenname '{roleName}' ist bereits vergeben.");
        }
    }

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
        if (roleId <= 0)
            throw new ArgumentOutOfRangeException(nameof(roleId));
        pageKey = NormalizePermissionPage(pageKey);
        accessLevel = NormalizeAccessLevel(accessLevel, rejectInvalid: true);

        using var tx = BeginWriteTransaction();
        try {
            var (users, permissions) = LoadAuthorizationState(tx);
            EnsureRoleExists(roleId, tx);

            var previousAccess = permissions
                .FirstOrDefault(permission => permission.RoleId == roleId
                    && string.Equals(permission.PageKey, pageKey, StringComparison.Ordinal))
                ?.AccessLevel ?? "none";
            var currentUser = users.FirstOrDefault(candidate =>
                string.Equals(candidate.Username, global::CashFlowPlannerPro.App.CurrentUsername, StringComparison.Ordinal));
            if (currentUser is { IsActive: true, RoleId: not null }
                && currentUser.RoleId.Value == roleId
                && AccessRank(accessLevel) < AccessRank(previousAccess)) {
                throw new InvalidOperationException("Sie können Ihre eigenen Berechtigungen nicht herabsetzen.");
            }

            var resultingPermissions = permissions
                .Where(permission => permission.RoleId != roleId
                    || !string.Equals(permission.PageKey, pageKey, StringComparison.Ordinal))
                .Append(new AuthorizationPermission(roleId, pageKey, accessLevel))
                .ToList();
            EnsureActiveAdministratorRemains(users, resultingPermissions);

            using (var cmd = Conn.CreateCommand()) {
                cmd.Transaction = tx;
                cmd.CommandText = _dialect.UpsertRolePermission();
                cmd.Parameters.AddWithValue("@rid", roleId);
                cmd.Parameters.AddWithValue("@pk", pageKey);
                cmd.Parameters.AddWithValue("@a", accessLevel);
                cmd.ExecuteNonQuery();
            }

            foreach (var affectedUser in users.Where(candidate => candidate.RoleId == roleId)) {
                using var rotate = Conn.CreateCommand();
                rotate.Transaction = tx;
                rotate.CommandText = "UPDATE users SET security_stamp=@stamp WHERE id=@id";
                rotate.Parameters.AddWithValue("@stamp", CreateSecurityStamp());
                rotate.Parameters.AddWithValue("@id", affectedUser.Id);
                if (rotate.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException($"Die Sitzung von Benutzer '{affectedUser.Username}' konnte nicht aktualisiert werden.");
            }
            tx.Commit();
        }
        catch {
            TryRollback(tx);
            throw;
        }
    }

    public string GetUserAccessLevel(string username, string pageKey) {
        pageKey = (pageKey ?? "").Trim().ToLowerInvariant();
        return GetUserPermissions(username).GetValueOrDefault(pageKey, "none");
    }

    public Dictionary<string, string> GetUserPermissions(string username) {
        username = NormalizeUsernameForLookup(username);
        if (username.Length == 0)
            return new Dictionary<string, string>(StringComparer.Ordinal);

        using var tx = Conn.BeginTransaction();
        try {
            var user = FindUserExact(username, tx);
            var permissions = user is { IsActive: true }
                ? LoadRolePermissionMap(user.RoleId, tx)
                : new Dictionary<string, string>(StringComparer.Ordinal);
            tx.Commit();
            return permissions;
        }
        catch {
            TryRollback(tx);
            throw;
        }
    }

    Dictionary<string, string> LoadRolePermissionMap(long? roleId, DbTransaction? transaction) {
        var permissions = new Dictionary<string, string>(StringComparer.Ordinal);
        if (roleId is null)
            return permissions;

        using var cmd = Conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT page_key,access_level FROM role_permissions WHERE role_id=@role ORDER BY id";
        cmd.Parameters.AddWithValue("@role", roleId.Value);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) {
            var page = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim();
            if (page.Length == 0)
                continue;
            permissions[page] = NormalizeAccessLevel(
                reader.IsDBNull(1) ? "none" : reader.GetString(1),
                rejectInvalid: false);
        }
        return permissions;
    }

    (List<AuthorizationUser> Users, List<AuthorizationPermission> Permissions)
        LoadAuthorizationState(DbTransaction transaction) {
        var users = new List<AuthorizationUser>();
        using (var cmd = Conn.CreateCommand()) {
            cmd.Transaction = transaction;
            cmd.CommandText = "SELECT id,username,is_active,role_id FROM users ORDER BY id" +
                (_dialect is MariaDbDialect ? " FOR UPDATE" : "");
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) {
                users.Add(new AuthorizationUser(
                    reader.GetInt64(0),
                    reader.IsDBNull(1) ? "" : reader.GetString(1),
                    !reader.IsDBNull(2)
                        && Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture) != 0,
                    reader.IsDBNull(3) ? null : reader.GetInt64(3)));
            }
        }

        var permissions = new List<AuthorizationPermission>();
        using (var cmd = Conn.CreateCommand()) {
            cmd.Transaction = transaction;
            cmd.CommandText = "SELECT role_id,page_key,access_level FROM role_permissions ORDER BY role_id,id" +
                (_dialect is MariaDbDialect ? " FOR UPDATE" : "");
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) {
                permissions.Add(new AuthorizationPermission(
                    reader.GetInt64(0),
                    reader.IsDBNull(1) ? "" : reader.GetString(1).Trim(),
                    NormalizeAccessLevel(reader.IsDBNull(2) ? "none" : reader.GetString(2), rejectInvalid: false)));
            }
        }
        return (users, permissions);
    }

    void EnsureRoleExists(long? roleId, DbTransaction transaction) {
        if (roleId is null)
            return;
        using var cmd = Conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT id FROM roles WHERE id=@id" +
            (_dialect is MariaDbDialect ? " FOR UPDATE" : "");
        cmd.Parameters.AddWithValue("@id", roleId.Value);
        if (cmd.ExecuteScalar() is null or DBNull)
            throw new InvalidOperationException($"Rolle #{roleId.Value} wurde nicht gefunden.");
    }

    static Dictionary<string, string> BuildRolePermissionMap(
        long? roleId,
        IEnumerable<AuthorizationPermission> permissions) {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (roleId is null)
            return result;
        foreach (var permission in permissions.Where(candidate => candidate.RoleId == roleId.Value))
            result[permission.PageKey] = NormalizeAccessLevel(permission.AccessLevel, rejectInvalid: false);
        return result;
    }

    static bool HasPermissionReduction(
        IReadOnlyDictionary<string, string> previous,
        IReadOnlyDictionary<string, string> replacement) {
        foreach (var page in previous.Keys.Concat(replacement.Keys).Distinct(StringComparer.Ordinal)) {
            if (AccessRank(replacement.GetValueOrDefault(page, "none"))
                < AccessRank(previous.GetValueOrDefault(page, "none")))
                return true;
        }
        return false;
    }

    static bool HasFullAdminPermission(
        long? roleId,
        IEnumerable<AuthorizationPermission> permissions) =>
        roleId.HasValue && permissions.Any(permission =>
            permission.RoleId == roleId.Value
            && string.Equals(permission.PageKey, "admin", StringComparison.Ordinal)
            && string.Equals(permission.AccessLevel, "full", StringComparison.Ordinal));

    static void EnsureActiveAdministratorRemains(
        IEnumerable<AuthorizationUser> users,
        IEnumerable<AuthorizationPermission> permissions) {
        if (!users.Any(user => user.IsActive && HasFullAdminPermission(user.RoleId, permissions)))
            throw new InvalidOperationException("Mindestens ein aktiver Benutzer mit vollständigem Admin-Zugriff muss erhalten bleiben.");
    }

    static string NormalizePermissionPage(string? pageKey) {
        var normalized = (pageKey ?? "").Trim().ToLowerInvariant();
        if (!KnownPermissionPages.Contains(normalized))
            throw new ArgumentException("Unbekannter Berechtigungsbereich.", nameof(pageKey));
        return normalized;
    }

    static string NormalizeAccessLevel(string? accessLevel, bool rejectInvalid) {
        var normalized = (accessLevel ?? "").Trim().ToLowerInvariant();
        if (normalized is "none" or "read" or "full")
            return normalized;
        if (rejectInvalid)
            throw new ArgumentException("Die Zugriffsstufe muss 'none', 'read' oder 'full' sein.", nameof(accessLevel));
        return "none";
    }

    static int AccessRank(string? accessLevel) =>
        NormalizeAccessLevel(accessLevel, rejectInvalid: false) switch {
            "read" => 1,
            "full" => 2,
            _ => 0
        };

    // CRUD: Customers
    public List<Customer> GetCustomers() => GetCustomers(Conn);

    static List<Customer> GetCustomers(DbConnection connection) {
        var list = new List<Customer>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id,customer_number,company,contact_name,email,phone,street,zip_code,city,country,tax_id,status,notes,created_at FROM customers ORDER BY company,contact_name";
        using var r = cmd.ExecuteReader();
        while (r.Read()) {
            list.Add(new Customer {
                Id = r.GetInt64(0),
                CustomerNumber = r.IsDBNull(1) ? "" : r.GetString(1),
                Company = r.IsDBNull(2) ? "" : r.GetString(2),
                ContactName = r.IsDBNull(3) ? "" : r.GetString(3),
                Email = r.IsDBNull(4) ? "" : r.GetString(4),
                Phone = r.IsDBNull(5) ? "" : r.GetString(5),
                Street = r.IsDBNull(6) ? "" : r.GetString(6),
                ZipCode = r.IsDBNull(7) ? "" : r.GetString(7),
                City = r.IsDBNull(8) ? "" : r.GetString(8),
                Country = r.IsDBNull(9) ? "" : r.GetString(9),
                TaxId = r.IsDBNull(10) ? "" : r.GetString(10),
                Status = r.IsDBNull(11) ? "Aktiv" : r.GetString(11),
                Notes = r.IsDBNull(12) ? "" : r.GetString(12),
                CreatedAt = r.GetInvariantText(13) ?? ""
            });
        }
        return list;
    }

    /// <summary>
    /// Resolves an imported or user-selected customer to one stable local ID.
    /// Source identity wins over customer number and display name. Ambiguous
    /// matches are rejected instead of silently linking a document incorrectly.
    /// </summary>
    public long? ResolveCustomerId(
        string? displayName,
        string? customerNumber = null,
        string? sourceProvider = null,
        string? sourceExternalId = null) {
        var normalizedName = NormalizeCustomerLookupText(displayName);
        var normalizedNumber = (customerNumber ?? "").Trim();
        var normalizedProvider = (sourceProvider ?? "").Trim();
        var normalizedExternalId = (sourceExternalId ?? "").Trim();

        if (string.IsNullOrEmpty(normalizedProvider) != string.IsNullOrEmpty(normalizedExternalId))
            throw new ArgumentException("Quellanbieter und externe Kunden-ID müssen gemeinsam angegeben werden.");
        if (normalizedProvider.Length > 32)
            throw new ArgumentException("Der Quellanbieter darf höchstens 32 Zeichen enthalten.", nameof(sourceProvider));
        if (normalizedExternalId.Length > 191)
            throw new ArgumentException("Die externe Kunden-ID darf höchstens 191 Zeichen enthalten.", nameof(sourceExternalId));
        if (normalizedNumber.Length > 100)
            throw new ArgumentException("Die Kundennummer darf höchstens 100 Zeichen enthalten.", nameof(customerNumber));

        var candidates = new List<CustomerIdentityCandidate>();
        using (var cmd = Conn.CreateCommand()) {
            cmd.CommandText = "SELECT id,customer_number,company,contact_name,notes FROM customers";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) {
                var company = reader.IsDBNull(2) ? "" : reader.GetString(2);
                var contact = reader.IsDBNull(3) ? "" : reader.GetString(3);
                candidates.Add(new CustomerIdentityCandidate(
                    reader.GetInt64(0),
                    reader.IsDBNull(1) ? "" : reader.GetString(1),
                    string.IsNullOrWhiteSpace(company) ? contact : company,
                    reader.IsDBNull(4) ? "" : reader.GetString(4)));
            }
        }

        if (!string.IsNullOrEmpty(normalizedProvider)) {
            var sourceMarker = normalizedProvider + ":" + normalizedExternalId;
            var matches = candidates
                .Where(candidate => ContainsExactCustomerSourceMarker(candidate.Notes, sourceMarker))
                .Select(candidate => candidate.Id)
                .Distinct()
                .ToList();
            if (matches.Count > 0)
                return RequireUniqueCustomerMatch(matches, "Quellkennung");
        }

        if (!string.IsNullOrEmpty(normalizedNumber)) {
            var matches = candidates
                .Where(candidate => string.Equals(
                    candidate.CustomerNumber.Trim(), normalizedNumber, StringComparison.OrdinalIgnoreCase))
                .Select(candidate => candidate.Id)
                .Distinct()
                .ToList();
            if (matches.Count > 0)
                return RequireUniqueCustomerMatch(matches, "Kundennummer");
        }

        if (!string.IsNullOrEmpty(normalizedName)) {
            var matches = candidates
                .Where(candidate => string.Equals(
                    NormalizeCustomerLookupText(candidate.DisplayName), normalizedName,
                    StringComparison.OrdinalIgnoreCase))
                .Select(candidate => candidate.Id)
                .Distinct()
                .ToList();
            if (matches.Count > 0)
                return RequireUniqueCustomerMatch(matches, "Kundenname");
        }

        return null;
    }

    private long? NormalizeDocumentCustomerReference(long? currentCustomerId, string? displayName) {
        var normalizedName = NormalizeCustomerLookupText(displayName);
        if (string.IsNullOrEmpty(normalizedName))
            return null;

        if (currentCustomerId is > 0) {
            using var current = Conn.CreateCommand();
            current.CommandText = "SELECT company,contact_name FROM customers WHERE id=@id";
            current.Parameters.AddWithValue("@id", currentCustomerId.Value);
            using var reader = current.ExecuteReader();
            if (reader.Read()) {
                var company = reader.IsDBNull(0) ? "" : reader.GetString(0);
                var contact = reader.IsDBNull(1) ? "" : reader.GetString(1);
                var currentName = string.IsNullOrWhiteSpace(company) ? contact : company;
                if (string.Equals(
                        NormalizeCustomerLookupText(currentName), normalizedName,
                        StringComparison.OrdinalIgnoreCase))
                    return currentCustomerId;
            }
        }

        return ResolveCustomerId(displayName);
    }

    private void NormalizeInvoiceCustomerReference(Invoice invoice) {
        invoice.Customer = (invoice.Customer ?? "").Trim();
        invoice.CustomerId = NormalizeDocumentCustomerReference(invoice.CustomerId, invoice.Customer);
    }

    private void NormalizeOfferCustomerReference(Offer offer) {
        offer.Customer = (offer.Customer ?? "").Trim();
        offer.CustomerId = NormalizeDocumentCustomerReference(offer.CustomerId, offer.Customer);
    }

    private static long RequireUniqueCustomerMatch(IReadOnlyCollection<long> matches, string criterion) {
        if (matches.Count != 1)
            throw new InvalidOperationException(
                $"Die {criterion} ist mehreren Kunden zugeordnet. Bitte wählen Sie den Kunden eindeutig aus.");
        return matches.First();
    }

    private static bool ContainsExactCustomerSourceMarker(string? notes, string marker) =>
        (notes ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(line => string.Equals(line, marker, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeCustomerLookupText(string? value) =>
        string.Join(' ', (value ?? "")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    public void AddCustomer(Customer c) {
        NormalizeCustomerNumber(c);
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO customers(customer_number,company,contact_name,email,phone,street,zip_code,city,country,tax_id,status,notes,created_at)
            VALUES(@customerNumber,@company,@contact,@email,@phone,@street,@zip,@city,@country,@tax,@status,@notes,CURRENT_TIMESTAMP)";
        cmd.Parameters.AddWithValue("@customerNumber", (object?)c.CustomerNumber ?? DBNull.Value);
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
        NormalizeCustomerNumber(c);
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"UPDATE customers SET customer_number=@customerNumber,company=@company,contact_name=@contact,email=@email,phone=@phone,
            street=@street,zip_code=@zip,city=@city,country=@country,tax_id=@tax,status=@status,notes=@notes WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", c.Id);
        cmd.Parameters.AddWithValue("@customerNumber", (object?)c.CustomerNumber ?? DBNull.Value);
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

    private static void NormalizeCustomerNumber(Customer customer) {
        customer.CustomerNumber = (customer.CustomerNumber ?? "").Trim();
        if (customer.CustomerNumber.Length > 100)
            throw new InvalidOperationException("Die Kundennummer darf höchstens 100 Zeichen lang sein.");
    }

    public void DeleteCustomer(long id) {
        if (id <= 0)
            throw new ArgumentOutOfRangeException(nameof(id));

        using var tx = BeginWriteTransaction();
        try {
            foreach (var table in new[] { "invoices", "offers" }) {
                using var unlink = Conn.CreateCommand();
                unlink.Transaction = tx;
                unlink.CommandText = $"UPDATE {table} SET customer_id=NULL WHERE customer_id=@id";
                unlink.Parameters.AddWithValue("@id", id);
                unlink.ExecuteNonQuery();
            }

            using var delete = Conn.CreateCommand();
            delete.Transaction = tx;
            delete.CommandText = "DELETE FROM customers WHERE id=@id";
            delete.Parameters.AddWithValue("@id", id);
            delete.ExecuteNonQuery();
            tx.Commit();
        }
        catch {
            TryRollback(tx);
            throw;
        }
    }

    sealed record CustomerIdentityCandidate(
        long Id,
        string CustomerNumber,
        string DisplayName,
        string Notes);

    public void SeedDemoData() {
        using (var cmd = Conn.CreateCommand()) {
            cmd.CommandText = "SELECT COUNT(*) FROM customers";
            if (Convert.ToInt64(cmd.ExecuteScalar()) > 0)
                return;
        }

        var adminRoleId = EnsureRole("Admin", "Vollzugriff auf alle Bereiche");

        AddSeedUser("demo", "demo", "Demo Benutzer");
        SetUserRole("demo", adminRoleId);
        AddSeedUser("anna", "demo", "Anna Weber");
        SetUserRole("anna", EnsureRole("Accounting", "Finanzen, Rechnungen, Angebote und Steuern"));
        AddSeedUser("mika", "demo", "Mika Schneider");
        SetUserRole("mika", EnsureRole("Ingenieur", "Projekt-, Ressourcen- und Aufgabenbearbeitung"));

        SetSettingStartBalance(19531.47);
        SaveSetting("company_name", "Building Engineering GmbH");
        SaveSetting("company_address_1", "Musterstrasse 12, 70173 Stuttgart");
        SaveSetting("company_address_2", "Abteilung Cashflow & Projektsteuerung");
        SaveSetting("company_email", "info@building-engineering.de");
        SaveSetting("company_phone", "+49 711 555 120");
        SaveSetting("company_website", "www.building-engineering.de");
        SaveSetting("company_tax_id", "DE123456789");

        var customer1 = new Customer {
            Company = "Musterbau GmbH",
            ContactName = "Sarah Klein",
            Email = "s.klein@musterbau.de",
            Phone = "+49 30 555 100",
            City = "Berlin",
            Notes = "Stammkunde mit laufendem Hallenprojekt"
        };
        AddCustomer(customer1);

        var customer2 = new Customer {
            Company = "Nordkraft Engineering",
            ContactName = "Jan Voigt",
            Email = "j.voigt@nordkraft.de",
            Phone = "+49 40 555 210",
            City = "Hamburg",
            Notes = "Energie- und TGA-Projekte"
        };
        AddCustomer(customer2);

        var customer3 = new Customer {
            Company = "GreenSteel AG",
            ContactName = "Elena Kurz",
            Email = "e.kurz@greensteel.de",
            Phone = "+49 211 555 310",
            City = "Dusseldorf",
            Notes = "Produktionsstandort im Ausbau"
        };
        AddCustomer(customer3);

        var customer4 = new Customer {
            Company = "Skyline Quartier GmbH",
            ContactName = "Lars Meier",
            Email = "l.meier@skyline-quartier.de",
            Phone = "+49 69 555 410",
            City = "Frankfurt",
            Notes = "Mixed-use Quartier mit enger Terminlage"
        };
        AddCustomer(customer4);

        var customer5 = new Customer {
            Company = "AeroLogistik Süd",
            ContactName = "Clara Stein",
            Email = "c.stein@aerologistik.de",
            Phone = "+49 89 555 515",
            City = "Muenchen",
            Notes = "Logistikzentrum und Bueroflaechen"
        };
        AddCustomer(customer5);

        var project1 = new Project {
            ProjectNumber = "P-2026-001",
            Name = "Werkhalle Nord",
            Client = customer1.DisplayName,
            Color = "#812B8C",
            StartDate = DateTime.Today.AddDays(-45).ToString("yyyy-MM-dd"),
            EndDate = DateTime.Today.AddMonths(4).ToString("yyyy-MM-dd"),
            Budget = 420,
            Status = "active"
        };
        AddProject(project1);

        var project2 = new Project {
            ProjectNumber = "P-2026-002",
            Name = "Campus Retrofit",
            Client = customer2.DisplayName,
            Color = "#BF247A",
            StartDate = DateTime.Today.AddDays(-20).ToString("yyyy-MM-dd"),
            EndDate = DateTime.Today.AddMonths(3).ToString("yyyy-MM-dd"),
            Budget = 280,
            Status = "active"
        };
        AddProject(project2);

        var project3 = new Project {
            ProjectNumber = "P-2026-003",
            Name = "Produktionslinie Ost",
            Client = customer3.DisplayName,
            Color = "#D9731A",
            StartDate = DateTime.Today.AddDays(-10).ToString("yyyy-MM-dd"),
            EndDate = DateTime.Today.AddMonths(6).ToString("yyyy-MM-dd"),
            Budget = 520,
            Status = "active"
        };
        AddProject(project3);

        var project4 = new Project {
            ProjectNumber = "P-2026-004",
            Name = "Skyline Suedfluegel",
            Client = customer4.DisplayName,
            Color = "#6C5CE7",
            StartDate = DateTime.Today.AddDays(-60).ToString("yyyy-MM-dd"),
            EndDate = DateTime.Today.AddMonths(2).ToString("yyyy-MM-dd"),
            Budget = 360,
            Status = "active"
        };
        AddProject(project4);

        var project5 = new Project {
            ProjectNumber = "P-2026-005",
            Name = "Logistikzentrum West",
            Client = customer5.DisplayName,
            Color = "#00A896",
            StartDate = DateTime.Today.AddDays(-8).ToString("yyyy-MM-dd"),
            EndDate = DateTime.Today.AddMonths(5).ToString("yyyy-MM-dd"),
            Budget = 610,
            Status = "active"
        };
        AddProject(project5);

        var resource1 = new Resource { Name = "Mika Schneider", Role = "Projektleitung", Availability = 0.95, HourlyRate = 88, WorkStartHour = 8, WorkEndHour = 17 };
        AddResource(resource1);
        var resource2 = new Resource { Name = "Nina Bauer", Role = "CAD", Availability = 0.85, HourlyRate = 72, WorkStartHour = 8, WorkEndHour = 16 };
        AddResource(resource2);
        var resource3 = new Resource { Name = "Leon Hoffmann", Role = "Bauleitung", Availability = 0.9, HourlyRate = 79, WorkStartHour = 7, WorkEndHour = 16 };
        AddResource(resource3);
        var resource4 = new Resource { Name = "Sofia Kramer", Role = "TGA Planung", Availability = 0.8, HourlyRate = 84, WorkStartHour = 8, WorkEndHour = 17 };
        AddResource(resource4);
        var resource5 = new Resource { Name = "Jonas Richter", Role = "Einkauf", Availability = 0.7, HourlyRate = 68, WorkStartHour = 8, WorkEndHour = 16 };
        AddResource(resource5);
        var resource6 = new Resource { Name = "Paula Winter", Role = "Controlling", Availability = 0.75, HourlyRate = 77, WorkStartHour = 8, WorkEndHour = 17 };
        AddResource(resource6);

        var hardware1 = new HardwareResource { Name = "Workstation CAD-01", Type = "GPU Workstation", CostPerHour = 9.5, Color = "#812B8C", Notes = "Rendering und Revit" };
        AddHardwareResource(hardware1);
        var hardware2 = new HardwareResource { Name = "Laserscanner Faro", Type = "Laserscanner", CostPerHour = 24, Color = "#D9731A", Notes = "Bestandsaufnahmen vor Ort" };
        AddHardwareResource(hardware2);
        var hardware3 = new HardwareResource { Name = "Plotter HP DesignJet", Type = "Plotter", CostPerHour = 6, Color = "#2F80ED", Notes = "Großformat Plaene und Uebersichten" };
        AddHardwareResource(hardware3);
        var hardware4 = new HardwareResource { Name = "BIM Server Node A", Type = "Server", CostPerHour = 12, Color = "#00A896", Notes = "Zentrale Modellkoordination" };
        AddHardwareResource(hardware4);

        var milestone1 = new ProjectMilestone {
            ProjectId = project1.Id,
            Name = "Ausfuehrungsplanung finalisieren",
            Status = "In Arbeit",
            Deadline = DateTime.Today.AddDays(5).ToString("yyyy-MM-dd"),
            Responsible = "Mika Schneider",
            HoursBudget = 120,
            Priority = 1
        };
        AddMilestone(milestone1);

        var milestone2 = new ProjectMilestone {
            ProjectId = project2.Id,
            Name = "Kostenfreigabe Bauherr",
            Status = "Offen",
            Deadline = DateTime.Today.AddDays(-2).ToString("yyyy-MM-dd"),
            Responsible = "Anna Weber",
            HoursBudget = 40,
            Priority = 1
        };
        AddMilestone(milestone2);

        AddMilestone(new ProjectMilestone {
            ProjectId = project3.Id,
            Name = "Medienplanung abstimmen",
            Status = "Review",
            Deadline = DateTime.Today.AddDays(9).ToString("yyyy-MM-dd"),
            Responsible = "Sofia Kramer",
            HoursBudget = 84,
            Priority = 2
        });
        AddMilestone(new ProjectMilestone {
            ProjectId = project4.Id,
            Name = "Revisionslauf Brandschutz",
            Status = "In Arbeit",
            Deadline = DateTime.Today.AddDays(3).ToString("yyyy-MM-dd"),
            Responsible = "Leon Hoffmann",
            HoursBudget = 56,
            Priority = 1
        });
        AddMilestone(new ProjectMilestone {
            ProjectId = project5.Id,
            Name = "BIM Modell an Einkauf uebergeben",
            Status = "Abgeschlossen",
            Deadline = DateTime.Today.AddDays(14).ToString("yyyy-MM-dd"),
            Responsible = "Jonas Richter",
            HoursBudget = 72,
            Priority = 2
        });
        AddMilestone(new ProjectMilestone {
            ProjectId = project1.Id,
            Name = "Technikfreigabe Bauherr",
            Status = "Aktiv",
            Deadline = DateTime.Today.AddDays(7).ToString("yyyy-MM-dd"),
            Responsible = "Anna Weber",
            HoursBudget = 32,
            Priority = 2
        });
        AddMilestone(new ProjectMilestone {
            ProjectId = project2.Id,
            Name = "Bestandsmodell validieren",
            Status = "Review",
            Deadline = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd"),
            Responsible = "Nina Bauer",
            HoursBudget = 28,
            Priority = 1
        });
        AddMilestone(new ProjectMilestone {
            ProjectId = project4.Id,
            Name = "Brandschutzpaket final abgegeben",
            Status = "Abgeschlossen",
            Deadline = DateTime.Today.AddDays(-6).ToString("yyyy-MM-dd"),
            Responsible = "Leon Hoffmann",
            HoursBudget = 44,
            Priority = 2
        });

        var demoUserId = GetUserId("demo");

        AddTodo(new UserTodo {
            UserId = demoUserId,
            Title = "Offene Rechnung Werkhalle nachfassen",
            Description = "Kunden rueckmelden lassen wegen Teilzahlung.",
            Status = "Offen",
            Priority = 1,
            DueDate = DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd"),
            ProjectId = project1.Id,
            MilestoneId = milestone1.Id
        });
        AddTodo(new UserTodo {
            UserId = demoUserId,
            Title = "Angebot GreenSteel final pruefen",
            Description = "Wahrscheinlichkeit und Zahlungsziel abstimmen.",
            Status = "In Arbeit",
            Priority = 2,
            DueDate = DateTime.Today.AddDays(2).ToString("yyyy-MM-dd"),
            ProjectId = project3.Id
        });
        AddTodo(new UserTodo {
            UserId = demoUserId,
            Title = "Teammeeting vorbereiten",
            Description = "Kapazitaet und naechste Meilensteine mitbringen.",
            Status = "Offen",
            Priority = 3,
            DueDate = DateTime.Today.AddDays(4).ToString("yyyy-MM-dd"),
            ProjectId = project2.Id
        });
        AddTodo(new UserTodo {
            UserId = demoUserId,
            Title = "Fixkosten fuer Q2 pruefen",
            Description = "Cloud, Versicherung und Miete gegen Budget spiegeln.",
            Status = "In Arbeit",
            Priority = 2,
            DueDate = DateTime.Today.AddDays(6).ToString("yyyy-MM-dd"),
            ProjectId = project4.Id
        });
        AddTodo(new UserTodo {
            UserId = demoUserId,
            Title = "Laserscan fuer Logistikzentrum einplanen",
            Description = "Geraet und Baustellentermin mit Kunde abstimmen.",
            Status = "Offen",
            Priority = 1,
            DueDate = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd"),
            ProjectId = project5.Id
        });
        AddTodo(new UserTodo {
            UserId = demoUserId,
            Title = "Revisionspläne an Kunden senden",
            Description = "Skyline-Stand als PDF exportieren und verschicken.",
            Status = "Erledigt",
            Priority = 2,
            DueDate = DateTime.Today.AddDays(-2).ToString("yyyy-MM-dd"),
            ProjectId = project4.Id
        });
        AddTodo(new UserTodo {
            UserId = demoUserId,
            Title = "Zeiterfassung der Woche kontrollieren",
            Description = "Offene Timer und fehlende Buchungen nachziehen.",
            Status = "In Arbeit",
            Priority = 2,
            DueDate = DateTime.Today.ToString("yyyy-MM-dd"),
            ProjectId = project1.Id
        });
        AddTodo(new UserTodo {
            UserId = demoUserId,
            Title = "Bauherr-Feedback in Angebot uebernehmen",
            Description = "Kommentar zur Medienversorgung in Version 3 einfliessen lassen.",
            Status = "Offen",
            Priority = 1,
            DueDate = DateTime.Today.AddDays(3).ToString("yyyy-MM-dd"),
            ProjectId = project3.Id
        });
        AddTodo(new UserTodo {
            UserId = demoUserId,
            Title = "Monatsreport fuer Management vorbereiten",
            Description = "Forecast, offene Rechnungen und Teamlast zusammenfassen.",
            Status = "Erledigt",
            Priority = 3,
            DueDate = DateTime.Today.AddDays(-4).ToString("yyyy-MM-dd"),
            ProjectId = project2.Id
        });
        AddTodo(new UserTodo {
            UserId = demoUserId,
            Title = "Lieferantenliste fuer Logistikzentrum abstimmen",
            Description = "Mit Einkauf finale Freigabe fuer Ausschreibung erzeugen.",
            Status = "Offen",
            Priority = 2,
            DueDate = DateTime.Today.AddDays(5).ToString("yyyy-MM-dd"),
            ProjectId = project5.Id
        });

        AddInvoice(new Invoice {
            IssueDate = DateTime.Today.AddDays(-22).ToString("yyyy-MM-dd"),
            DueDate = DateTime.Today.AddDays(8).ToString("yyyy-MM-dd"),
            Customer = customer1.DisplayName,
            Amount = 12450,
            Description = "Teilrechnung Werkhalle Nord - LPH 3",
            PaidAmount = 0,
            Status = "Offen"
        });
        AddInvoice(new Invoice {
            IssueDate = DateTime.Today.AddDays(-12).ToString("yyyy-MM-dd"),
            DueDate = DateTime.Today.AddDays(2).ToString("yyyy-MM-dd"),
            Customer = customer2.DisplayName,
            Amount = 6500,
            Description = "Bestandsaufnahme Campus Retrofit",
            PaidAmount = 0,
            Status = "Offen"
        });
        AddInvoice(new Invoice {
            IssueDate = DateTime.Today.AddDays(-35).ToString("yyyy-MM-dd"),
            DueDate = DateTime.Today.AddDays(-5).ToString("yyyy-MM-dd"),
            Customer = customer3.DisplayName,
            Amount = 9800,
            Description = "Vorplanung Produktionslinie Ost",
            PaidDate = DateTime.Today.AddDays(-4).ToString("yyyy-MM-dd"),
            PaidAmount = 9800,
            Status = "Bezahlt"
        });
        AddInvoice(new Invoice {
            IssueDate = DateTime.Today.AddDays(-6).ToString("yyyy-MM-dd"),
            DueDate = DateTime.Today.AddDays(16).ToString("yyyy-MM-dd"),
            Customer = customer4.DisplayName,
            Amount = 8450,
            Description = "Revisionsplanung Skyline Suedfluegel",
            PaidAmount = 0,
            Status = "Offen"
        });
        AddInvoice(new Invoice {
            IssueDate = DateTime.Today.AddDays(-3).ToString("yyyy-MM-dd"),
            DueDate = DateTime.Today.AddDays(27).ToString("yyyy-MM-dd"),
            Customer = customer5.DisplayName,
            Amount = 11200,
            Description = "Kickoff und Vor-Ort-Aufnahme Logistikzentrum",
            PaidAmount = 0,
            Status = "Offen"
        });

        AddOffer(new Offer {
            OfferNumber = $"ANG-{DateTime.Today.Year}-1001",
            OfferDate = DateTime.Today.AddDays(-4).ToString("yyyy-MM-dd"),
            DateExpected = DateTime.Today.AddDays(18).ToString("yyyy-MM-dd"),
            Customer = customer3.DisplayName,
            Amount = 64000,
            Probability = 72,
            Description = "Erweiterung TGA und Medienversorgung",
            Status = "Offen",
            PaymentDelay = 21
        });
        AddOffer(new Offer {
            OfferNumber = $"ANG-{DateTime.Today.Year}-1002",
            OfferDate = DateTime.Today.AddDays(-14).ToString("yyyy-MM-dd"),
            DateExpected = DateTime.Today.AddDays(12).ToString("yyyy-MM-dd"),
            Customer = customer1.DisplayName,
            Amount = 18500,
            Probability = 90,
            Description = "Nachtrag Brandschutz und Druckbelueftung",
            Status = "Beauftragt",
            PaymentDelay = 14
        });
        AddOffer(new Offer {
            OfferNumber = $"ANG-{DateTime.Today.Year}-1003",
            OfferDate = DateTime.Today.AddDays(-7).ToString("yyyy-MM-dd"),
            DateExpected = DateTime.Today.AddDays(24).ToString("yyyy-MM-dd"),
            Customer = customer4.DisplayName,
            Amount = 28500,
            Probability = 58,
            Description = "Monitoring und technische Gebaeudeauswertung",
            Status = "Offen",
            PaymentDelay = 30
        });
        AddOffer(new Offer {
            OfferNumber = $"ANG-{DateTime.Today.Year}-1004",
            OfferDate = DateTime.Today.AddDays(-2).ToString("yyyy-MM-dd"),
            DateExpected = DateTime.Today.AddDays(31).ToString("yyyy-MM-dd"),
            Customer = customer5.DisplayName,
            Amount = 74000,
            Probability = 41,
            Description = "Lagerautomation und Medienkoordination",
            Status = "Offen",
            PaymentDelay = 21
        });

        AddTransaction(new Transaction {
            Date = DateTime.Today.AddDays(-16).ToString("yyyy-MM-dd"),
            Description = "Miete und Nebenkosten",
            Amount = -2850,
            Interval = "monatlich",
            Notes = "FIXKOSTEN:Buero und Nebenkosten"
        });
        AddTransaction(new Transaction {
            Date = DateTime.Today.AddDays(-9).ToString("yyyy-MM-dd"),
            Description = "Abschlag Werkhalle Nord",
            Amount = 15750,
            Notes = "projekt"
        });
        AddTransaction(new Transaction {
            Date = DateTime.Today.AddDays(-3).ToString("yyyy-MM-dd"),
            Description = "Lohn und Gehaelter",
            Amount = -11200,
            Interval = "monatlich",
            Notes = "FIXKOSTEN:Personal"
        });
        AddTransaction(new Transaction {
            Date = DateTime.Today.AddDays(6).ToString("yyyy-MM-dd"),
            Description = "Software und Cloud",
            Amount = -640,
            Interval = "monatlich",
            Notes = "FIXKOSTEN:Software und Cloud"
        });
        AddTransaction(new Transaction {
            Date = DateTime.Today.AddDays(17).ToString("yyyy-MM-dd"),
            Description = "Materialvorschuss GreenSteel",
            Amount = -4200,
            Notes = "projekt"
        });
        AddTransaction(new Transaction {
            Date = DateTime.Today.AddDays(-11).ToString("yyyy-MM-dd"),
            Description = "Betriebshaftpflicht",
            Amount = -520,
            Interval = "jährlich",
            Notes = "FIXKOSTEN:Versicherung"
        });
        AddTransaction(new Transaction {
            Date = DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd"),
            Description = "Strom und Infrastruktur",
            Amount = -780,
            Interval = "monatlich",
            Notes = "FIXKOSTEN:Energie"
        });
        AddTransaction(new Transaction {
            Date = DateTime.Today.AddDays(9).ToString("yyyy-MM-dd"),
            Description = "Steuerberatung und Abschluss",
            Amount = -950,
            Interval = "monatlich",
            Notes = "FIXKOSTEN:Steuerberatung"
        });
        AddTransaction(new Transaction {
            Date = DateTime.Today.AddDays(-8).ToString("yyyy-MM-dd"),
            Description = "Umsatzsteuer-Voranmeldung",
            Amount = -3150,
            Interval = "Monatlich",
            Notes = "STEUER:Umsatzsteuer"
        });
        AddTransaction(new Transaction {
            Date = DateTime.Today.AddDays(13).ToString("yyyy-MM-dd"),
            Description = "Gewerbesteuer Vorauszahlung",
            Amount = -2400,
            Interval = "Vierteljährlich",
            Notes = "STEUER:Gewerbesteuer"
        });
        AddTransaction(new Transaction {
            Date = DateTime.Today.AddDays(21).ToString("yyyy-MM-dd"),
            Description = "Kapitalertragsteuer Ruecklage",
            Amount = -850,
            Interval = "Jährlich",
            Notes = "STEUER:Kapitalertragsteuer"
        });
        AddTransaction(new Transaction {
            Date = DateTime.Today.AddDays(-18).ToString("yyyy-MM-dd"),
            Description = "Abschlag Skyline Quartier",
            Amount = 9200,
            Notes = "projekt"
        });
        AddTransaction(new Transaction {
            Date = DateTime.Today.AddDays(12).ToString("yyyy-MM-dd"),
            Description = "Abschlag Logistikzentrum West",
            Amount = 13400,
            Notes = "projekt"
        });

        AddTarget(new Target { Year = DateTime.Today.Year, Month = DateTime.Today.Month, Amount = 22000 });
        var nextMonth = DateTime.Today.AddMonths(1);
        AddTarget(new Target { Year = nextMonth.Year, Month = nextMonth.Month, Amount = 24000 });
        var monthAfterNext = DateTime.Today.AddMonths(2);
        AddTarget(new Target { Year = monthAfterNext.Year, Month = monthAfterNext.Month, Amount = 26500 });

        AddAllocation(new ResourceAllocation {
            ResourceId = resource1.Id,
            ProjectId = project1.Id,
            Date = DateTime.Today.ToString("yyyy-MM-dd"),
            Hours = 6,
            Notes = "Planungsreview"
        });
        AddAllocation(new ResourceAllocation {
            ResourceId = resource2.Id,
            ProjectId = project2.Id,
            Date = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd"),
            Hours = 5,
            Notes = "CAD Ueberarbeitung"
        });
        AddAllocation(new ResourceAllocation {
            ResourceId = resource3.Id,
            ProjectId = project3.Id,
            Date = DateTime.Today.AddDays(2).ToString("yyyy-MM-dd"),
            Hours = 7,
            Notes = "Baustellenabstimmung"
        });
        AddAllocation(new ResourceAllocation {
            ResourceId = resource4.Id,
            ProjectId = project4.Id,
            Date = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd"),
            Hours = 6,
            Notes = "Brandschutz-TGA Abgleich"
        });
        AddAllocation(new ResourceAllocation {
            ResourceId = resource5.Id,
            ProjectId = project5.Id,
            Date = DateTime.Today.AddDays(3).ToString("yyyy-MM-dd"),
            Hours = 4,
            Notes = "Lieferantenkoordination"
        });
        AddAllocation(new ResourceAllocation {
            ResourceId = resource6.Id,
            ProjectId = project1.Id,
            Date = DateTime.Today.AddDays(4).ToString("yyyy-MM-dd"),
            Hours = 3,
            Notes = "Forecast und Nachkalkulation"
        });

        AddHardwareAllocation(new HardwareAllocation {
            ResourceId = resource2.Id,
            HardwareId = hardware1.Id,
            ProjectId = project2.Id,
            Date = DateTime.Today.ToString("yyyy-MM-dd"),
            Hours = 5,
            Notes = "3D Modellupdate"
        });
        AddHardwareAllocation(new HardwareAllocation {
            ResourceId = resource3.Id,
            HardwareId = hardware2.Id,
            ProjectId = project5.Id,
            Date = DateTime.Today.AddDays(2).ToString("yyyy-MM-dd"),
            Hours = 6,
            Notes = "Bestandsaufnahme Halle"
        });
        AddHardwareAllocation(new HardwareAllocation {
            ResourceId = resource4.Id,
            HardwareId = hardware4.Id,
            ProjectId = project3.Id,
            Date = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd"),
            Hours = 7,
            Notes = "BIM Koordination"
        });
        AddHardwareAllocation(new HardwareAllocation {
            ResourceId = resource1.Id,
            HardwareId = hardware3.Id,
            ProjectId = project4.Id,
            Date = DateTime.Today.AddDays(5).ToString("yyyy-MM-dd"),
            Hours = 2,
            Notes = "Plaene fuer Abstimmung"
        });

        AddHistoricalTimeEntry(demoUserId, project1.Id, "Planung", "Kickoff und Abstimmung", DateTime.Today.AddDays(-3).AddHours(8), 4.5);
        AddHistoricalTimeEntry(demoUserId, project2.Id, "CAD", "Grundrisse ueberarbeitet", DateTime.Today.AddDays(-2).AddHours(9), 6.0);
        AddHistoricalTimeEntry(demoUserId, project3.Id, "Meeting", "Jour fixe mit GreenSteel", DateTime.Today.AddDays(-1).AddHours(10), 2.0);
        AddHistoricalTimeEntry(demoUserId, project4.Id, "Dokumentation", "Revisionsunterlagen vorbereitet", DateTime.Today.AddDays(-5).AddHours(8), 3.5);
        AddHistoricalTimeEntry(demoUserId, project5.Id, "Abstimmung", "Baustellenlogistik mit Kunde abgestimmt", DateTime.Today.AddDays(-4).AddHours(11), 2.5);
        StartDemoRunningTimer(demoUserId, project1.Id, "Dokumentation", "Demo-Timer laeuft fuer Dashboard");

        // Link the known seeded user/resource pair (Mika) and provision resources
        // for the remaining non-admin demo users without duplicating existing rows.
        BackfillResourceUserLinks();
    }

    // Helpers
    DbTransaction BeginWriteTransaction() => Conn.BeginTransaction();

    long LastInsertId(DbTransaction tx) {
        using var cmd = Conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = _dialect.LastInsertIdSql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

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
        var allPages = new[] { "dashboard", "transactions", "bank", "fixkosten", "taxes", "invoices", "offers", "resources", "targets", "todos", "timetracking", "kunden", "integrations", "admin" };

        long adminRoleId = EnsureRole("Admin", "Vollzugriff auf alle Bereiche");
        foreach (var page in allPages)
            EnsureRolePermission(adminRoleId, page, "full");

        long engineerRoleId = EnsureRole("Ingenieur", "Projekt-, Ressourcen- und Aufgabenbearbeitung");
        EnsureRolePermission(engineerRoleId, "dashboard", "read");
        EnsureRolePermission(engineerRoleId, "offers", "read");
        EnsureRolePermission(engineerRoleId, "resources", "full");
        EnsureRolePermission(engineerRoleId, "todos", "full");
        EnsureRolePermission(engineerRoleId, "timetracking", "full");
        EnsureRolePermission(engineerRoleId, "kunden", "read");

        long accountingRoleId = EnsureRole("Accounting", "Finanzen, Rechnungen, Angebote und Steuern");
        EnsureRolePermission(accountingRoleId, "dashboard", "read");
        EnsureRolePermission(accountingRoleId, "transactions", "full");
        EnsureRolePermission(accountingRoleId, "bank", "full");
        EnsureRolePermission(accountingRoleId, "fixkosten", "full");
        EnsureRolePermission(accountingRoleId, "taxes", "full");
        EnsureRolePermission(accountingRoleId, "invoices", "full");
        EnsureRolePermission(accountingRoleId, "offers", "full");
        EnsureRolePermission(accountingRoleId, "resources", "read");
        EnsureRolePermission(accountingRoleId, "targets", "full");
        EnsureRolePermission(accountingRoleId, "todos", "read");
        EnsureRolePermission(accountingRoleId, "timetracking", "read");
        EnsureRolePermission(accountingRoleId, "kunden", "full");
        EnsureRolePermission(accountingRoleId, "integrations", "full");

        long managementRoleId = EnsureRole("Management", "Lesender Zugriff auf relevante Bereiche");
        EnsureRolePermission(managementRoleId, "dashboard", "read");
        EnsureRolePermission(managementRoleId, "transactions", "read");
        EnsureRolePermission(managementRoleId, "bank", "read");
        EnsureRolePermission(managementRoleId, "fixkosten", "read");
        EnsureRolePermission(managementRoleId, "taxes", "read");
        EnsureRolePermission(managementRoleId, "invoices", "read");
        EnsureRolePermission(managementRoleId, "offers", "read");
        EnsureRolePermission(managementRoleId, "resources", "read");
        EnsureRolePermission(managementRoleId, "targets", "read");
        EnsureRolePermission(managementRoleId, "todos", "read");
        EnsureRolePermission(managementRoleId, "timetracking", "read");
        EnsureRolePermission(managementRoleId, "kunden", "read");
        EnsureRolePermission(managementRoleId, "integrations", "read");

        var reservedAdmin = FindUserExact("admin");
        if (reservedAdmin is not null && reservedAdmin.RoleId != adminRoleId) {
            using var cmd = Conn.CreateCommand();
            cmd.CommandText = "UPDATE users SET role_id=@rid,security_stamp=@stamp WHERE id=@id";
            cmd.Parameters.AddWithValue("@rid", adminRoleId);
            cmd.Parameters.AddWithValue("@stamp", CreateSecurityStamp());
            cmd.Parameters.AddWithValue("@id", reservedAdmin.Id);
            cmd.ExecuteNonQuery();
        }
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

    void AddHistoricalTimeEntry(long userId, long projectId, string activityType, string description, DateTime start, double durationHours) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO time_entries(user_id,project_id,activity_type,description,entry_date,start_time,end_time,duration_hours,is_running,created_at)
            VALUES(@uid,@pid,@act,@desc,@entry,@start,@end,@hours,0,CURRENT_TIMESTAMP)";
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@pid", projectId);
        cmd.Parameters.AddWithValue("@act", activityType);
        cmd.Parameters.AddWithValue("@desc", description);
        cmd.Parameters.AddWithValue("@entry", start.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@start", start.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@end", start.AddHours(durationHours).ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@hours", durationHours);
        cmd.ExecuteNonQuery();
    }

    void StartDemoRunningTimer(long userId, long projectId, string activityType, string description) {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO time_entries(user_id,project_id,activity_type,description,entry_date,start_time,is_running,created_at)
            VALUES(@uid,@pid,@act,@desc,@entry,@start,1,CURRENT_TIMESTAMP)";
        var start = DateTime.Now.AddMinutes(-42);
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@pid", projectId);
        cmd.Parameters.AddWithValue("@act", activityType);
        cmd.Parameters.AddWithValue("@desc", description);
        cmd.Parameters.AddWithValue("@entry", start.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@start", start.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.ExecuteNonQuery();
    }
}
