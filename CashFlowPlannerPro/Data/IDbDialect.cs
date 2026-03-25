using System.Data.Common;

namespace CashFlowPlannerPro.Data;

public interface IDbDialect
{
    DbConnection CreateConnection(string connectionString);
    void ConfigureConnection(DbConnection conn);

    string AutoIncrementPk { get; }
    string RealType { get; }
    string LastInsertIdSql { get; }

    string RewriteDdl(string sql);
    string InsertOrIgnore(string sql);
    string UpsertSettings(string valueParam);
    string UpsertRolePermission();
    string DurationHoursExpr(string startCol, string endParam);

    bool IsMigrationError(Exception ex);
}
