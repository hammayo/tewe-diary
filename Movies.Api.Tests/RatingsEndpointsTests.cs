using System.Net;
using Movies.Contracts.Requests;
using Movies.Contracts.Responses;
using Movies.Tests.Shared;

namespace Movies.Api.Tests;

[Collection("api")]
public class RatingsEndpointsTests
{
    private const string MoviesUrl = "/api/movies";
    private const string UserRatingsUrl = "/api/ratings/me";

    private readonly ApiFixture _fx;

    public RatingsEndpointsTests(ApiFixture fx) => _fx = fx;

    // --- Rate a movie ---------------------------------------------------------

    [Fact]
    public async Task Rate_without_authentication_is_rejected()
    {
        await _fx.ResetAsync();
        var movie = await CreateMovieAsync();
        var client = _fx.Factory.CreateAnonymousClient();

        var response = await client.PutAsync($"{MoviesUrl}/{movie.Id}/ratings",
            new RateMovieRequest { Rating = 4 }.AsJsonContent());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task Rate_with_a_valid_score_succeeds(int rating)
    {
        await _fx.ResetAsync();
        var movie = await CreateMovieAsync();
        var client = _fx.Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.PutAsync($"{MoviesUrl}/{movie.Id}/ratings",
            new RateMovieRequest { Rating = rating }.AsJsonContent());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public async Task Rate_with_an_out_of_range_score_returns_bad_request(int rating)
    {
        await _fx.ResetAsync();
        var movie = await CreateMovieAsync();
        var client = _fx.Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.PutAsync($"{MoviesUrl}/{movie.Id}/ratings",
            new RateMovieRequest { Rating = rating }.AsJsonContent());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Rate_a_missing_movie_returns_not_found()
    {
        await _fx.ResetAsync();
        var client = _fx.Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.PutAsync($"{MoviesUrl}/{Guid.NewGuid()}/ratings",
            new RateMovieRequest { Rating = 4 }.AsJsonContent());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- Delete a rating ------------------------------------------------------

    [Fact]
    public async Task DeleteRating_without_authentication_is_rejected()
    {
        await _fx.ResetAsync();
        var movie = await CreateMovieAsync();
        var client = _fx.Factory.CreateAnonymousClient();

        var response = await client.DeleteAsync($"{MoviesUrl}/{movie.Id}/ratings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteRating_removes_an_existing_rating()
    {
        await _fx.ResetAsync();
        var userId = Guid.NewGuid();
        var movie = await CreateMovieAsync();
        var client = _fx.Factory.CreateAuthenticatedClient(userId);
        await client.PutAsync($"{MoviesUrl}/{movie.Id}/ratings", new RateMovieRequest { Rating = 4 }.AsJsonContent());

        var response = await client.DeleteAsync($"{MoviesUrl}/{movie.Id}/ratings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteRating_when_none_exists_returns_not_found()
    {
        await _fx.ResetAsync();
        var movie = await CreateMovieAsync();
        var client = _fx.Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.DeleteAsync($"{MoviesUrl}/{movie.Id}/ratings");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- Get the user's ratings ----------------------------------------------

    [Fact]
    public async Task GetUserRatings_without_authentication_is_rejected()
    {
        await _fx.ResetAsync();
        var client = _fx.Factory.CreateAnonymousClient();

        var response = await client.GetAsync(UserRatingsUrl);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetUserRatings_returns_only_the_callers_ratings()
    {
        await _fx.ResetAsync();
        var userId = Guid.NewGuid();
        var first = await CreateMovieAsync();
        var second = await CreateMovieAsync();
        var client = _fx.Factory.CreateAuthenticatedClient(userId);
        await client.PutAsync($"{MoviesUrl}/{first.Id}/ratings", new RateMovieRequest { Rating = 2 }.AsJsonContent());
        await client.PutAsync($"{MoviesUrl}/{second.Id}/ratings", new RateMovieRequest { Rating = 5 }.AsJsonContent());

        var response = await client.GetAsync(UserRatingsUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ratings = await response.ReadJsonAsync<IEnumerable<MovieRatingResponse>>();
        Assert.Equal(2, ratings!.Count());
    }

    private async Task<MovieResponse> CreateMovieAsync()
    {
        var client = _fx.Factory.CreateAuthenticatedClient(trusted: true);
        var response = await client.PostAsync(MoviesUrl, TestData.CreateMovieRequest.Generate().AsJsonContent());
        response.EnsureSuccessStatusCode();
        return (await response.ReadJsonAsync<MovieResponse>())!;
    }
}
