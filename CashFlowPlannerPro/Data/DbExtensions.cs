using System.Data.Common;

namespace CashFlowPlannerPro.Data;

public static class DbExtensions
{
    /// <summary>
    /// Provides AddWithValue for DbParameterCollection, which both
    /// SqliteParameterCollection and MySqlParameterCollection support
    /// but the base DbParameterCollection class does not expose.
    /// </summary>
    public static DbParameter AddWithValue(this DbParameterCollection collection, string parameterName, object value)
    {
        // Both SQLite and MySQL parameter collections support AddWithValue,
        // but it's not on the base class. We use CreateParameter pattern instead.
        // We need the owning command to call CreateParameter, so we build the param manually.
        switch (collection)
        {
            case Microsoft.Data.Sqlite.SqliteParameterCollection sqlite:
                return sqlite.AddWithValue(parameterName, value);
            case MySqlConnector.MySqlParameterCollection mysql:
                return mysql.AddWithValue(parameterName, value);
            default:
                throw new NotSupportedException($"Unsupported parameter collection: {collection.GetType().Name}");
        }
    }
}
