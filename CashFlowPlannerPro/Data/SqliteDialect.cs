using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace CashFlowPlannerPro.Data;

public class SqliteDialect : IDbDialect
{
    public DbConnection CreateConnection(string connectionString)
        => new SqliteConnection(connectionString);

    public void ConfigureConnection(DbConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL;";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "PRAGMA foreign_keys=ON;";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "PRAGMA busy_timeout=5000;";
        cmd.ExecuteNonQuery();
    }

    public string AutoIncrementPk => "INTEGER PRIMARY KEY AUTOINCREMENT";
    public string RealType => "REAL";
    public string LastInsertIdSql => "SELECT last_insert_rowid()";

    public string RewriteDdl(string sql) => sql;

    public string InsertOrIgnore(string sql) => sql; // already uses "INSERT OR IGNORE"

    public string UpsertSettings(string valueParam)
        => $"INSERT INTO settings(key,value) VALUES('start_balance',{valueParam}) ON CONFLICT(key) DO UPDATE SET value=excluded.value";

    public string UpsertRolePermission()
        => "INSERT INTO role_permissions(role_id,page_key,access_level) VALUES(@rid,@pk,@a) ON CONFLICT(role_id,page_key) DO UPDATE SET access_level=excluded.access_level";

    public string DurationHoursExpr(string startCol, string endParam)
        => $"(julianday({endParam}) - julianday({startCol})) * 24.0";

    public bool IsMigrationError(Exception ex)
        => ex is SqliteException sqEx &&
           (sqEx.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase) ||
            sqEx.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase));
}
