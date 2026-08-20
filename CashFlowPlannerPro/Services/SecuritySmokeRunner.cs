using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Models;
using Microsoft.Data.Sqlite;
using System.IO;
using System.Runtime.InteropServices;

namespace CashFlowPlannerPro.Services;

internal static class SecuritySmokeRunner
{
    public static int Run()
    {
        var checks = 0;
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "CashFlowPlannerPro-SmokeTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

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
            var databasePath = Path.Combine(tempRoot, "security-smoke.db");
            var config = new ConnectionConfig
            {
                Backend = DatabaseBackend.SQLite,
                FilePath = databasePath
            };

            Database.Instance.Open(config);
            Database.Instance.EnsureSchema();
            Database.Instance.EnsureSchema();

            Check(Database.Instance.ValidateUser("admin", "admin"), "default administrator login");
            var adminId = Database.Instance.GetUserId("admin");
            var adminSession = Database.Instance.GetUserSessionState(adminId);
            Check(adminSession is { IsActive: true }, "active administrator session");
            Check(adminSession!.SecurityStamp.Length == 64, "64-character security stamp");

            App.CurrentConnectionConfig = config.Clone();
            App.DatabasePath = databasePath;
            App.ApplySessionState(adminSession);
            Check(App.TryValidateCurrentSession(out _), "live session validation");

            Expect<ArgumentException>(
                () => Database.Instance.AddUserWithResource("weak-user", "password", "Weak User"),
                "weak password rejected in data layer");

            const string userPassword = "Strong!AuditPassphrase2026";
            var resource = Database.Instance.AddUserWithResource("audit-user", userPassword, "Audit User");
            var userId = Database.Instance.GetUserId("audit-user");
            var userBeforeRole = Database.Instance.GetUserSessionState(userId)!;
            Check(resource.UserId == userId, "new user linked to resource");

            var auditRole = new Role { Name = "Audit Operator", Description = "Smoke test role" };
            Database.Instance.AddRole(auditRole);
            Database.Instance.SetRolePermission(auditRole.Id, PageKeys.Transactions, "full");
            Database.Instance.SetUserRole("audit-user", auditRole.Id);
            var userAfterRole = Database.Instance.GetUserSessionState(userId)!;
            Check(userAfterRole.SecurityStamp != userBeforeRole.SecurityStamp, "role change rotates session stamp");
            Check(
                userAfterRole.Permissions.GetValueOrDefault(PageKeys.Transactions) == "full",
                "role permission applied");

            var userAfterPassword = Database.Instance.ChangePassword(
                userId,
                userAfterRole.SecurityStamp,
                userPassword,
                "EvenStronger!AuditPassphrase2027");
            Check(userAfterPassword.SecurityStamp != userAfterRole.SecurityStamp, "password change rotates session stamp");
            Check(
                Database.Instance.ValidateUser("audit-user", "EvenStronger!AuditPassphrase2027"),
                "new password authenticates");
            Expect<UnauthorizedAccessException>(
                () => Database.Instance.ChangePassword(
                    userId,
                    userAfterRole.SecurityStamp,
                    userPassword,
                    "AnotherStrong!AuditPassphrase2028"),
                "stale password session cannot overwrite newer credentials");

            Database.Instance.DeleteUser("audit-user");
            var inactiveUser = Database.Instance.GetUserSessionState(userId)!;
            Check(!inactiveUser.IsActive, "user deletion is a safe deactivation");
            Check(
                Database.Instance.GetResources().Any(item => item.Id == resource.Id),
                "resource survives user deactivation");
            Expect<InvalidOperationException>(
                () => Database.Instance.DeleteRole(
                    Database.Instance.GetRoles().Single(role => role.Name == "Admin").Id),
                "reserved administrator role cannot be deleted");
            Database.Instance.DeleteRole(auditRole.Id);
            Check(
                Database.Instance.GetRoles().All(role => role.Id != auditRole.Id),
                "unprotected role can be deleted safely");

            var bankAccount = new BankAccount
            {
                SourceProvider = "sevdesk",
                ExternalAccountId = "account-smoke",
                AccountName = "Smoke Account",
                IbanMasked = "DE**1234",
                Currency = "EUR",
                Balance = 500,
                LastSync = "2026-08-19T08:00:00Z"
            };
            var debit = new BankTransaction
            {
                SourceExternalId = "debit-smoke",
                EntryDate = "2026-08-19",
                ValueDate = "2026-08-19",
                Amount = -99.50,
                Currency = "EUR",
                Purpose = "Cloud subscription",
                Payee = "Example Provider",
                Status = "booked",
                IsSelected = true
            };
            var imported = Database.Instance.ImportBankTransactions(bankAccount, [debit]);
            Check(imported.Added == 1, "bank debit imported once");
            var fixedCost = Database.Instance.CreateFixedCostFromBankTransaction(
                "sevdesk", "account-smoke", "debit-smoke", "monatlich", "Cloud subscription", null);
            var repeatedFixedCost = Database.Instance.CreateFixedCostFromBankTransaction(
                "sevdesk", "account-smoke", "debit-smoke", "monatlich", "Changed text", null);
            Check(fixedCost.Id == repeatedFixedCost.Id, "bank-to-fixed-cost conversion is idempotent");
            Check(
                fixedCost.Amount == -99.50 && fixedCost.Interval == "monatlich",
                "fixed-cost amount and interval preserved");
            Check(
                Database.Instance.GetBankTransactions()
                    .Single(item => item.SourceExternalId == "debit-smoke")
                    .FixedCostTransactionId == fixedCost.Id,
                "bank movement linked to fixed cost");

            var credit = new BankTransaction
            {
                SourceExternalId = "credit-smoke",
                EntryDate = "2026-08-19",
                ValueDate = "2026-08-19",
                Amount = 10,
                Currency = "EUR",
                Purpose = "Refund",
                Status = "booked",
                IsSelected = true
            };
            Database.Instance.ImportBankTransactions(bankAccount, [credit]);
            Expect<InvalidOperationException>(
                () => Database.Instance.CreateFixedCostFromBankTransaction(
                    "sevdesk", "account-smoke", "credit-smoke", "monatlich", "Refund", null),
                "credit cannot become fixed cost");

            var backupPath = Path.Combine(tempRoot, "validated-backup.db");
            BackupService.CreateBackup(backupPath);
            Check(
                File.Exists(backupPath) && new FileInfo(backupPath).Length > 0,
                "validated backup created");
            BackupService.RestoreBackup(backupPath);
            Check(
                App.CurrentUserId == 0 && string.IsNullOrEmpty(App.CurrentSecurityStamp),
                "successful restore clears the active application session");
            Database.Instance.Open(config);
            Database.Instance.EnsureSchema();
            Check(
                Database.Instance.ValidateUser("admin", "admin"),
                "restored database validates after a fresh open");

            Database.Instance.Close();
            var hardLinkPath = Path.Combine(tempRoot, "same-database-hardlink.db");
            if (!CreateHardLink(hardLinkPath, databasePath, IntPtr.Zero))
                throw new IOException(
                    "The smoke-test hardlink could not be created.",
                    Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
            Database.Instance.Open(config);
            Database.Instance.EnsureSchema();
            var migration = DatabaseMigrator.Migrate(new ConnectionConfig
            {
                Backend = DatabaseBackend.SQLite,
                FilePath = hardLinkPath
            });
            Check(
                !migration.Success && migration.Errors.Any(error =>
                    error.Contains("dieselbe Datenbank", StringComparison.Ordinal)),
                "hardlink alias cannot migrate onto itself");
            Check(Database.Instance.ValidateUser("admin", "admin"), "same-database rejection preserves data");

            Database.Instance.Close();
            var legacySourcePath = Path.Combine(tempRoot, "legacy-source.db");
            var legacySourceConfig = new ConnectionConfig
            {
                Backend = DatabaseBackend.SQLite,
                FilePath = legacySourcePath
            };
            Database.Instance.Open(legacySourceConfig);
            Database.Instance.EnsureSchema();
            Database.Instance.AddUser(
                "legacy-user",
                "Strong!LegacyMigrationPassphrase2026",
                "Legacy User");
            using (var legacy = Database.Instance.GetConnection().CreateCommand())
            {
                legacy.CommandText = @"DROP INDEX idx_users_is_active;
                    ALTER TABLE users DROP COLUMN is_active;
                    ALTER TABLE users DROP COLUMN security_stamp;";
                legacy.ExecuteNonQuery();
            }
            Database.Instance.Close();

            var migratedTargetPath = Path.Combine(tempRoot, "legacy-target.db");
            var migratedTargetConfig = new ConnectionConfig
            {
                Backend = DatabaseBackend.SQLite,
                FilePath = migratedTargetPath
            };
            Database.Instance.Open(migratedTargetConfig);
            Database.Instance.EnsureSchema();
            var legacyMigration = DatabaseMigrator.Migrate(legacySourceConfig);
            Check(legacyMigration.Success, "legacy users migrate into current schema");
            Check(Database.Instance.ValidateUser("admin", "admin"), "legacy administrator remains active");
            var migratedAdmin = Database.Instance.GetUserSessionState(
                Database.Instance.GetUserId("admin"));
            var migratedLegacyUser = Database.Instance.GetUserSessionState(
                Database.Instance.GetUserId("legacy-user"));
            var migratedAdminStamp = migratedAdmin?.SecurityStamp;
            Check(
                migratedAdmin is { IsActive: true } && migratedAdmin.SecurityStamp.Length == 64,
                "legacy user receives a secure session stamp");
            Check(
                migratedLegacyUser is { IsActive: true } &&
                migratedLegacyUser.SecurityStamp.Length == 64 &&
                !string.Equals(
                    migratedLegacyUser.SecurityStamp,
                    migratedAdminStamp,
                    StringComparison.Ordinal),
                "each legacy user receives a distinct secure session stamp");

            Database.Instance.Close();
            var interimUpgradePath = Path.Combine(tempRoot, "interim-schema-upgrade.db");
            var longDuplicateNumber = new string('X', 150);
            using (var interim = new SqliteConnection($"Data Source={interimUpgradePath}"))
            {
                interim.Open();
                using var createInterim = interim.CreateCommand();
                createInterim.CommandText = @"CREATE TABLE settings(`key` TEXT PRIMARY KEY,value TEXT);
                    INSERT INTO settings(`key`,value) VALUES('schema_version','2026.08.19.1');
                    CREATE TABLE offers(
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        offer_number TEXT, offer_date TEXT, date_expected TEXT,
                        customer TEXT, amount_before_discount REAL DEFAULT 0,
                        discount_percent REAL DEFAULT 0, amount REAL, probability REAL,
                        description TEXT, status TEXT, payment_delay INTEGER DEFAULT 30,
                        pdf_path TEXT, created_at TEXT DEFAULT CURRENT_TIMESTAMP);
                    INSERT INTO offers(offer_number) VALUES('ANG-42');
                    INSERT INTO offers(offer_number) VALUES('ANG-42');
                    INSERT INTO offers(offer_number) VALUES('ANG-42-DUP-2');
                    INSERT INTO offers(offer_number) VALUES(@longNumber);
                    INSERT INTO offers(offer_number) VALUES(@longNumber);";
                createInterim.Parameters.AddWithValue("@longNumber", longDuplicateNumber);
                createInterim.ExecuteNonQuery();
            }

            var interimUpgradeConfig = new ConnectionConfig
            {
                Backend = DatabaseBackend.SQLite,
                FilePath = interimUpgradePath
            };
            Database.Instance.Open(interimUpgradeConfig);
            Database.Instance.EnsureSchema();

            static List<string> ReadOfferNumbers()
            {
                var numbers = new List<string>();
                using var command = Database.Instance.GetConnection().CreateCommand();
                command.CommandText = "SELECT offer_number FROM offers ORDER BY id";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    numbers.Add(reader.GetString(0));
                return numbers;
            }

            var upgradedNumbers = ReadOfferNumbers();
            Check(
                upgradedNumbers.Count == 5 && upgradedNumbers.Distinct(StringComparer.Ordinal).Count() == 5,
                "interim duplicate offer numbers become unique");
            Check(
                upgradedNumbers[0] == "ANG-42"
                && upgradedNumbers[1] == "ANG-42-DUP-2-1"
                && upgradedNumbers[2] == "ANG-42-DUP-2",
                "lowest offer ID keeps its number and occupied suffixes are skipped deterministically");
            Check(
                upgradedNumbers[4].Length <= 100 && upgradedNumbers[4].EndsWith("-DUP-5", StringComparison.Ordinal),
                "renamed duplicate offer number is capped at 100 characters");
            Check(
                Database.Instance.GetSetting("schema_version") == "2026.08.19.2",
                "interim schema version advances to the current version");

            Database.Instance.EnsureSchema();
            Check(
                upgradedNumbers.SequenceEqual(ReadOfferNumbers(), StringComparer.Ordinal),
                "interim offer-number upgrade is idempotent");

            Console.WriteLine($"Security smoke tests passed: {checks} checks.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            Database.Instance.Close();
            SqliteConnection.ClearAllPools();
            App.ClearSessionState();
            RemoveValidatedTempDirectory(tempRoot);
        }
    }

    private static void RemoveValidatedTempDirectory(string tempRoot)
    {
        var normalizedTemp = Path.GetFullPath(Path.GetTempPath());
        var normalizedTarget = Path.GetFullPath(tempRoot);
        if (!normalizedTarget.StartsWith(normalizedTemp, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(normalizedTarget)
                .StartsWith("CashFlowPlannerPro-SmokeTests-", StringComparison.Ordinal) ||
            !Directory.Exists(normalizedTarget) ||
            (File.GetAttributes(normalizedTarget) & FileAttributes.ReparsePoint) != 0)
        {
            return;
        }

        Directory.Delete(normalizedTarget, recursive: true);
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(
        string newFileName,
        string existingFileName,
        IntPtr securityAttributes);
}
