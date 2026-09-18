using System.Net;
using Movies.Contracts.Requests;
using Movies.Contracts.Responses;
using Movies.Tests.Shared;
using NSubstitute;

namespace Movies.Api.Tests;

[Collection("api")]
public class MoviesEndpointsTests
{
    private const string MoviesUrl = "/api/v1/movies";

    private readonly ApiFixture _fx;

    public MoviesEndpointsTests(ApiFixture fx) => _fx = fx;

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
        Assert.NotNull(created);
        Assert.Equal(request.Title, created!.Title);
        Assert.NotEqual(Guid.Empty, created.Id);
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
        Assert.NotNull(problem);
        Assert.NotEmpty(problem!.Errors);
    }

    [Fact]
    public async Task create_with_a_duplicate_title_and_year_returns_bad_request()
    {
        // Arrange
        // Slug is derived from title + year, so the same pair collides. The uniqueness rule in
        // MovieValidator must reject the second create rather than let the insert fail.
        await _fx.ResetAsync();
        var client = _fx.Factory.CreateAuthenticatedClient(trusted: true);
        var request = TestData.CreateMovieRequest.Generate();
        var first = await client.PostAsync(MoviesUrl, request.AsJsonContent());
        first.EnsureSuccessStatusCode();

        // Act
        var response = await client.PostAsync(MoviesUrl, request.AsJsonContent());
        var problem = await response.ReadJsonAsync<ValidationFailureResponse>();
        var errorMessages = string.Join(" ", problem!.Errors.Select(e => e.Message));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("already exists", errorMessages);
    }

    [Fact]
    public async Task get_by_id_returns_the_movie()
    {
        // Arrange
        await _fx.ResetAsync();
        var created = await CreateMovieAsync();
        var client = _fx.Factory.CreateAnonymousClient();

        // Act
        var response = await client.GetAsync($"{MoviesUrl}/{created.Id}");
        var movie = await response.ReadJsonAsync<MovieResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(created.Id, movie!.Id);
    }

    [Fact]
    public async Task get_by_slug_returns_the_movie()
    {
        // Arrange
        await _fx.ResetAsync();
        var created = await CreateMovieAsync();
        var client = _fx.Factory.CreateAnonymousClient();

        // Act
        var response = await client.GetAsync($"{MoviesUrl}/{created.Slug}");
        var movie = await response.ReadJsonAsync<MovieResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(created.Id, movie!.Id);
    }

    [Theory]
    [InlineData("7f9c3d1e-0000-0000-0000-000000000000")] // unknown id
    [InlineData("this-movie-does-not-exist-2020")]        // unknown slug
    public async Task get_with_an_unknown_identifier_returns_not_found(string idOrSlug)
    {
        // Arrange
        await _fx.ResetAsync();
        var client = _fx.Factory.CreateAnonymousClient();

        // Act
        var response = await client.GetAsync($"{MoviesUrl}/{idOrSlug}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

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
        var items = movies!.Items.ToList();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, movies.Total);
        Assert.Equal(3, items.Count);
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
        var response = await client.GetAsync($"{MoviesUrl}?page=1&pageSize=25");
        var movies = await response.ReadJsonAsync<MoviesResponse>();
        var years = movies!.Items.Select(m => m.YearOfRelease).ToList();

        // Assert
        Assert.Equal(new[] { 2021, 2011, 2001 }, years);
    }

    [Fact]
    public async Task update_without_authentication_is_rejected()
    {
        // Arrange
        await _fx.ResetAsync();
        var created = await CreateMovieAsync();
        var client = _fx.Factory.CreateAnonymousClient();

        // Act
        var response = await client.PutAsync($"{MoviesUrl}/{created.Id}",
            TestData.UpdateMovieRequest.Generate().AsJsonContent());

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
        await _fx.CacheStore.Received().EvictByTagAsync("movies", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task update_of_a_missing_movie_returns_not_found()
    {
        // Arrange
        await _fx.ResetAsync();
        var client = _fx.Factory.CreateAuthenticatedClient(trusted: true);

        // Act
        var response = await client.PutAsync($"{MoviesUrl}/{Guid.NewGuid()}",
            TestData.UpdateMovieRequest.Generate().AsJsonContent());

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
        var client = _fx.Factory.CreateAuthenticatedClient(trusted: true); // authenticated but not admin

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

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _fx.CacheStore.Received().EvictByTagAsync("movies", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task delete_of_a_missing_movie_as_admin_returns_not_found()
    {
        // Arrange
        await _fx.ResetAsync();
        var client = _fx.Factory.CreateAuthenticatedClient(admin: true);

        // Act
        var response = await client.DeleteAsync($"{MoviesUrl}/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
    
    private static CreateMovieRequest YearOf(int year)
    {
        var request = TestData.CreateMovieRequest.Generate();
        return new CreateMovieRequest { Title = request.Title, YearOfRelease = year, Genres = request.Genres };
    }

    private async Task<MovieResponse> CreateMovieAsync(CreateMovieRequest? request = null)
    {
        var client = _fx.Factory.CreateAuthenticatedClient(trusted: true);
        var response = await client.PostAsync(MoviesUrl, (request ?? TestData.CreateMovieRequest.Generate()).AsJsonContent());
        response.EnsureSuccessStatusCode();
        return (await response.ReadJsonAsync<MovieResponse>())!;
    }
}
