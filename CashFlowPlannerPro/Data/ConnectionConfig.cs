using MySqlConnector;

namespace CashFlowPlannerPro.Data;

public class ConnectionConfig
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 3306;
    public string DatabaseName { get; set; } = "cashflow";
    public string DbUsername { get; set; } = "";
    public string DbPassword { get; set; } = "";

    public string ToConnectionString() => BuildMariaDbConnectionString();

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
        Host = Host,
        Port = Port,
        DatabaseName = DatabaseName,
        DbUsername = DbUsername,
        DbPassword = DbPassword
    };
}
