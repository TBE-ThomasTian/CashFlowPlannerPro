using System.Data;
using System.Data.Common;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using MySqlConnector;

namespace CashFlowPlannerPro.Data;

public static class DatabaseMigrator
{
    // Tables in dependency order (parents before children). This order is also
    // used in reverse when the target is cleared, so foreign keys remain valid.
    private static readonly string[] Tables = [
        "roles",
        "categories",
        "persons",
        "settings",
        "customers",
        "projects",
        "users",
        "resources",
        "hardware_resources",
        "transactions",
        "bank_accounts",
        "bank_transactions",
        "offers",
        "invoices",
        "targets",
        "role_permissions",
        "project_milestones",
        "resource_allocations",
        "hardware_allocations",
        "document_contents",
        "document_line_items",
        "user_todos",
        "user_settings",
        "time_entries"
    ];

    // These tables were added after the first released database format. Older
    // source databases are valid even when they do not contain them yet.
    private static readonly HashSet<string> OptionalSourceTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "document_contents",
        "document_line_items",
        "user_settings",
        "bank_accounts",
        "bank_transactions"
    };

    /// <summary>
    /// Migrates all data from source to the currently open Database.Instance.
    /// The target (Database.Instance) must already be open with EnsureSchema() called.
    /// </summary>
    public static MigrationResult Migrate(ConnectionConfig sourceConfig, Action<string, int, int>? onProgress = null)
    {
        var result = new MigrationResult();
        IDbDialect sourceDialect = sourceConfig.Backend switch
        {
            DatabaseBackend.MariaDB => new MariaDbDialect(),
            _ => new SqliteDialect()
        };

        var sourceConnStr = sourceConfig.ToConnectionString();
        using var sourceConn = sourceDialect.CreateConnection(sourceConnStr);
        sourceConn.Open();
        sourceDialect.ConfigureConnection(sourceConn);

        var targetConn = GetTargetConnection();
        if (IsSameDatabase(sourceConn, targetConn))
        {
            result.Errors.Add("Quelle und Ziel sind dieselbe Datenbank. Die Migration wurde zum Schutz der Daten abgebrochen.");
            result.Success = false;
            return result;
        }

        using var sourceTransaction = sourceConn.BeginTransaction(IsolationLevel.Serializable);
        var availableSourceTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in Tables)
        {
            if (TableExists(sourceConn, table, sourceTransaction))
            {
                availableSourceTables.Add(table);
                continue;
            }

            if (!OptionalSourceTables.Contains(table))
            {
                result.Errors.Add($"{table}: Die Tabelle fehlt in der Quelldatenbank.");
                result.Success = false;
                return result;
            }
        }

        foreach (var table in Tables)
        {
            if (!TableExists(targetConn, table, transaction: null))
            {
                result.Errors.Add($"{table}: Die Tabelle fehlt in der Zieldatenbank.");
                result.Success = false;
                return result;
            }
        }

        var copiedCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string currentTable = "";
        using var transaction = targetConn.BeginTransaction();
        try
        {
            foreach (var table in Tables.Reverse())
            {
                currentTable = table;
                using var delete = targetConn.CreateCommand();
                delete.Transaction = transaction;
                delete.CommandText = $"DELETE FROM {table}";
                delete.ExecuteNonQuery();
            }

            for (int i = 0; i < Tables.Length; i++)
            {
                currentTable = Tables[i];
                onProgress?.Invoke(currentTable, i + 1, Tables.Length);

                int count = availableSourceTables.Contains(currentTable)
                    ? CopyTable(sourceConn, targetConn, currentTable, sourceTransaction, transaction)
                    : 0;
                copiedCounts[currentTable] = count;
            }

            sourceTransaction.Commit();
            transaction.Commit();
        }
        catch (Exception ex)
        {
            try { transaction.Rollback(); } catch { }
            result.Errors.Add($"{currentTable}: {ex.Message}");
            result.Success = false;
            return result;
        }

        foreach (var (table, count) in copiedCounts)
        {
            result.TableCounts[table] = count;
            result.TotalRows += count;
        }

        result.Success = true;
        return result;
    }

    private static DbConnection GetTargetConnection()
    {
        // Access the connection via reflection or a helper — we use the Conn property
        // through a public method we'll add
        return Database.Instance.GetConnection();
    }

    private static bool TableExists(DbConnection connection, string table, DbTransaction? transaction)
    {
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = $"SELECT 1 FROM {QuoteIdentifier(table, connection)} WHERE 1=0";
            cmd.ExecuteScalar();
            return true;
        }
        catch (DbException)
        {
            return false;
        }
    }

    private static int CopyTable(
        DbConnection source,
        DbConnection target,
        string table,
        DbTransaction sourceTransaction,
        DbTransaction targetTransaction)
    {
        var targetColumns = GetColumnNames(target, table, targetTransaction);
        using var sourceCommand = source.CreateCommand();
        sourceCommand.Transaction = sourceTransaction;
        sourceCommand.CommandText = $"SELECT * FROM {QuoteIdentifier(table, source)}";
        using var reader = sourceCommand.ExecuteReader();

        var acceptedColumns = new List<(int SourceOrdinal, string TargetName)>();
        for (var sourceOrdinal = 0; sourceOrdinal < reader.FieldCount; sourceOrdinal++)
        {
            var sourceName = reader.GetName(sourceOrdinal);
            if (targetColumns.TryGetValue(sourceName, out var targetName))
                acceptedColumns.Add((sourceOrdinal, targetName));
        }

        var isUsersTable = string.Equals(table, "users", StringComparison.OrdinalIgnoreCase);
        if (isUsersTable)
        {
            AddSyntheticUserColumnIfMissing(acceptedColumns, targetColumns, "is_active");
            AddSyntheticUserColumnIfMissing(acceptedColumns, targetColumns, "security_stamp");
        }

        if (acceptedColumns.Count == 0)
            throw new InvalidOperationException($"Die Tabelle '{table}' hat keine kompatiblen Spalten.");

        using var insertCommand = target.CreateCommand();
        insertCommand.Transaction = targetTransaction;
        var quotedTable = QuoteIdentifier(table, target);
        var quotedColumns = acceptedColumns.Select(column => QuoteIdentifier(column.TargetName, target));
        var parameterNames = acceptedColumns.Select((_, index) => $"@p{index}").ToArray();
        insertCommand.CommandText = $"INSERT INTO {quotedTable} ({string.Join(",", quotedColumns)}) VALUES ({string.Join(",", parameterNames)})";

        for (var index = 0; index < acceptedColumns.Count; index++)
        {
            var parameter = insertCommand.CreateParameter();
            parameter.ParameterName = parameterNames[index];
            insertCommand.Parameters.Add(parameter);
        }

        int inserted = 0;
        while (reader.Read())
        {
            for (var index = 0; index < acceptedColumns.Count; index++)
            {
                var ordinal = acceptedColumns[index].SourceOrdinal;
                var targetName = acceptedColumns[index].TargetName;
                insertCommand.Parameters[index].Value = isUsersTable
                    ? GetMigratedUserColumnValue(reader, ordinal, targetName)
                    : ordinal < 0 || reader.IsDBNull(ordinal)
                        ? DBNull.Value
                        : reader.GetValue(ordinal);
            }

            insertCommand.ExecuteNonQuery();
            inserted++;
        }

        return inserted;
    }

    private static void AddSyntheticUserColumnIfMissing(
        ICollection<(int SourceOrdinal, string TargetName)> acceptedColumns,
        IReadOnlyDictionary<string, string> targetColumns,
        string columnName)
    {
        if (targetColumns.TryGetValue(columnName, out var targetName) &&
            !acceptedColumns.Any(column =>
                string.Equals(column.TargetName, targetName, StringComparison.OrdinalIgnoreCase)))
        {
            acceptedColumns.Add((-1, targetName));
        }
    }

    private static object GetMigratedUserColumnValue(
        DbDataReader reader,
        int sourceOrdinal,
        string targetName)
    {
        if (string.Equals(targetName, "is_active", StringComparison.OrdinalIgnoreCase))
        {
            if (sourceOrdinal < 0 || reader.IsDBNull(sourceOrdinal))
                return 1;

            return Convert.ToInt64(
                reader.GetValue(sourceOrdinal),
                System.Globalization.CultureInfo.InvariantCulture) == 0 ? 0 : 1;
        }

        if (string.Equals(targetName, "security_stamp", StringComparison.OrdinalIgnoreCase))
        {
            var sourceStamp = sourceOrdinal < 0 || reader.IsDBNull(sourceOrdinal)
                ? ""
                : reader.GetValue(sourceOrdinal)?.ToString() ?? "";
            return IsValidSecurityStamp(sourceStamp)
                ? sourceStamp
                : Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        }

        return sourceOrdinal < 0 || reader.IsDBNull(sourceOrdinal)
            ? DBNull.Value
            : reader.GetValue(sourceOrdinal);
    }

    private static bool IsValidSecurityStamp(string value) =>
        value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9'
                or >= 'A' and <= 'F'
                or >= 'a' and <= 'f');

    private static Dictionary<string, string> GetColumnNames(
        DbConnection connection,
        string table,
        DbTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT * FROM {QuoteIdentifier(table, connection)} WHERE 1=0";
        using var reader = command.ExecuteReader();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < reader.FieldCount; index++)
        {
            var name = reader.GetName(index);
            result[name] = name;
        }

        return result;
    }

    private static string QuoteIdentifier(string identifier, DbConnection connection)
    {
        if (!Tables.Contains(identifier, StringComparer.OrdinalIgnoreCase) &&
            !IsSafeIdentifier(identifier))
        {
            throw new InvalidOperationException($"Ungültiger Datenbankbezeichner: {identifier}");
        }

        return connection is MySqlConnection
            ? $"`{identifier.Replace("`", "``", StringComparison.Ordinal)}`"
            : $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static bool IsSafeIdentifier(string identifier)
        => !string.IsNullOrWhiteSpace(identifier) &&
           identifier.All(character => char.IsLetterOrDigit(character) || character == '_') &&
           (char.IsLetter(identifier[0]) || identifier[0] == '_');

    private static bool IsSameDatabase(DbConnection source, DbConnection target)
    {
        if (source is SqliteConnection sourceSqlite && target is SqliteConnection targetSqlite)
        {
            var sourcePath = Path.GetFullPath(sourceSqlite.DataSource);
            var targetPath = Path.GetFullPath(targetSqlite.DataSource);
            if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
                return true;

            return File.Exists(sourcePath) && File.Exists(targetPath) &&
                   FilesReferToSameObject(sourcePath, targetPath);
        }

        if (source is MySqlConnection && target is MySqlConnection)
        {
            // Compare the server-reported identity, not connection-string host
            // aliases (DNS name vs. IP vs. localhost). A false negative here
            // would make the migrator delete its own source before copying.
            var sourceIdentity = ReadMariaDatabaseIdentity(source);
            var targetIdentity = ReadMariaDatabaseIdentity(target);
            return sourceIdentity == targetIdentity;
        }

        return false;
    }

    private static (string Host, int Port, string Database) ReadMariaDatabaseIdentity(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT @@hostname,@@port,DATABASE()";
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new InvalidOperationException("Die MariaDB-Serveridentität konnte nicht ermittelt werden.");

        return (
            reader.IsDBNull(0) ? "" : reader.GetString(0).Trim().ToUpperInvariant(),
            reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1), System.Globalization.CultureInfo.InvariantCulture),
            reader.IsDBNull(2) ? "" : reader.GetString(2).Trim().ToUpperInvariant());
    }

    private static bool FilesReferToSameObject(string firstPath, string secondPath)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        using var first = new FileStream(
            firstPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var second = new FileStream(
            secondPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        if (!GetFileInformationByHandle(first.SafeFileHandle, out var firstInfo) ||
            !GetFileInformationByHandle(second.SafeFileHandle, out var secondInfo))
        {
            throw new IOException(
                "Die physische Identität der SQLite-Dateien konnte nicht sicher geprüft werden.",
                Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
        }

        return firstInfo.VolumeSerialNumber == secondInfo.VolumeSerialNumber &&
               firstInfo.FileIndexHigh == secondInfo.FileIndexHigh &&
               firstInfo.FileIndexLow == secondInfo.FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        Microsoft.Win32.SafeHandles.SafeFileHandle file,
        out ByHandleFileInformation information);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}

public class MigrationResult
{
    public bool Success { get; set; }
    public int TotalRows { get; set; }
    public Dictionary<string, int> TableCounts { get; set; } = new();
    public List<string> Errors { get; set; } = [];

    public string Summary()
    {
        var msg = $"{TotalRows} Datensätze aus {TableCounts.Count(x => x.Value > 0)} Tabellen migriert.";
        if (Errors.Count > 0)
            msg += $"\n\n{Errors.Count} Fehler:\n" + string.Join("\n", Errors);
        return msg;
    }
}
