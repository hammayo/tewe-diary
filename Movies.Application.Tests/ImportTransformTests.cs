using Dapper;
using Movies.Application.Database;
using Movies.Application.Database.Import;
using Movies.Application.Database.Migrations;
using Movies.Tests.Shared;

namespace Movies.Application.Tests;

[Collection("postgres")]
public class ImportTransformTests
{
    private readonly PostgresFixture _fx;

    public ImportTransformTests(PostgresFixture fx) => _fx = fx;

    private static string[] FixtureLines() =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "import-sample.ndjson"));

    private async Task<(NpgsqlConnectionFactory factory, MovieImporter importer)> FreshDbAsync()
    {
        var factory = new NpgsqlConnectionFactory(_fx.ConnectionString);
        MigrationRunner.Run(_fx.ConnectionString);
        using var connection = await factory.CreateConnectionAsync();
        await connection.ExecuteAsync("truncate movie_metadata, genres, ratings, movies cascade;");
        return (factory, new MovieImporter(factory));
    }

    [Fact]
    public async Task Import_is_idempotent_on_tmdb_id()
    {
        var (factory, importer) = await FreshDbAsync();
        using var connection = await factory.CreateConnectionAsync();

        var lines = FixtureLines();
        await importer.ImportAsync(lines);
        await importer.ImportAsync(lines); // second run refreshes in place, adds nothing

        Assert.Equal(2, await connection.ExecuteScalarAsync<int>("select count(*) from movies;"));
        Assert.Equal(4, await connection.ExecuteScalarAsync<int>("select count(*) from genres;"));
        Assert.Equal(2, await connection.ExecuteScalarAsync<int>("select count(*) from movie_metadata;"));
    }

    [Fact]
    public async Task Duplicate_tmdb_id_in_batch_inserts_one_row_and_does_not_throw()
    {
        var (factory, importer) = await FreshDbAsync();
        using var connection = await factory.CreateConnectionAsync();

        // Two lines with the same tmdb_id — simulates TMDB paged list overlap.
        var lines = new[]
        {
            """{"tmdb_id":99901,"Title":"Dup Movie A","YearOfRelease":2020,"Genres":["Action"],"raw":{"id":99901}}""",
            """{"tmdb_id":99901,"Title":"Dup Movie A","YearOfRelease":2020,"Genres":["Action"],"raw":{"id":99901}}"""
        };

        await importer.ImportAsync(lines); // must not throw

        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("select count(*) from movies;"));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("select count(*) from movie_metadata;"));
    }

    [Fact]
    public async Task Slug_collision_distinct_tmdb_ids_inserts_one_row_and_does_not_throw()
    {
        var (factory, importer) = await FreshDbAsync();
        using var connection = await factory.CreateConnectionAsync();

        // Two different tmdb_ids but same Title + YearOfRelease → same slug.
        var lines = new[]
        {
            """{"tmdb_id":99902,"Title":"Same Slug Film","YearOfRelease":2019,"Genres":["Drama"],"raw":{"id":99902}}""",
            """{"tmdb_id":99903,"Title":"Same Slug Film","YearOfRelease":2019,"Genres":["Drama"],"raw":{"id":99903}}"""
        };

        await importer.ImportAsync(lines); // must not throw

        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("select count(*) from movies;"));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("select count(*) from movie_metadata;"));
    }

    [Fact]
    public async Task Reimport_refreshes_existing_movie_in_place_and_preserves_ratings()
    {
        var (factory, importer) = await FreshDbAsync();
        using var connection = await factory.CreateConnectionAsync();

        await importer.ImportAsync(new[]
        {
            """{"tmdb_id":862,"Title":"Toy Story","YearOfRelease":1995,"Genres":["Animation","Comedy"],"raw":{"id":862,"v":1}}"""
        });
        var id = await connection.ExecuteScalarAsync<Guid>("select id from movies where slug = 'toy-story-1995';");

        // A real user rates the movie — this must survive a refresh.
        var userId = Guid.NewGuid();
        await connection.ExecuteAsync(
            "insert into ratings (userid, movieid, rating) values (@userId, @id, 5);", new { userId, id });

        // Re-import the same tmdb_id with changed title/year/genres/raw.
        await importer.ImportAsync(new[]
        {
            """{"tmdb_id":862,"Title":"Toy Story Remastered","YearOfRelease":1996,"Genres":["Animation"],"raw":{"id":862,"v":2}}"""
        });

        // Same identity, refreshed fields.
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("select count(*) from movies;"));
        Assert.Equal(id, await connection.ExecuteScalarAsync<Guid>("select id from movies where slug = 'toy-story-remastered-1996';"));
        Assert.Equal("Toy Story Remastered", await connection.ExecuteScalarAsync<string>("select title from movies where id = @id;", new { id }));
        Assert.Equal(1996, await connection.ExecuteScalarAsync<int>("select yearofrelease from movies where id = @id;", new { id }));
        Assert.Equal(new[] { "Animation" }, (await connection.QueryAsync<string>("select name from genres where movieid = @id;", new { id })).ToArray());
        Assert.Equal("2", await connection.ExecuteScalarAsync<string>("select raw->>'v' from movie_metadata where movieid = @id;", new { id }));

        // Rating preserved.
        Assert.Equal(5, await connection.ExecuteScalarAsync<int>(
            "select rating from ratings where movieid = @id and userid = @userId;", new { id, userId }));
    }

    [Fact]
    public async Task Import_leaves_manual_movies_untouched()
    {
        var (factory, importer) = await FreshDbAsync();
        using var connection = await factory.CreateConnectionAsync();

        // A manually/API-created movie has no movie_metadata row.
        var manualId = Guid.NewGuid();
        await connection.ExecuteAsync(
            "insert into movies (id, slug, title, yearofrelease) values (@manualId, 'manual-classic-2000', 'Manual Classic', 2000);",
            new { manualId });
        await connection.ExecuteAsync("insert into genres (movieid, name) values (@manualId, 'Cult');", new { manualId });
        var userId = Guid.NewGuid();
        await connection.ExecuteAsync(
            "insert into ratings (userid, movieid, rating) values (@userId, @manualId, 4);", new { userId, manualId });

        await importer.ImportAsync(new[]
        {
            """{"tmdb_id":862,"Title":"Toy Story","YearOfRelease":1995,"Genres":["Animation"],"raw":{"id":862}}"""
        });

        // Manual movie completely unchanged, no metadata created for it.
        Assert.Equal(2, await connection.ExecuteScalarAsync<int>("select count(*) from movies;"));
        Assert.Equal("Manual Classic", await connection.ExecuteScalarAsync<string>("select title from movies where id = @manualId;", new { manualId }));
        Assert.Equal("Cult", await connection.ExecuteScalarAsync<string>("select name from genres where movieid = @manualId;", new { manualId }));
        Assert.Equal(4, await connection.ExecuteScalarAsync<int>("select rating from ratings where movieid = @manualId;", new { manualId }));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("select count(*) from movie_metadata where movieid = @manualId;", new { manualId }));
    }

    [Fact]
    public async Task Import_skips_tmdb_movie_that_collides_with_a_manual_slug()
    {
        var (factory, importer) = await FreshDbAsync();
        using var connection = await factory.CreateConnectionAsync();

        // Manual movie already owns the slug that TMDB "Toy Story" (1995) would generate.
        var manualId = Guid.NewGuid();
        await connection.ExecuteAsync(
            "insert into movies (id, slug, title, yearofrelease) values (@manualId, 'toy-story-1995', 'My Own Toy Story', 1995);",
            new { manualId });

        await importer.ImportAsync(new[]
        {
            """{"tmdb_id":862,"Title":"Toy Story","YearOfRelease":1995,"Genres":["Animation"],"raw":{"id":862}}"""
        });

        // TMDB movie skipped, manual movie preserved.
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("select count(*) from movies;"));
        Assert.Equal("My Own Toy Story", await connection.ExecuteScalarAsync<string>("select title from movies where id = @manualId;", new { manualId }));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("select count(*) from movie_metadata;"));
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
