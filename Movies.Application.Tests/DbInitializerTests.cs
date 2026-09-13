using Dapper;
using Movies.Application.Database;

namespace Movies.Application.Tests;

[Collection("postgres")]
public class DbInitializerTests
{
    private readonly PostgresFixture _fx;

    public DbInitializerTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task InitializeAsync_creates_movie_metadata_table()
    {
        var factory = new NpgsqlConnectionFactory(_fx.ConnectionString);
        await new DbInitializer(factory).InitializeAsync();

        using var connection = await factory.CreateConnectionAsync();
        var columns = (await connection.QueryAsync<string>("""
            select column_name from information_schema.columns
            where table_name = 'movie_metadata'
            order by column_name
            """)).ToList();

        Assert.Equal(new[] { "fetched_at", "movieid", "raw", "tmdb_id" }, columns);
    }
}
