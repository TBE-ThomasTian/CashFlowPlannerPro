using System.Data.Common;

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
        "user_settings"
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
        var availableSourceTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in Tables)
        {
            if (TableExists(sourceConn, table))
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
                    ? CopyTable(sourceConn, targetConn, currentTable, Database.Instance.Dialect, transaction)
                    : 0;
                copiedCounts[currentTable] = count;
            }

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

    private static bool TableExists(DbConnection connection, string table)
    {
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT 1 FROM {table} WHERE 1=0";
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
        IDbDialect targetDialect,
        DbTransaction transaction)
    {
        // Read all rows from source
        var rows = new List<Dictionary<string, object?>>();
        string[] columnNames;

        using (var cmd = source.CreateCommand())
        {
            cmd.CommandText = $"SELECT * FROM {table}";
            using var reader = cmd.ExecuteReader();

            columnNames = new string[reader.FieldCount];
            for (int i = 0; i < reader.FieldCount; i++)
                columnNames[i] = reader.GetName(i);

            while (reader.Read())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                    row[columnNames[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rows.Add(row);
            }
        }

        // Insert rows into target
        int inserted = 0;
        foreach (var row in rows)
        {
            using var insertCmd = target.CreateCommand();
            insertCmd.Transaction = transaction;

            // Quote 'key' column for MariaDB
            var cols = columnNames.Select(c =>
                c == "key" && targetDialect is MariaDbDialect ? "`key`" : c);
            var paramNames = columnNames.Select((_, i) => $"@p{i}");

            insertCmd.CommandText = $"INSERT INTO {table} ({string.Join(",", cols)}) VALUES ({string.Join(",", paramNames)})";

            for (int i = 0; i < columnNames.Length; i++)
            {
                var val = row[columnNames[i]];
                insertCmd.Parameters.AddWithValue($"@p{i}", val ?? DBNull.Value);
            }

            insertCmd.ExecuteNonQuery();
            inserted++;
        }

        return inserted;
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
