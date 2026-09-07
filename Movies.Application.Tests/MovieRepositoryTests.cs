using Dapper;
using Movies.Application.Database;
using Movies.Application.Models;
using Movies.Application.Repositories;
using Xunit;

namespace Movies.Application.Tests;

[Collection("postgres")]
public class MovieRepositoryTests
{
    private readonly PostgresFixture _fx;

    public MovieRepositoryTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task GetAllAsync_returns_movies_that_have_no_genres()
    {
        // Regression for the get-all 500: a movie with no genres makes string_agg return
        // NULL, which the dynamic mapping must tolerate rather than calling .Split on null.
        var factory = new NpgsqlConnectionFactory(_fx.ConnectionString);
        await new DbInitializer(factory).InitializeAsync();
        using (var connection = await factory.CreateConnectionAsync())
        {
            await connection.ExecuteAsync("truncate movie_metadata, genres, ratings, movies cascade;");
        }

        var repository = new MovieRepository(factory);
        await repository.CreateAsync(new Movie
        {
            Id = Guid.NewGuid(), Title = "No Genre Movie", YearOfRelease = 2024, Genres = [],
        });
        await repository.CreateAsync(new Movie
        {
            Id = Guid.NewGuid(), Title = "Has Genre", YearOfRelease = 2024, Genres = ["Action"],
        });

        var all = (await repository.GetAllAsync()).ToList(); // must not throw

        Assert.Equal(2, all.Count);
        Assert.Empty(all.Single(m => m.Title == "No Genre Movie").Genres);
        Assert.Equal(new[] { "Action" }, all.Single(m => m.Title == "Has Genre").Genres);
    }
}
