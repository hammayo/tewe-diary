using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Movies.Api.Startup;
using Movies.Application.Database;
using Npgsql;

namespace Movies.Api.Tests;

[Collection("api")]
public class WebApplicationExtensionsTests
{
    private readonly ApiFixture _fx;

    public WebApplicationExtensionsTests(ApiFixture fx) => _fx = fx;

    private static WebApplication AppWith(DatabaseOptions options)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(options);
        return builder.Build();
    }

    private async Task<string> CreateEmptyDatabaseAsync()
    {
        var name = $"migrate_{Guid.NewGuid():N}";
        await using var admin = new NpgsqlConnection(_fx.ConnectionString);
        await admin.ExecuteAsync($"create database {name};");
        return new NpgsqlConnectionStringBuilder(_fx.ConnectionString) { Database = name, Pooling = false }.ConnectionString;
    }

    [Fact]
    public async Task migrate_is_skipped_when_migrate_on_startup_is_off()
    {
        // Arrange
        var connectionString = await CreateEmptyDatabaseAsync();
        await using var app = AppWith(new DatabaseOptions { ConnectionString = connectionString, MigrateOnStartup = false });

        // Act
        await app.MigrateDatabaseAsync();

        // Assert
        await using var connection = new NpgsqlConnection(connectionString);
        var movies = await connection.ExecuteScalarAsync<string?>("select to_regclass('public.movies')::text;");
        Assert.Null(movies);
    }

    [Fact]
    public async Task migrate_creates_the_schema_when_migrate_on_startup_is_on()
    {
        // Arrange
        var connectionString = await CreateEmptyDatabaseAsync();
        await using var app = AppWith(new DatabaseOptions { ConnectionString = connectionString, MigrateOnStartup = true });

        // Act
        await app.MigrateDatabaseAsync();

        // Assert
        await using var connection = new NpgsqlConnection(connectionString);
        var movies = await connection.ExecuteScalarAsync<string?>("select to_regclass('public.movies')::text;");
        Assert.Equal("movies", movies);
    }

    [Fact]
    public async Task migrate_rethrows_failures_that_are_not_a_missing_database()
    {
        // Arrange
        // Wrong password: the server is reachable, so this is not retried like a down database.
        var badLogin = new NpgsqlConnectionStringBuilder(_fx.ConnectionString) { Password = "wrong-password", Pooling = false }.ConnectionString;
        await using var app = AppWith(new DatabaseOptions { ConnectionString = badLogin, MigrateOnStartup = true });

        // Act
        var exception = await Record.ExceptionAsync(() => app.MigrateDatabaseAsync());

        // Assert
        Assert.NotNull(exception);
    }
}
