using System.Net;
using System.Text.Json;
using Movies.Contracts.Responses;
using Movies.Tests.Shared;

namespace Movies.Api.Tests;

[Collection("api")]
public class MoviesV2EndpointsTests
{
    private const string V1Url = "/api/v1/movies";
    private const string V2Url = "/api/v2/movies";

    private readonly ApiFixture _fx;

    public MoviesV2EndpointsTests(ApiFixture fx) => _fx = fx;

    [Fact]
    public async Task get_v2_by_id_returns_the_same_movie_as_v1()
    {
        // Arrange
        await _fx.ResetAsync();
        var created = await CreateMovieAsync();
        var client = _fx.Factory.CreateAnonymousClient();

        // Act
        var v1 = await (await client.GetAsync($"{V1Url}/{created.Id}")).ReadJsonAsync<MovieResponse>();
        var response = await client.GetAsync($"{V2Url}/{created.Id}");
        var v2 = await response.ReadJsonAsync<MovieResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(v1!.Id, v2!.Id);
        Assert.Equal(v1.Title, v2.Title);
    }

    [Fact]
    public async Task get_v2_by_slug_returns_the_movie()
    {
        // Arrange
        await _fx.ResetAsync();
        var created = await CreateMovieAsync();
        var client = _fx.Factory.CreateAnonymousClient();

        // Act
        var response = await client.GetAsync($"{V2Url}/{created.Slug}");
        var movie = await response.ReadJsonAsync<MovieResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(created.Id, movie!.Id);
    }

    [Theory]
    [InlineData("7f9c3d1e-0000-0000-0000-000000000000")] // unknown id
    [InlineData("this-movie-does-not-exist-2020")]        // unknown slug
    public async Task get_v2_with_an_unknown_identifier_returns_not_found(string idOrSlug)
    {
        // Arrange
        await _fx.ResetAsync();
        var client = _fx.Factory.CreateAnonymousClient();

        // Act
        var response = await client.GetAsync($"{V2Url}/{idOrSlug}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task v2_is_listed_in_its_own_swagger_document()
    {
        // Arrange
        var client = _fx.Factory.CreateAnonymousClient();

        // Act
        var response = await client.GetAsync("/swagger/2.0/swagger.json");
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("2.0", document.GetProperty("info").GetProperty("version").GetString());
        Assert.True(document.GetProperty("paths").TryGetProperty("/api/v{version}/movies/{idOrSlug}", out _));
    }

    private async Task<MovieResponse> CreateMovieAsync()
    {
        var client = _fx.Factory.CreateAuthenticatedClient(trusted: true);
        var response = await client.PostAsync(V1Url, TestData.CreateMovieRequest.Generate().AsJsonContent());
        response.EnsureSuccessStatusCode();
        return (await response.ReadJsonAsync<MovieResponse>())!;
    }
}
