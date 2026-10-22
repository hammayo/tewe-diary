using System.Net.Sockets;
using Movies.Api.Endpoints.V2;
using Movies.Api.Mapping;
using Movies.Application.Database;
using Movies.Application.Database.Migrations;

namespace Movies.Api.Startup;

// Middleware pipeline and startup database migration, kept out of Program.cs.
public static class WebApplicationExtensions
{
    public static WebApplication UseApiPipeline(this WebApplication app)
    {
        // Must run before UseHttpsRedirection/auth so downstream middleware sees the forwarded scheme.
        app.UseForwardedHeaders();

        app.UseExceptionHandler();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
        }

        app.MapHealthChecks("_health");

        app.UseHttpsRedirection();

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseOutputCache();

        app.UseMiddleware<ValidationMappingMiddleware>();
        app.MapControllers();
        app.MapApiEndpoints();

        // After every endpoint is mapped: the version list is read here, so an earlier call would
        // only see the controllers (v1) and the UI would never offer the Minimal API version (v2).
        if (app.Environment.IsDevelopment())
        {
            app.UseSwaggerUI(x =>
            {
                foreach (var groupName in app.DescribeApiVersions().Select(d => d.GroupName))
                {
                    x.SwaggerEndpoint($"/swagger/{groupName}/swagger.json", groupName);
                }
            });
        }

        return app;
    }

    // In Azure the deploy pipeline applies migrations with an elevated role before the new version
    // goes live, so the web process must not self-migrate (Database:MigrateOnStartup=false there).
    // Locally this defaults on for zero-friction `dotnet run` / `./run.sh`. Called after the host is
    // already listening, so the service (and Swagger) stays reachable while Postgres comes up.
    public static async Task MigrateDatabaseAsync(this WebApplication app)
    {
        var databaseOptions = app.Services.GetRequiredService<DatabaseOptions>();
        if (!databaseOptions.MigrateOnStartup)
        {
            return;
        }

        var connectionString = databaseOptions.BuildConnectionString();

        const int maxAttempts = 10;
        var delay = TimeSpan.FromSeconds(2);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                MigrationRunner.Run(connectionString);
                app.Logger.LogInformation("Database migrations applied.");
                return;
            }
            catch (Exception ex) when (IsDatabaseUnavailable(ex))
            {
                if (attempt == maxAttempts)
                {
                    app.Logger.LogError(ex,
                        "Database still not reachable after {MaxAttempts} attempts. The service is running but database calls will fail until Postgres is available. Start it with: docker compose up -d",
                        maxAttempts);
                    return;
                }

                app.Logger.LogWarning(
                    "Database not reachable (attempt {Attempt}/{MaxAttempts}): {Message}. Retrying in {Delay}s...",
                    attempt, maxAttempts, ex.Message, delay.TotalSeconds);
                await Task.Delay(delay);
            }
        }
    }

    // FluentMigrator wraps the underlying Npgsql/socket failure it hits when Postgres isn't up yet,
    // so matching the top-level type isn't enough — walk the inner-exception chain.
    private static bool IsDatabaseUnavailable(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is SocketException)
            {
                return true;
            }
        }

        return false;
    }
}
