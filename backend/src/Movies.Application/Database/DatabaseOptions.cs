using Npgsql;

namespace Movies.Application.Database;

// Central definition of how the app connects to Postgres, bound from the "Database" config
// section. A full ConnectionString (e.g. supplied from Key Vault in Azure) is authoritative;
// otherwise the string is built from the individual parts shared with docker-compose, with SSL
// applied in one place so managed Postgres (Azure Flexible Server requires TLS) and local docker
// both work.
public class DatabaseOptions
{
    public const string SectionName = "Database";

    public string? ConnectionString { get; set; }

    public string? Host { get; set; }
    public string? Port { get; set; }
    public string? Name { get; set; }
    public string? User { get; set; }
    public string? Password { get; set; }

    // Npgsql SslMode. "Prefer" locally (docker has no TLS, so it falls back to plaintext).
    // Against Azure Flexible Server use "Require" (encrypt, no cert validation) or "VerifyFull"
    // (encrypt + validate the chain and hostname). In Npgsql 10 validation is driven entirely by
    // this value, so no separate TrustServerCertificate flag is needed.
    public string SslMode { get; set; } = "Prefer";

    public bool MigrateOnStartup { get; set; } = true;

    public string BuildConnectionString()
    {
        // A full connection string is authoritative and carries its own SSL settings.
        if (!string.IsNullOrWhiteSpace(ConnectionString))
        {
            return ConnectionString;
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Host,
            Port = int.TryParse(Port, out var port) ? port : 5432,
            Database = Name,
            Username = User,
            Password = Password
        };

        if (Enum.TryParse<SslMode>(SslMode, ignoreCase: true, out var sslMode))
        {
            builder.SslMode = sslMode;
        }

        return builder.ConnectionString;
    }
}
