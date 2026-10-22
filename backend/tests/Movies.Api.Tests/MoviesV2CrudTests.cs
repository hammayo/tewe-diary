using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Movies.Application.Database.Import;
using Movies.Contracts.Requests;
using Movies.Contracts.Responses;
using Movies.Tests.Shared;
using NSubstitute;

namespace Movies.Api.Tests;

// v2 twins of the v1 movie tests (MoviesEndpointsTests).
[Collection("api")]
public class MoviesV2CrudTests
{
    private const string MoviesUrl = "/api/v2/movies";

    private readonly ApiFixture _fx;

    public MoviesV2CrudTests(ApiFixture fx) => _fx = fx;

    [Fact]
    public async Task get_all_returns_all_created_movies()
    {
        // Arrange
        await _fx.ResetAsync();
        await CreateMovieAsync();
        await CreateMovieAsync();
        await CreateMovieAsync();
        var client = _fx.Factory.CreateAnonymousClient();

        // Act
        var response = await client.GetAsync($"{MoviesUrl}?page=1&pageSize=25");
        var movies = await response.ReadJsonAsync<MoviesResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, movies!.Total);
        Assert.Equal(3, movies.Items.Count());
    }

    [Fact]
    public async Task get_all_defaults_to_newest_first()
    {
        // Arrange
        await _fx.ResetAsync();
        await CreateMovieAsync(YearOf(2001));
        await CreateMovieAsync(YearOf(2021));
        await CreateMovieAsync(YearOf(2011));
        var client = _fx.Factory.CreateAnonymousClient();

        // Act
        var movies = await (await client.GetAsync($"{MoviesUrl}?page=1&pageSize=25")).ReadJsonAsync<MoviesResponse>();
        var years = movies!.Items.Select(m => m.YearOfRelease).ToList();

        // Assert
        Assert.Equal(new[] { 2021, 2011, 2001 }, years);
    }

    [Fact]
    public async Task get_all_filters_by_year_and_pages()
    {
        // Arrange
        await _fx.ResetAsync();
        await CreateMovieAsync(YearOf(2001));
        await CreateMovieAsync(YearOf(2001));
        await CreateMovieAsync(YearOf(2021));
        var client = _fx.Factory.CreateAnonymousClient();

        // Act
        var movies = await (await client.GetAsync($"{MoviesUrl}?year=2001&page=2&pageSize=1")).ReadJsonAsync<MoviesResponse>();

        // Assert
        Assert.Equal(2, movies!.Total);
        Assert.Equal(2, movies.Page);
        Assert.Equal(1, movies.PageSize);
        Assert.Single(movies.Items);
    }

    [Fact]
    public async Task get_all_returns_poster_urls_without_details()
    {
        // Arrange
        await _fx.ResetAsync();
        await _fx.Factory.Services.GetRequiredService<MovieImporter>()
            .ImportAsync(new[] { TmdbFixtures.OdysseyDetailsLine });
        var client = _fx.Factory.CreateAnonymousClient();

        // Act
        var movies = await (await client.GetAsync(MoviesUrl)).ReadJsonAsync<MoviesResponse>();
        var item = movies!.Items.Single();

        // Assert
        Assert.Equal("https://image.tmdb.org/t/p/w500/poster.jpg", item.PosterUrl);
        Assert.Null(item.Details);
    }

    [Fact]
    public async Task list_items_carry_a_self_link_that_resolves_to_the_v2_movie()
    {
        // Arrange
        await _fx.ResetAsync();
        var created = await CreateMovieAsync();
        var client = _fx.Factory.CreateAnonymousClient();

        // Act
        var movies = await (await client.GetAsync(MoviesUrl)).ReadJsonAsync<MoviesResponse>();
        var self = movies!.Items.Single().Links!.Single();
        var followed = await client.GetAsync(self.Href);
        var sameMovie = await followed.ReadJsonAsync<MovieResponse>();

        // Assert
        Assert.Equal("self", self.Rel);
        Assert.Equal("GET", self.Type);
        Assert.Equal($"http://localhost/api/v2/movies/{created.Slug}", self.Href);
        Assert.Equal(HttpStatusCode.OK, followed.StatusCode);
        Assert.Equal(created.Id, sameMovie!.Id);
    }

    [Fact]
    public async Task single_movie_response_has_no_links()
    {
        // Arrange
        await _fx.ResetAsync();
        var created = await CreateMovieAsync();
        var client = _fx.Factory.CreateAnonymousClient();

        // Act
        var movie = await (await client.GetAsync($"{MoviesUrl}/{created.Id}")).ReadJsonAsync<MovieResponse>();

        // Assert
        Assert.Null(movie!.Links);
    }

    [Fact]
    public async Task get_of_a_tmdb_movie_returns_poster_url_and_details()
    {
        // Arrange
        await _fx.ResetAsync();
        await _fx.Factory.Services.GetRequiredService<MovieImporter>()
            .ImportAsync(new[] { TmdbFixtures.OdysseyDetailsLine });
        var client = _fx.Factory.CreateAnonymousClient();

        // Act
        var movie = await (await client.GetAsync($"{MoviesUrl}/{TmdbFixtures.OdysseySlug}")).ReadJsonAsync<MovieResponse>();

        // Assert
        Assert.Equal("https://image.tmdb.org/t/p/w500/poster.jpg", movie!.PosterUrl);
        Assert.Equal(1368337, movie.Details!.TmdbId);
        Assert.Equal("https://www.youtube.com/embed/Mzw2ttJD2qQ", movie.Details.Trailer!.EmbedUrl);
    }

    [Fact]
    public async Task get_of_a_manual_movie_returns_no_poster_and_no_details()
    {
        // Arrange
        await _fx.ResetAsync();
        var created = await CreateMovieAsync();
        var client = _fx.Factory.CreateAnonymousClient();

        // Act
        var movie = await (await client.GetAsync($"{MoviesUrl}/{created.Id}")).ReadJsonAsync<MovieResponse>();

        // Assert
        Assert.Null(movie!.PosterUrl);
        Assert.Null(movie.Details);
    }

    [Fact]
    public async Task create_without_authentication_is_rejected()
    {
        // Arrange
        await _fx.ResetAsync();
        var client = _fx.Factory.CreateAnonymousClient();

        // Act
        var response = await client.PostAsync(MoviesUrl, TestData.CreateMovieRequest.Generate().AsJsonContent());

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task create_as_trusted_member_creates_the_movie_and_evicts_the_cache()
    {
        // Arrange
        await _fx.ResetAsync();
        var client = _fx.Factory.CreateAuthenticatedClient(trusted: true);
        var request = TestData.CreateMovieRequest.Generate();

        // Act
        var response = await client.PostAsync(MoviesUrl, request.AsJsonContent());
        var created = await response.ReadJsonAsync<MovieResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(request.Title, created!.Title);
        Assert.Equal($"http://localhost/api/v2/movies/{created.Id}", response.Headers.Location!.ToString());
        Assert.Equal($"http://localhost/api/v2/movies/{created.Slug}", created.Links!.Single().Href);
        await _fx.CacheStore.Received().EvictByTagAsync("movies", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("empty-title")]
    [InlineData("empty-genres")]
    [InlineData("future-year")]
    public async Task create_with_an_invalid_request_returns_bad_request(string scenario)
    {
        // Arrange
        await _fx.ResetAsync();
        var valid = TestData.CreateMovieRequest.Generate();
        CreateMovieRequest request = scenario switch
        {
            "empty-title" => new CreateMovieRequest { Title = "", YearOfRelease = valid.YearOfRelease, Genres = valid.Genres },
            "empty-genres" => new CreateMovieRequest { Title = valid.Title, YearOfRelease = valid.YearOfRelease, Genres = Array.Empty<string>() },
            "future-year" => new CreateMovieRequest { Title = valid.Title, YearOfRelease = DateTime.UtcNow.Year + 1, Genres = valid.Genres },
            _ => valid
        };
        var client = _fx.Factory.CreateAuthenticatedClient(trusted: true);

        // Act
        var response = await client.PostAsync(MoviesUrl, request.AsJsonContent());
        var problem = await response.ReadJsonAsync<ValidationFailureResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty(problem!.Errors);
    }

    [Fact]
    public async Task create_with_a_duplicate_title_and_year_returns_bad_request()
    {
        // Arrange
        await _fx.ResetAsync();
        var client = _fx.Factory.CreateAuthenticatedClient(trusted: true);
        var request = TestData.CreateMovieRequest.Generate();
        (await client.PostAsync(MoviesUrl, request.AsJsonContent())).EnsureSuccessStatusCode();

        // Act
        var response = await client.PostAsync(MoviesUrl, request.AsJsonContent());
        var problem = await response.ReadJsonAsync<ValidationFailureResponse>();
        var errorMessages = string.Join(" ", problem!.Errors.Select(e => e.Message));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("already exists", errorMessages);
    }

    [Fact]
    public async Task update_without_authentication_is_rejected()
    {
        // Arrange
        await _fx.ResetAsync();
        var created = await CreateMovieAsync();
        var client = _fx.Factory.CreateAnonymousClient();

        // Act
        var response = await client.PutAsync($"{MoviesUrl}/{created.Id}", TestData.UpdateMovieRequest.Generate().AsJsonContent());

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task update_as_trusted_member_updates_the_movie_and_evicts_the_cache()
    {
        // Arrange
        await _fx.ResetAsync();
        var created = await CreateMovieAsync();
        var client = _fx.Factory.CreateAuthenticatedClient(trusted: true);
        var request = TestData.UpdateMovieRequest.Generate();

        // Act
        var response = await client.PutAsync($"{MoviesUrl}/{created.Id}", request.AsJsonContent());
        var updated = await response.ReadJsonAsync<MovieResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(request.Title, updated!.Title);
        Assert.NotNull(updated.Links);
        await _fx.CacheStore.Received().EvictByTagAsync("movies", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task update_of_a_missing_movie_returns_not_found_and_does_not_evict()
    {
        // Arrange
        await _fx.ResetAsync();
        var client = _fx.Factory.CreateAuthenticatedClient(trusted: true);

        // Act
        var response = await client.PutAsync($"{MoviesUrl}/{Guid.NewGuid()}", TestData.UpdateMovieRequest.Generate().AsJsonContent());

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await _fx.CacheStore.DidNotReceive().EvictByTagAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task delete_without_authentication_is_rejected()
    {
        // Arrange
        await _fx.ResetAsync();
        var created = await CreateMovieAsync();
        var client = _fx.Factory.CreateAnonymousClient();

        // Act
        var response = await client.DeleteAsync($"{MoviesUrl}/{created.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task delete_as_a_non_admin_is_forbidden()
    {
        // Arrange
        await _fx.ResetAsync();
        var created = await CreateMovieAsync();
        var client = _fx.Factory.CreateAuthenticatedClient(trusted: true);

        // Act
        var response = await client.DeleteAsync($"{MoviesUrl}/{created.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task delete_as_admin_deletes_the_movie_and_evicts_the_cache()
    {
        // Arrange
        await _fx.ResetAsync();
        var created = await CreateMovieAsync();
        var client = _fx.Factory.CreateAuthenticatedClient(admin: true);

        // Act
        var response = await client.DeleteAsync($"{MoviesUrl}/{created.Id}");
        var afterwards = await client.GetAsync($"{MoviesUrl}/{created.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, afterwards.StatusCode);
        await _fx.CacheStore.Received().EvictByTagAsync("movies", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task delete_of_a_missing_movie_as_admin_returns_not_found_and_does_not_evict()
    {
        // Arrange
        await _fx.ResetAsync();
        var client = _fx.Factory.CreateAuthenticatedClient(admin: true);

        // Act
        var response = await client.DeleteAsync($"{MoviesUrl}/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await _fx.CacheStore.DidNotReceive().EvictByTagAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static CreateMovieRequest YearOf(int year)
    {
        var request = TestData.CreateMovieRequest.Generate();
        return new CreateMovieRequest { Title = request.Title, YearOfRelease = year, Genres = request.Genres };
    }

    private async Task<MovieResponse> CreateMovieAsync(CreateMovieRequest? request = null)
    {
        var client = _fx.Factory.CreateAuthenticatedClient(trusted: true);
        var response = await client.PostAsync(MoviesUrl,
            (request ?? TestData.CreateMovieRequest.Generate()).AsJsonContent());
        response.EnsureSuccessStatusCode();
        return (await response.ReadJsonAsync<MovieResponse>())!;
    }
}
