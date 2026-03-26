namespace CashFlowPlannerPro.Data;

public enum DatabaseBackend { SQLite, MariaDB }

public class ConnectionConfig
{
    public DatabaseBackend Backend { get; set; } = DatabaseBackend.SQLite;

    // SQLite
    public string FilePath { get; set; } = "";

    // MariaDB
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 3306;
    public string DatabaseName { get; set; } = "cashflow";
    public string DbUsername { get; set; } = "";
    public string DbPassword { get; set; } = "";

    public string ToConnectionString() => Backend switch
    {
        DatabaseBackend.SQLite => $"Data Source={FilePath}",
        DatabaseBackend.MariaDB => $"Server={Host};Port={Port};Database={DatabaseName};User={DbUsername};Password={DbPassword};CharSet=utf8mb4;AllowUserVariables=true",
        _ => throw new ArgumentOutOfRangeException()
    };

    public ConnectionConfig Clone() => new()
    {
        Backend = Backend,
        FilePath = FilePath,
        Host = Host,
        Port = Port,
        DatabaseName = DatabaseName,
        DbUsername = DbUsername,
        DbPassword = DbPassword
    };
}
