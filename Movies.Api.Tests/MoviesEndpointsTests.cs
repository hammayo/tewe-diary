using System.Net;
using Movies.Contracts.Requests;
using Movies.Contracts.Responses;
using Movies.Tests.Shared;
using NSubstitute;
using Microsoft.AspNetCore.OutputCaching;

namespace Movies.Api.Tests;

[Collection("api")]
public class MoviesEndpointsTests
{
    private const string MoviesUrl = "/api/movies";

    private readonly ApiFixture _fx;

    public MoviesEndpointsTests(ApiFixture fx) => _fx = fx;

    // --- Create ---------------------------------------------------------------

    [Fact]
    public async Task Create_without_authentication_is_rejected()
    {
        await _fx.ResetAsync();
        var client = _fx.Factory.CreateAnonymousClient();

        var response = await client.PostAsync(MoviesUrl, TestData.CreateMovieRequest.Generate().AsJsonContent());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_as_trusted_member_creates_the_movie_and_evicts_the_cache()
    {
        await _fx.ResetAsync();
        var client = _fx.Factory.CreateAuthenticatedClient(trusted: true);
        var request = TestData.CreateMovieRequest.Generate();

        var response = await client.PostAsync(MoviesUrl, request.AsJsonContent());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.ReadJsonAsync<MovieResponse>();
        Assert.NotNull(created);
        Assert.Equal(request.Title, created!.Title);
        Assert.NotEqual(Guid.Empty, created.Id);
        await _fx.CacheStore.Received().EvictByTagAsync("movies", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("empty-title")]
    [InlineData("empty-genres")]
    [InlineData("future-year")]
    public async Task Create_with_an_invalid_request_returns_bad_request(string scenario)
    {
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

        var response = await client.PostAsync(MoviesUrl, request.AsJsonContent());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync<ValidationFailureResponse>();
        Assert.NotNull(problem);
        Assert.NotEmpty(problem!.Errors);
    }

    // --- Get by id / slug -----------------------------------------------------

    [Fact]
    public async Task Get_by_id_returns_the_movie()
    {
        await _fx.ResetAsync();
        var created = await CreateMovieAsync();
        var client = _fx.Factory.CreateAnonymousClient();

        var response = await client.GetAsync($"{MoviesUrl}/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var movie = await response.ReadJsonAsync<MovieResponse>();
        Assert.Equal(created.Id, movie!.Id);
    }

    [Fact]
    public async Task Get_by_slug_returns_the_movie()
    {
        await _fx.ResetAsync();
        var created = await CreateMovieAsync();
        var client = _fx.Factory.CreateAnonymousClient();

        var response = await client.GetAsync($"{MoviesUrl}/{created.Slug}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var movie = await response.ReadJsonAsync<MovieResponse>();
        Assert.Equal(created.Id, movie!.Id);
    }

    [Theory]
    [InlineData("7f9c3d1e-0000-0000-0000-000000000000")] // unknown id
    [InlineData("this-movie-does-not-exist-2020")]        // unknown slug
    public async Task Get_with_an_unknown_identifier_returns_not_found(string idOrSlug)
    {
        await _fx.ResetAsync();
        var client = _fx.Factory.CreateAnonymousClient();

        var response = await client.GetAsync($"{MoviesUrl}/{idOrSlug}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- Get all --------------------------------------------------------------

    [Fact]
    public async Task GetAll_returns_all_created_movies()
    {
        await _fx.ResetAsync();
        await CreateMovieAsync();
        await CreateMovieAsync();
        await CreateMovieAsync();
        var client = _fx.Factory.CreateAnonymousClient();

        var response = await client.GetAsync($"{MoviesUrl}?page=1&pageSize=25");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var movies = await response.ReadJsonAsync<MoviesResponse>();
        Assert.Equal(3, movies!.Total);
        Assert.Equal(3, movies.Items.Count());
    }

    [Fact]
    public async Task GetAll_defaults_to_newest_first()
    {
        await _fx.ResetAsync();
        await CreateMovieAsync(YearOf(2001));
        await CreateMovieAsync(YearOf(2021));
        await CreateMovieAsync(YearOf(2011));
        var client = _fx.Factory.CreateAnonymousClient();

        var response = await client.GetAsync($"{MoviesUrl}?page=1&pageSize=25");

        var movies = await response.ReadJsonAsync<MoviesResponse>();
        var years = movies!.Items.Select(m => m.YearOfRelease).ToList();
        Assert.Equal(new[] { 2021, 2011, 2001 }, years);
    }

    // --- Update ---------------------------------------------------------------

    [Fact]
    public async Task Update_without_authentication_is_rejected()
    {
        await _fx.ResetAsync();
        var created = await CreateMovieAsync();
        var client = _fx.Factory.CreateAnonymousClient();

        var response = await client.PutAsync($"{MoviesUrl}/{created.Id}",
            TestData.UpdateMovieRequest.Generate().AsJsonContent());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Update_as_trusted_member_updates_the_movie_and_evicts_the_cache()
    {
        await _fx.ResetAsync();
        var created = await CreateMovieAsync();
        var client = _fx.Factory.CreateAuthenticatedClient(trusted: true);
        var request = TestData.UpdateMovieRequest.Generate();

        var response = await client.PutAsync($"{MoviesUrl}/{created.Id}", request.AsJsonContent());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.ReadJsonAsync<MovieResponse>();
        Assert.Equal(request.Title, updated!.Title);
        await _fx.CacheStore.Received().EvictByTagAsync("movies", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_of_a_missing_movie_returns_not_found()
    {
        await _fx.ResetAsync();
        var client = _fx.Factory.CreateAuthenticatedClient(trusted: true);

        var response = await client.PutAsync($"{MoviesUrl}/{Guid.NewGuid()}",
            TestData.UpdateMovieRequest.Generate().AsJsonContent());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- Delete ---------------------------------------------------------------

    [Fact]
    public async Task Delete_without_authentication_is_rejected()
    {
        await _fx.ResetAsync();
        var created = await CreateMovieAsync();
        var client = _fx.Factory.CreateAnonymousClient();

        var response = await client.DeleteAsync($"{MoviesUrl}/{created.Id}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_as_a_non_admin_is_forbidden()
    {
        await _fx.ResetAsync();
        var created = await CreateMovieAsync();
        var client = _fx.Factory.CreateAuthenticatedClient(trusted: true); // authenticated but not admin

        var response = await client.DeleteAsync($"{MoviesUrl}/{created.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Delete_as_admin_deletes_the_movie_and_evicts_the_cache()
    {
        await _fx.ResetAsync();
        var created = await CreateMovieAsync();
        var client = _fx.Factory.CreateAuthenticatedClient(admin: true);

        var response = await client.DeleteAsync($"{MoviesUrl}/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _fx.CacheStore.Received().EvictByTagAsync("movies", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_of_a_missing_movie_as_admin_returns_not_found()
    {
        await _fx.ResetAsync();
        var client = _fx.Factory.CreateAuthenticatedClient(admin: true);

        var response = await client.DeleteAsync($"{MoviesUrl}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- helpers --------------------------------------------------------------

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
