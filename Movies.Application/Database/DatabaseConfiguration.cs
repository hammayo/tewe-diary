using Microsoft.Extensions.Configuration;

namespace Movies.Application.Database;

// Shared resolution of DatabaseOptions from configuration, so the web app (Movies.Api) and the
// ops CLI (Movies.DbTool) build the connection string the same way. Binds the "Database" section,
// then falls back the individual parts to the POSTGRES_* variables shared with docker-compose.
public static class DatabaseConfiguration
{
    public static DatabaseOptions Resolve(IConfiguration config)
    {
        var options = config.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()
                      ?? new DatabaseOptions();

        options.Host ??= config["POSTGRES_HOST"];
        options.Port ??= config["POSTGRES_PORT"];
        options.Name ??= config["POSTGRES_DB"];
        options.User ??= config["POSTGRES_USER"];
        options.Password ??= config["POSTGRES_PASSWORD"];

        return options;
    }
}
