using System.Data.Common;
using System.Globalization;

namespace CashFlowPlannerPro.Data;

internal enum DbTextKind
{
    Auto,
    Date,
    DateTime,
    Time
}

public static class DbExtensions
{
    /// <summary>
    /// Provides AddWithValue for the MariaDB parameter collection; the base
    /// DbParameterCollection class does not expose this provider convenience.
    /// </summary>
    public static DbParameter AddWithValue(this DbParameterCollection collection, string parameterName, object value)
    {
        return collection is MySqlConnector.MySqlParameterCollection mysql
            ? mysql.AddWithValue(parameterName, value)
            : throw new NotSupportedException($"Unsupported parameter collection: {collection.GetType().Name}");
    }

    /// <summary>
    /// Reads a database value as stable invariant text. MariaDB exposes native
    /// DATE, DATETIME, TIMESTAMP and TIME columns as CLR temporal values, so
    /// DbDataReader.GetString cannot safely be used for schema-compatible reads.
    /// </summary>
    internal static string? GetInvariantText(
        this DbDataReader reader,
        int ordinal,
        DbTextKind kind = DbTextKind.Auto)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (reader.IsDBNull(ordinal))
            return null;

        return FormatInvariantText(
            reader.GetValue(ordinal),
            reader.GetDataTypeName(ordinal),
            kind);
    }

    internal static string? FormatInvariantText(
        object? value,
        string? providerTypeName = null,
        DbTextKind kind = DbTextKind.Auto)
    {
        if (value is null or DBNull)
            return null;
        if (value is string text)
            return text;

        var resolvedKind = ResolveTextKind(kind, providerTypeName);
        return value switch
        {
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime dateTime => resolvedKind switch
            {
                DbTextKind.Date => dateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                DbTextKind.Time => dateTime.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture),
                _ => dateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture)
            },
            DateTimeOffset dateTimeOffset => resolvedKind switch
            {
                DbTextKind.Date => dateTimeOffset.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                DbTextKind.Time => dateTimeOffset.ToString("HH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture),
                _ => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture)
            },
            TimeOnly time => time.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture),
            TimeSpan duration => duration.ToString("c", CultureInfo.InvariantCulture),
            char character => character.ToString(),
            char[] characters => new string(characters),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
    }

    private static DbTextKind ResolveTextKind(DbTextKind requested, string? providerTypeName)
    {
        if (requested != DbTextKind.Auto)
            return requested;

        var normalized = providerTypeName?.Trim().ToUpperInvariant() ?? "";
        if (normalized is "DATE" or "NEWDATE")
            return DbTextKind.Date;
        if (normalized.StartsWith("TIME", StringComparison.Ordinal)
            && !normalized.StartsWith("TIMESTAMP", StringComparison.Ordinal))
            return DbTextKind.Time;
        return DbTextKind.DateTime;
    }
}
