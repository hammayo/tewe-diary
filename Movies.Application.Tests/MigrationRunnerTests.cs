using Dapper;
using Movies.Application.Database;
using Movies.Application.Database.Migrations;

namespace Movies.Application.Tests;

[Collection("postgres")]
public class MigrationRunnerTests
{
    private readonly PostgresFixture _fx;

    public MigrationRunnerTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task Run_creates_the_baseline_schema()
    {
        MigrationRunner.Run(_fx.ConnectionString);

        var factory = new NpgsqlConnectionFactory(_fx.ConnectionString);
        using var connection = await factory.CreateConnectionAsync();

        var tables = (await connection.QueryAsync<string>("""
            select table_name from information_schema.tables
            where table_schema = 'public'
            """)).ToHashSet();

        Assert.Contains("movies", tables);
        Assert.Contains("genres", tables);
        Assert.Contains("ratings", tables);
        Assert.Contains("movie_metadata", tables);

        var metadataColumns = (await connection.QueryAsync<string>("""
            select column_name from information_schema.columns
            where table_name = 'movie_metadata'
            order by column_name
            """)).ToList();

        Assert.Equal(new[] { "fetched_at", "movieid", "raw", "tmdb_id" }, metadataColumns);
    }
}
