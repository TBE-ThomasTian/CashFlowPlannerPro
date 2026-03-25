using System.Data.Common;

namespace CashFlowPlannerPro.Data;

public static class DatabaseMigrator
{
    // Tables in dependency order (parents before children)
    private static readonly string[] Tables = [
        "users",
        "categories",
        "persons",
        "settings",
        "roles",
        "role_permissions",
        "projects",
        "resources",
        "customers",
        "transactions",
        "offers",
        "invoices",
        "targets",
        "resource_allocations",
        "hardware_resources",
        "hardware_allocations",
        "project_milestones",
        "user_todos",
        "time_entries"
    ];

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

        for (int i = 0; i < Tables.Length; i++)
        {
            var table = Tables[i];
            onProgress?.Invoke(table, i + 1, Tables.Length);

            try
            {
                int count = CopyTable(sourceConn, targetConn, table, Database.Instance.Dialect);
                result.TableCounts[table] = count;
                result.TotalRows += count;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{table}: {ex.Message}");
            }
        }

        result.Success = result.Errors.Count == 0;
        return result;
    }

    private static DbConnection GetTargetConnection()
    {
        // Access the connection via reflection or a helper — we use the Conn property
        // through a public method we'll add
        return Database.Instance.GetConnection();
    }

    private static int CopyTable(DbConnection source, DbConnection target, string table, IDbDialect targetDialect)
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

        if (rows.Count == 0) return 0;

        // Clear target table (to avoid duplicates)
        using (var delCmd = target.CreateCommand())
        {
            delCmd.CommandText = $"DELETE FROM {table}";
            delCmd.ExecuteNonQuery();
        }

        // Insert rows into target
        int inserted = 0;
        foreach (var row in rows)
        {
            using var insertCmd = target.CreateCommand();

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
