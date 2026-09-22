using Dapper;
using Movies.Application.Database;
using Movies.Application.Database.Import;
using Movies.Application.Models;
using Movies.Application.Repositories;
using Movies.Tests.Shared;

namespace Movies.Application.Tests;

[Collection("postgres")]
public class MovieDetailsRepositoryTests
{
    private readonly PostgresFixture _fx;

    public MovieDetailsRepositoryTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task get_by_movie_id_returns_details_with_credits_split_and_ordered()
    {
        // Arrange
        await _fx.ResetAsync();
        var factory = new NpgsqlConnectionFactory(_fx.ConnectionString);
        await new MovieImporter(factory).ImportAsync(new[] { TmdbFixtures.OdysseyDetailsLine });
        using var connection = await factory.CreateConnectionAsync();
        var movieId = await connection.ExecuteScalarAsync<Guid>("select id from movies where slug = @slug;", new { slug = TmdbFixtures.OdysseySlug });
        var repository = new MovieDetailsRepository(factory);

        // Act
        var details = await repository.GetByMovieIdAsync(movieId);

        // Assert
        Assert.NotNull(details);
        Assert.Equal(1368337, details!.TmdbId);
        Assert.Equal("tt33764258", details.ImdbId);
        Assert.Equal("Odysseus sails home.", details.Overview);
        Assert.Equal("Defy the gods.", details.Tagline);
        Assert.Equal(173, details.RuntimeMinutes);
        Assert.Equal("/backdrop.jpg", details.BackdropPath);
        Assert.Equal(new MovieTrailer("YouTube", "Mzw2ttJD2qQ", "Official Trailer"), details.Trailer);
        Assert.Equal(new[] { new MovieCredit(525, "Christopher Nolan", null, "/nolan.jpg") }, details.Directors);
        Assert.Equal(new[] { new MovieCredit(525, "Christopher Nolan", "Writer", "/nolan.jpg") }, details.Writers);
        Assert.Equal(new[]
        {
            new MovieCredit(1892, "Matt Damon", "Odysseus", "/damon.jpg"),
            new MovieCredit(1136406, "Tom Holland", "Telemachus", null)
        }, details.Cast);
    }

    [Fact]
    public async Task get_by_movie_id_returns_null_for_a_manual_movie()
    {
        // Arrange
        await _fx.ResetAsync();
        var factory = new NpgsqlConnectionFactory(_fx.ConnectionString);
        var movie = TestData.Movie.Generate();
        await new MovieRepository(factory).CreateAsync(movie);
        var repository = new MovieDetailsRepository(factory);

        // Act
        var details = await repository.GetByMovieIdAsync(movie.Id);

        // Assert
        Assert.Null(details);
    }
}
