using MySqlConnector;

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
        DatabaseBackend.MariaDB => BuildMariaDbConnectionString(),
        _ => throw new ArgumentOutOfRangeException()
    };

    private string BuildMariaDbConnectionString()
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = Host,
            Port = checked((uint)Port),
            Database = DatabaseName,
            UserID = DbUsername,
            Password = DbPassword,
            CharacterSet = "utf8mb4",
            AllowUserVariables = true,
            SslMode = MySqlSslMode.VerifyFull
        };

        return builder.ConnectionString;
    }

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
