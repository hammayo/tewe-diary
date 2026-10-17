using Dapper;
using Movies.Application.Database;
using Movies.Application.Database.Migrations;
using Movies.Application.Repositories;
using Movies.Tests.Shared;
using Npgsql;

namespace Movies.Application.Tests;

[Collection("postgres")]
public class MigrationRunnerTests
{
    private readonly PostgresFixture _fx;

    public MigrationRunnerTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task run_creates_the_baseline_schema()
    {
        // Arrange
        var factory = new NpgsqlConnectionFactory(_fx.ConnectionString);

        // Act
        MigrationRunner.Run(_fx.ConnectionString);

        // Assert
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
        Assert.Equal(new[] { "fetched_at", "imdb_id", "movieid", "raw", "tmdb_id" }, metadataColumns);
    }

    [Fact]
    public async Task run_adds_the_movie_details_tables()
    {
        // Arrange
        var factory = new NpgsqlConnectionFactory(_fx.ConnectionString);

        // Act
        MigrationRunner.Run(_fx.ConnectionString);

        // Assert
        using var connection = await factory.CreateConnectionAsync();
        var detailsColumns = await ColumnsOfAsync(connection, "movie_details");
        var creditsColumns = await ColumnsOfAsync(connection, "movie_credits");
        Assert.Equal(new[]
        {
            "backdrop_path:text", "movieid:uuid", "overview:text", "poster_path:text", "runtime_minutes:integer",
            "tagline:text", "trailer_key:text", "trailer_name:text", "trailer_site:text"
        }, detailsColumns);
        Assert.Equal(new[]
        {
            "credit_type:text", "movieid:uuid", "name:text", "ordinal:integer", "profile_path:text", "role:text",
            "tmdb_person_id:bigint"
        }, creditsColumns);
    }

    [Theory]
    [InlineData("insert into movie_credits (movieid, credit_type, ordinal, tmdb_person_id, name) values (@id, 'producer', 1, 1, 'x');")]
    [InlineData("insert into movie_details (movieid, trailer_site) values (@id, 'YouTube');")]
    [InlineData("insert into movie_details (movieid, trailer_site, trailer_key) values (@id, 'Dailymotion', 'k');")]
    public async Task run_adds_constraints_that_reject_invalid_details(string sql)
    {
        // Arrange
        await _fx.ResetAsync();
        var movie = TestData.Movie.Generate();
        var factory = new NpgsqlConnectionFactory(_fx.ConnectionString);
        await new MovieRepository(factory).CreateAsync(movie);
        using var connection = await factory.CreateConnectionAsync();

        // Act
        var insert = () => connection.ExecuteAsync(sql, new { id = movie.Id });

        // Assert
        var error = await Assert.ThrowsAsync<PostgresException>(insert);
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Fact]
    public async Task deleting_a_movie_cascades_to_its_details_and_credits()
    {
        // Arrange
        await _fx.ResetAsync();
        var movie = TestData.Movie.Generate();
        var factory = new NpgsqlConnectionFactory(_fx.ConnectionString);
        var repository = new MovieRepository(factory);
        await repository.CreateAsync(movie);
        using var connection = await factory.CreateConnectionAsync();
        await connection.ExecuteAsync("insert into movie_details (movieid, overview) values (@id, 'o');", new { id = movie.Id });
        await connection.ExecuteAsync(
            "insert into movie_credits (movieid, credit_type, ordinal, tmdb_person_id, name) values (@id, 'cast', 1, 1, 'x');",
            new { id = movie.Id });

        // Act
        await repository.DeleteByIdAsync(movie.Id);

        // Assert
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("select count(*) from movie_details;"));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("select count(*) from movie_credits;"));
    }

    // "column:data_type" pairs, so the test also pins text (not varchar) on the TMDB columns.
    private static async Task<List<string>> ColumnsOfAsync(System.Data.IDbConnection connection, string table) =>
        (await connection.QueryAsync<string>("""
            select column_name || ':' || data_type from information_schema.columns
            where table_name = @table
            order by column_name
            """, new { table })).ToList();
}
