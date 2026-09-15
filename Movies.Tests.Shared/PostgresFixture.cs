using Dapper;
using Movies.Application.Database.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Movies.Tests.Shared;

// Spins up a throwaway Postgres in Docker, applies the FluentMigrator schema once, and offers
// ResetAsync() to truncate between tests. Shared by every integration/repository test so the
// container + migration wiring lives in exactly one place.
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:latest")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        MigrationRunner.Run(ConnectionString);
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    // Wipe all data (schema stays) so each test starts from a known-empty state.
    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("truncate movie_metadata, genres, ratings, movies cascade;");
    }
}
