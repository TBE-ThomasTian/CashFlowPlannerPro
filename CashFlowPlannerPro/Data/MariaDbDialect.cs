using System.Data.Common;
using System.Text.RegularExpressions;
using MySqlConnector;

namespace CashFlowPlannerPro.Data;

public class MariaDbDialect : IDbDialect
{
    public DbConnection CreateConnection(string connectionString)
        => new MySqlConnection(connectionString);

    public void ConfigureConnection(DbConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SET NAMES utf8mb4;";
        cmd.ExecuteNonQuery();
    }

    public string AutoIncrementPk => "BIGINT PRIMARY KEY AUTO_INCREMENT";
    public string RealType => "DOUBLE";
    public string LastInsertIdSql => "SELECT LAST_INSERT_ID()";

    public string RewriteDdl(string sql)
    {
        var result = sql
            .Replace("INTEGER PRIMARY KEY AUTOINCREMENT", AutoIncrementPk)
            .Replace(" REAL ", $" {RealType} ")
            .Replace(" REAL,", $" {RealType},")
            .Replace(" REAL\n", $" {RealType}\n")
            .Replace(" REAL DEFAULT", $" {RealType} DEFAULT")
            // MariaDB does not support CURRENT_TIMESTAMP as a portable TEXT
            // default. Keep the default (instead of silently dropping it) by
            // using the native temporal type for audit timestamps.
            .Replace("TEXT DEFAULT CURRENT_TIMESTAMP", "DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP")
            // MariaDB: TEXT cannot be used in UNIQUE/PRIMARY KEY without length
            // Convert "TEXT UNIQUE NOT NULL" → "VARCHAR(255) UNIQUE NOT NULL"
            .Replace("TEXT UNIQUE NOT NULL", "VARCHAR(255) UNIQUE NOT NULL")
            // Convert "TEXT PRIMARY KEY" → "VARCHAR(191) PRIMARY KEY" (191*4=764 < 1000 byte limit)
            .Replace("TEXT PRIMARY KEY", "VARCHAR(191) PRIMARY KEY")
            // MariaDB: `key` is a reserved word
            .Replace("(key,", "(`key`,")
            .Replace("(key VARCHAR", "(`key` VARCHAR")
            .Replace(" key=", " `key`=")
            // MariaDB: `interval` is a reserved word
            .Replace(" interval TEXT", " `interval` TEXT");

        // Parent IDs are BIGINT; MariaDB requires integer foreign keys to use
        // the exact same size and signedness as the referenced column.
        result = Regex.Replace(
            result,
            @"\b([A-Za-z_][A-Za-z0-9_]*_id)\s+INTEGER\b",
            "$1 BIGINT",
            RegexOptions.IgnoreCase);

        return result;
    }

    public string InsertOrIgnore(string sql)
        => sql.Replace("INSERT OR IGNORE INTO", "INSERT IGNORE INTO");

    public string UpsertSettings(string valueParam)
        => $"INSERT INTO settings(`key`,value) VALUES('start_balance',{valueParam}) ON DUPLICATE KEY UPDATE value=VALUES(value)";

    public string UpsertRolePermission()
        => "INSERT INTO role_permissions(role_id,page_key,access_level) VALUES(@rid,@pk,@a) ON DUPLICATE KEY UPDATE access_level=VALUES(access_level)";

    public string DurationHoursExpr(string startCol, string endParam)
        => $"TIMESTAMPDIFF(SECOND, {startCol}, {endParam}) / 3600.0";

    public bool IsMigrationError(Exception ex)
        => ex is MySqlException { ErrorCode: MySqlErrorCode.DuplicateFieldName };
}
