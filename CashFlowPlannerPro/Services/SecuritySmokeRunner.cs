using CashFlowPlannerPro.Data;
using MySqlConnector;
using System.Data;
using System.Globalization;

namespace CashFlowPlannerPro.Services;

/// <summary>
/// Runs security-sensitive checks that require neither a database server nor
/// filesystem access. This keeps --security-smoke safe to execute on developer
/// and build machines without risking changes to a configured MariaDB database.
/// </summary>
internal static class SecuritySmokeRunner
{
    public static int Run()
    {
        var checks = 0;

        void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException("Smoke check failed: " + name);
            checks++;
        }

        void Expect<TException>(Action action, string name) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                checks++;
                return;
            }

            throw new InvalidOperationException(
                $"Smoke check failed: {name} (expected {typeof(TException).Name}).");
        }

        try
        {
            CheckPasswordPolicy(Check);
            CheckPasswordHashing(Check);
            CheckDiagnosticRedaction(Check);
            CheckMariaDbConnectionSecurity(Check, Expect<OverflowException>);
            CheckMariaDbDialect(Check);
            CheckDefaultCategories(Check);
            CheckBankFixedCostMapping(Check);
            CheckTemporalDatabaseText(Check);

            Console.WriteLine($"Security smoke tests passed: {checks} checks.");
            return 0;
        }
        catch (Exception ex)
        {
            var safeMessage = AppLogger.RedactForDiagnostics(ex.Message);
            Console.Error.WriteLine(
                $"Security smoke tests failed: {ex.GetType().Name}: {safeMessage}");
            return 1;
        }
    }

    private static void CheckPasswordPolicy(Action<bool, string> check)
    {
        check(
            PasswordPolicy.TryValidate(
                "Strong!OfflineSmokePassphrase2026",
                "smoke-user",
                out _),
            "strong password accepted");
        check(
            !PasswordPolicy.TryValidate("short", "smoke-user", out _),
            "short password rejected");
        check(
            !PasswordPolicy.TryValidate("123456789012", "smoke-user", out _),
            "known common password rejected");
        check(
            !PasswordPolicy.TryValidate(
                "Prefix-Smoke-User-Suffix-2026!",
                "smoke-user",
                out _),
            "password containing username rejected");
        check(
            !PasswordPolicy.TryValidate(
                "StrongPassword\u0001WithControl",
                "smoke-user",
                out _),
            "password containing control character rejected");
        check(
            !PasswordPolicy.TryValidate(
                new string('X', PasswordPolicy.MaximumLength + 1),
                "smoke-user",
                out _),
            "overlong password rejected");
    }

    private static void CheckPasswordHashing(Action<bool, string> check)
    {
        const string password = "Strong!OfflineHashPassphrase2026";
        const string wrongPassword = "Strong!OfflineHashPassphrase2027";

        var firstHash = PasswordHasher.Hash(password);
        var secondHash = PasswordHasher.Hash(password);

        check(firstHash.StartsWith("pbkdf2-sha256$600000$", StringComparison.Ordinal),
            "current PBKDF2 format and work factor used");
        check(!string.Equals(firstHash, secondHash, StringComparison.Ordinal),
            "password hashes use independent random salts");
        check(PasswordHasher.Verify(password, firstHash),
            "correct password verifies");
        check(!PasswordHasher.Verify(wrongPassword, firstHash),
            "incorrect password rejected");
        check(!PasswordHasher.NeedsRehash(firstHash),
            "current password hash does not require rehash");
        check(PasswordHasher.NeedsRehash("legacy-plaintext-value"),
            "legacy password representation requires rehash");
        check(!PasswordHasher.Verify(password, "pbkdf2-sha256$600000$invalid$invalid"),
            "malformed password hash rejected safely");
    }

    private static void CheckDiagnosticRedaction(Action<bool, string> check)
    {
        const string passwordSecret = "NeverLogThisPassword-2026";
        const string tokenSecret = "NeverLogThisAccessToken-2026";
        const string apiKeySecret = "NeverLogThisApiKey-2026";
        var diagnostic =
            $"Server=db.example.test;Password={passwordSecret};" +
            $"Authorization: Bearer {tokenSecret}\n" +
            $"{{\"api_key\":\"{apiKeySecret}\",\"operation\":\"smoke\"}}";

        var redacted = AppLogger.RedactForDiagnostics(diagnostic);

        check(!redacted.Contains(passwordSecret, StringComparison.Ordinal),
            "database password redacted from diagnostics");
        check(!redacted.Contains(tokenSecret, StringComparison.Ordinal),
            "authorization token redacted from diagnostics");
        check(!redacted.Contains(apiKeySecret, StringComparison.Ordinal),
            "API key redacted from diagnostics");
        check(redacted.Contains("[REDACTED]", StringComparison.Ordinal),
            "redaction marker emitted");
        check(redacted.Contains("db.example.test", StringComparison.Ordinal)
              && redacted.Contains("operation", StringComparison.Ordinal),
            "non-secret diagnostic context preserved");
    }

    private static void CheckMariaDbConnectionSecurity(
        Action<bool, string> check,
        Action<Action, string> expectOverflow)
    {
        const string password = "Offline;Connection=Secret!2026";
        var config = new ConnectionConfig
        {
            Host = "db.example.test",
            Port = 3307,
            DatabaseName = "cashflow_smoke",
            DbUsername = "smoke-user",
            DbPassword = password
        };

        var builder = new MySqlConnectionStringBuilder(config.ToConnectionString());
        check(builder.Server == config.Host
              && builder.Port == (uint)config.Port
              && builder.Database == config.DatabaseName
              && builder.UserID == config.DbUsername,
            "MariaDB endpoint preserved in connection string");
        check(builder.Password == password,
            "MariaDB password survives connection-string escaping");
        check(builder.SslMode == MySqlSslMode.VerifyFull,
            "MariaDB TLS certificate and hostname verification required");
        check(string.Equals(builder.CharacterSet, "utf8mb4", StringComparison.OrdinalIgnoreCase),
            "MariaDB utf8mb4 character set required");
        check(builder.AllowUserVariables,
            "MariaDB named SQL parameters enabled");

        var clone = config.Clone();
        clone.Host = "changed.example.test";
        clone.DbPassword = "changed";
        check(config.Host == "db.example.test" && config.DbPassword == password,
            "connection configuration clone is independent");

        expectOverflow(
            () => new ConnectionConfig
            {
                Port = -1,
                DbPassword = "irrelevant"
            }.ToConnectionString(),
            "negative MariaDB port rejected before network access");

        using var connection = new MariaDbDialect().CreateConnection(config.ToConnectionString());
        check(connection is MySqlConnection && connection.State == System.Data.ConnectionState.Closed,
            "MariaDB connection factory remains offline until explicitly opened");
    }

    private static void CheckMariaDbDialect(Action<bool, string> check)
    {
        var dialect = new MariaDbDialect();
        var rewritten = dialect.RewriteDdl(
            """
            CREATE TABLE child_records(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                parent_id INTEGER,
                amount REAL DEFAULT 0,
                name TEXT UNIQUE NOT NULL,
                created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                interval TEXT)
            """);

        check(rewritten.Contains("id BIGINT PRIMARY KEY AUTO_INCREMENT", StringComparison.Ordinal),
            "MariaDB auto-increment primary key generated");
        check(rewritten.Contains("parent_id BIGINT", StringComparison.Ordinal),
            "MariaDB foreign-key integer width aligned");
        check(rewritten.Contains("amount DOUBLE DEFAULT 0", StringComparison.Ordinal),
            "MariaDB floating-point type generated");
        check(rewritten.Contains("name VARCHAR(255) UNIQUE NOT NULL", StringComparison.Ordinal),
            "MariaDB indexed text type bounded");
        check(rewritten.Contains(
                "created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP",
                StringComparison.Ordinal),
            "MariaDB audit timestamp type generated");
        check(rewritten.Contains("`interval` TEXT", StringComparison.Ordinal),
            "MariaDB reserved interval identifier quoted");

        var settingsDdl = dialect.RewriteDdl(
            "CREATE TABLE settings(key TEXT PRIMARY KEY, value TEXT)");
        check(settingsDdl.Contains("`key` VARCHAR(191) PRIMARY KEY", StringComparison.Ordinal),
            "MariaDB settings key quoted and index-safe");
        check(
            dialect.InsertOrIgnore("INSERT OR IGNORE INTO categories(name) VALUES(@name)")
                .StartsWith("INSERT IGNORE INTO", StringComparison.Ordinal),
            "MariaDB duplicate-safe insert generated");
        check(dialect.UpsertSettings("@value")
                .Contains("ON DUPLICATE KEY UPDATE", StringComparison.Ordinal),
            "MariaDB settings upsert generated");
        check(dialect.UpsertRolePermission()
                .Contains("ON DUPLICATE KEY UPDATE", StringComparison.Ordinal),
            "MariaDB permission upsert generated");
        check(dialect.DurationHoursExpr("start_time", "@end")
                .Contains("TIMESTAMPDIFF(SECOND", StringComparison.Ordinal),
            "MariaDB duration expression generated");
        check(dialect.LastInsertIdSql == "SELECT LAST_INSERT_ID()",
            "MariaDB insert identity query generated");
    }

    private static void CheckTemporalDatabaseText(Action<bool, string> check)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");

            var dateTime = new DateTime(2026, 8, 20, 14, 15, 16, DateTimeKind.Unspecified)
                .AddTicks(1_234_567);
            var dateTimeOffset = new DateTimeOffset(
                    2026, 8, 20, 14, 15, 16, TimeSpan.FromHours(2))
                .AddTicks(7_654_321);
            var duration = new TimeSpan(1, 2, 3, 4).Add(TimeSpan.FromTicks(50_067));
            var timeOnly = new TimeOnly(14, 15, 16).Add(TimeSpan.FromTicks(1_234_567));

            check(DbExtensions.FormatInvariantText("unverändert", "VARCHAR") == "unverändert",
                "database text remains unchanged");
            check(DbExtensions.FormatInvariantText(dateTime, "DATE") == "2026-08-20",
                "native MariaDB DATE remains date-only");
            check(DbExtensions.FormatInvariantText(dateTime, "DATETIME")
                    == "2026-08-20T14:15:16.1234567",
                "native MariaDB DATETIME uses invariant timestamp text");
            check(DbExtensions.FormatInvariantText(dateTime, "TIMESTAMP")
                    == "2026-08-20T14:15:16.1234567",
                "native MariaDB TIMESTAMP uses invariant timestamp text");
            check(DbExtensions.FormatInvariantText(
                    dateTimeOffset, "DATETIME") == "2026-08-20T14:15:16.7654321+02:00",
                "DateTimeOffset keeps its original offset");
            check(DbExtensions.FormatInvariantText(new DateOnly(2026, 8, 20), "DATE") == "2026-08-20",
                "DateOnly uses invariant date text");
            check(DbExtensions.FormatInvariantText(timeOnly, "TIME") == "14:15:16.1234567",
                "TimeOnly uses invariant time text");
            check(DbExtensions.FormatInvariantText(duration, "TIME") == "1.02:03:04.0050067",
                "MariaDB TIME duration uses constant invariant format");
            check(DbExtensions.FormatInvariantText(DBNull.Value, "DATETIME") is null,
                "database NULL remains null");

            var table = new DataTable();
            table.Columns.Add("text_value", typeof(string));
            table.Columns.Add("date_value", typeof(DateTime));
            table.Columns.Add("null_value", typeof(string));
            table.Rows.Add("reader text", dateTime, DBNull.Value);
            using var reader = table.CreateDataReader();
            check(reader.Read(), "in-memory temporal reader row available");
            check(reader.GetInvariantText(0) == "reader text",
                "reader helper preserves strings");
            check(reader.GetInvariantText(1, DbTextKind.Date) == "2026-08-20",
                "reader helper applies explicit date-only semantics");
            check(reader.GetInvariantText(2) is null,
                "reader helper handles database NULL");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    private static void CheckDefaultCategories(Action<bool, string> check)
    {
        var categories = Database.DefaultCategoryNames;
        check(categories.Contains("Telefon", StringComparer.Ordinal),
            "telephone default category available");
        check(categories.Contains("Internet", StringComparer.Ordinal),
            "internet default category available");
        check(categories.Contains("Fahrkosten", StringComparer.Ordinal),
            "travel-cost default category available");
        check(categories.All(value => !string.IsNullOrWhiteSpace(value)),
            "default categories contain no blank values");
        check(categories.Distinct(StringComparer.OrdinalIgnoreCase).Count() == categories.Count,
            "default categories are case-insensitively unique");
    }

    private static void CheckBankFixedCostMapping(Action<bool, string> check)
    {
        check(Database.SelectBankFixedCostDate("2026-08-02", "2026-08-01") == "2026-08-02",
            "bank fixed costs prefer the displayed value date");
        check(Database.SelectBankFixedCostDate("", "2026-08-01") == "2026-08-01",
            "bank fixed costs fall back to the entry date");
    }
}
