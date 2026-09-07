using System.Data;
using Dapper;
using Movies.Application.Database;
using Xunit;

namespace Movies.Application.Tests;

[Collection("postgres")]
public class ImportTransformTests
{
    private readonly PostgresFixture _fx;

    public ImportTransformTests(PostgresFixture fx) => _fx = fx;

    private static string TransformSql() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "scripts", "import-transform.sql"));

    private static string[] FixtureLines() =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "import-sample.ndjson"));

    private static async Task RunImportAsync(IDbConnection connection, string[] ndjsonLines)
    {
        using var tx = connection.BeginTransaction();
        await connection.ExecuteAsync(
            "create temp table _import (doc jsonb) on commit drop;", transaction: tx);
        foreach (var line in ndjsonLines.Where(l => l.Trim().Length > 0))
        {
            await connection.ExecuteAsync(
                "insert into _import (doc) values (@doc::jsonb);",
                new { doc = line }, transaction: tx);
        }
        await connection.ExecuteAsync(TransformSql(), transaction: tx);
        tx.Commit();
    }

    [Fact]
    public async Task Transform_is_idempotent_on_tmdb_id()
    {
        var factory = new NpgsqlConnectionFactory(_fx.ConnectionString);
        await new DbInitializer(factory).InitializeAsync();
        using var connection = await factory.CreateConnectionAsync();
        await connection.ExecuteAsync("truncate movie_metadata, genres, movies cascade;");

        var lines = FixtureLines();
        await RunImportAsync(connection, lines);
        await RunImportAsync(connection, lines); // second run must add nothing

        Assert.Equal(2, await connection.ExecuteScalarAsync<int>("select count(*) from movies;"));
        Assert.Equal(4, await connection.ExecuteScalarAsync<int>("select count(*) from genres;"));
        Assert.Equal(2, await connection.ExecuteScalarAsync<int>("select count(*) from movie_metadata;"));
    }

    [Theory]
    [InlineData("Toy Story", 1995)]
    [InlineData("Monsters, Inc.", 2001)]
    [InlineData("Spider-Man: No Way Home", 2021)]
    [InlineData("WALL·E", 2008)]
    [InlineData("Amélie", 2001)]
    [InlineData("The   Big  Spaces", 1999)]
    public async Task Sql_slug_matches_csharp_slug(string title, int year)
    {
        var expected = new Movies.Application.Models.Movie
        {
            Id = Guid.NewGuid(), Title = title, YearOfRelease = year, Genres = []
        }.Slug;

        var factory = new NpgsqlConnectionFactory(_fx.ConnectionString);
        using var connection = await factory.CreateConnectionAsync();
        var actual = await connection.ExecuteScalarAsync<string>("""
            select replace(lower(regexp_replace(@title, '[^0-9A-Za-z _-]', '', 'g')), ' ', '-')
                   || '-' || @year::text
            """, new { title, year });

        Assert.Equal(expected, actual);
    }
}
