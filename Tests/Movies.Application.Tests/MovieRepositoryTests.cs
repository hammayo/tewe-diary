using Movies.Application.Database;
using Movies.Application.Database.Import;
using Movies.Application.Models;
using Movies.Application.Repositories;
using Movies.Tests.Shared;

namespace Movies.Application.Tests;

[Collection("postgres")]
public class MovieRepositoryTests
{
    private readonly PostgresFixture _fx;

    public MovieRepositoryTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task get_all_async_returns_movies_that_have_no_genres()
    {
        // Arrange
        // Regression for the get-all 500: a movie with no genres makes string_agg return
        // NULL, which the dynamic mapping must tolerate rather than calling .Split on null.
        var repository = await SeededRepositoryAsync(
            NewMovie("No Genre Movie", 2024),
            NewMovie("Has Genre", 2024, "Action"));

        // Act
        var all = (await repository.GetAllAsync(Options())).ToList(); // must not throw
        var noGenreMovie = all.Single(m => m.Title == "No Genre Movie");
        var hasGenreMovie = all.Single(m => m.Title == "Has Genre");

        // Assert
        Assert.Equal(2, all.Count);
        Assert.Empty(noGenreMovie.Genres);
        Assert.Equal(new[] { "Action" }, hasGenreMovie.Genres);
    }

    [Theory]
    [InlineData(SortOrder.Ascending, new[] { "Alpha", "Bravo", "Charlie" })]
    [InlineData(SortOrder.Descending, new[] { "Charlie", "Bravo", "Alpha" })]
    public async Task get_all_async_sorts_by_title(SortOrder order, string[] expected)
    {
        // Arrange
        var repository = await SeededRepositoryAsync(
            NewMovie("Charlie", 2001),
            NewMovie("Alpha", 2002),
            NewMovie("Bravo", 2003));

        // Act
        var titles = (await repository.GetAllAsync(Options(sortField: "title", sortOrder: order)))
            .Select(m => m.Title).ToArray();

        // Assert
        Assert.Equal(expected, titles);
    }

    [Theory]
    [InlineData(SortOrder.Ascending, new[] { 2001, 2011, 2021 })]
    [InlineData(SortOrder.Descending, new[] { 2021, 2011, 2001 })]
    public async Task get_all_async_sorts_by_year(SortOrder order, int[] expected)
    {
        // Arrange
        var repository = await SeededRepositoryAsync(
            NewMovie("First", 2021),
            NewMovie("Second", 2001),
            NewMovie("Third", 2011));

        // Act
        var years = (await repository.GetAllAsync(Options(sortField: "yearofrelease", sortOrder: order)))
            .Select(m => m.YearOfRelease).ToArray();

        // Assert
        Assert.Equal(expected, years);
    }

    [Fact]
    public async Task get_all_async_ignores_an_unrecognised_sort_field()
    {
        // Arrange
        // SortableColumns is an allow-list: a value outside it (here a SQL-injection attempt) must
        // be dropped rather than interpolated into the query.
        var repository = await SeededRepositoryAsync(
            NewMovie("Alpha", 2001),
            NewMovie("Bravo", 2002));

        // Act
        var all = (await repository.GetAllAsync(
            Options(sortField: "title; drop table movies--", sortOrder: SortOrder.Ascending))).ToList();

        // Assert
        Assert.Equal(2, all.Count); // no throw, every row returned
    }

    [Theory]
    [InlineData("Matrix", null, new[] { "Matrix Reloaded", "The Matrix" })]
    [InlineData(null, 2020, new[] { "Inception", "Matrix Reloaded" })]
    [InlineData(null, 1999, new[] { "The Matrix" })]
    public async Task get_all_async_filters_by_title_and_year(string? title, int? year, string[] expected)
    {
        // Arrange
        var repository = await FilterFixtureAsync();

        // Act
        var titles = (await repository.GetAllAsync(Options(title: title, year: year)))
            .Select(m => m.Title).OrderBy(t => t).ToArray();

        // Assert
        Assert.Equal(expected, titles);
    }

    [Theory]
    [InlineData(null, null, 3)]
    [InlineData("Matrix", null, 2)]
    [InlineData(null, 2020, 2)]
    [InlineData("Matrix", 2020, 1)]
    public async Task get_count_async_respects_title_and_year_filters(string? title, int? year, int expected)
    {
        // Arrange
        var repository = await FilterFixtureAsync();

        // Act
        var count = await repository.GetCountAsync(title, year);

        // Assert
        Assert.Equal(expected, count);
    }

    [Theory]
    [InlineData(1, new[] { 2001, 2002 })]
    [InlineData(2, new[] { 2003, 2004 })]
    [InlineData(3, new[] { 2005 })]
    public async Task get_all_async_paginates_in_sorted_order(int page, int[] expected)
    {
        // Arrange
        var repository = await SeededRepositoryAsync(
            NewMovie("A", 2001),
            NewMovie("B", 2002),
            NewMovie("C", 2003),
            NewMovie("D", 2004),
            NewMovie("E", 2005));

        // Act
        var years = (await repository.GetAllAsync(
                Options(sortField: "yearofrelease", sortOrder: SortOrder.Ascending, page: page, pageSize: 2)))
            .Select(m => m.YearOfRelease).ToArray();

        // Assert
        Assert.Equal(expected, years);
    }

    [Theory]
    [InlineData("all")]
    [InlineData("id")]
    [InlineData("slug")]
    public async Task reads_return_the_tmdb_poster_path(string read)
    {
        // Arrange
        await _fx.ResetAsync();
        var factory = new NpgsqlConnectionFactory(_fx.ConnectionString);
        await new MovieImporter(factory).ImportAsync(new[] { TmdbFixtures.OdysseyDetailsLine });
        var repository = new MovieRepository(factory);
        var imported = await repository.GetBySlugAsync(TmdbFixtures.OdysseySlug);

        // Act
        var movie = read switch
        {
            "all" => (await repository.GetAllAsync(Options())).Single(),
            "id" => await repository.GetByIdAsync(imported!.Id),
            _ => await repository.GetBySlugAsync(TmdbFixtures.OdysseySlug)
        };

        // Assert
        Assert.Equal("/poster.jpg", movie!.PosterPath);
    }

    private static Movie NewMovie(string title, int year, params string[] genres) =>
        new() { Id = Guid.NewGuid(), Title = title, YearOfRelease = year, Genres = genres.ToList() };

    private static GetAllMoviesOptions Options(
        string? title = null, int? year = null, string? sortField = null,
        SortOrder? sortOrder = null, int page = 1, int pageSize = 25) =>
        new()
        {
            Title = title, YearOfRelease = year, SortField = sortField,
            SortOrder = sortOrder, Page = page, PageSize = pageSize
        };

    // Shared seed for the title/year filter and count theories: "Matrix" matches two titles,
    // 2020 matches two years, 1999 matches one.
    private Task<MovieRepository> FilterFixtureAsync() =>
        SeededRepositoryAsync(
            NewMovie("The Matrix", 1999),
            NewMovie("Matrix Reloaded", 2020),
            NewMovie("Inception", 2020));

    private async Task<MovieRepository> SeededRepositoryAsync(params Movie[] movies)
    {
        await _fx.ResetAsync();
        var repository = new MovieRepository(new NpgsqlConnectionFactory(_fx.ConnectionString));
        foreach (var movie in movies)
        {
            await repository.CreateAsync(movie);
        }

        return repository;
    }
}
