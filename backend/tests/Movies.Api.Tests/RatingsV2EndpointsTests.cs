using System.Net;
using Movies.Contracts.Requests;
using Movies.Contracts.Responses;
using Movies.Tests.Shared;

namespace Movies.Api.Tests;

// v2 twins of RatingsEndpointsTests.
[Collection("api")]
public class RatingsV2EndpointsTests
{
    private const string MoviesUrl = "/api/v2/movies";
    private const string UserRatingsUrl = "/api/v2/ratings/me";

    private readonly ApiFixture _fx;

    public RatingsV2EndpointsTests(ApiFixture fx) => _fx = fx;

    [Fact]
    public async Task rate_without_authentication_is_rejected()
    {
        // Arrange
        await _fx.ResetAsync();
        var movie = await CreateMovieAsync();
        var client = _fx.Factory.CreateAnonymousClient();

        // Act
        var response = await client.PutAsync($"{MoviesUrl}/{movie.Id}/ratings", new RateMovieRequest { Rating = 4 }.AsJsonContent());

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task rate_with_a_valid_score_succeeds(int rating)
    {
        // Arrange
        await _fx.ResetAsync();
        var movie = await CreateMovieAsync();
        var client = _fx.Factory.CreateAuthenticatedClient(Guid.NewGuid());

        // Act
        var response = await client.PutAsync($"{MoviesUrl}/{movie.Id}/ratings", new RateMovieRequest { Rating = rating }.AsJsonContent());
        var rated = await (await client.GetAsync($"{MoviesUrl}/{movie.Id}")).ReadJsonAsync<MovieResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(rating, rated!.UserRating);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public async Task rate_with_an_out_of_range_score_returns_bad_request(int rating)
    {
        // Arrange
        await _fx.ResetAsync();
        var movie = await CreateMovieAsync();
        var client = _fx.Factory.CreateAuthenticatedClient(Guid.NewGuid());

        // Act
        var response = await client.PutAsync($"{MoviesUrl}/{movie.Id}/ratings", new RateMovieRequest { Rating = rating }.AsJsonContent());

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task rate_a_missing_movie_returns_not_found()
    {
        // Arrange
        await _fx.ResetAsync();
        var client = _fx.Factory.CreateAuthenticatedClient(Guid.NewGuid());

        // Act
        var response = await client.PutAsync($"{MoviesUrl}/{Guid.NewGuid()}/ratings", new RateMovieRequest { Rating = 4 }.AsJsonContent());

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task delete_rating_without_authentication_is_rejected()
    {
        // Arrange
        await _fx.ResetAsync();
        var movie = await CreateMovieAsync();
        var client = _fx.Factory.CreateAnonymousClient();

        // Act
        var response = await client.DeleteAsync($"{MoviesUrl}/{movie.Id}/ratings");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task delete_rating_removes_an_existing_rating()
    {
        // Arrange
        await _fx.ResetAsync();
        var movie = await CreateMovieAsync();
        var client = _fx.Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.PutAsync($"{MoviesUrl}/{movie.Id}/ratings", new RateMovieRequest { Rating = 4 }.AsJsonContent());

        // Act
        var response = await client.DeleteAsync($"{MoviesUrl}/{movie.Id}/ratings");
        var afterwards = await (await client.GetAsync($"{MoviesUrl}/{movie.Id}")).ReadJsonAsync<MovieResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(afterwards!.UserRating);
    }

    [Fact]
    public async Task delete_rating_when_none_exists_returns_not_found()
    {
        // Arrange
        await _fx.ResetAsync();
        var movie = await CreateMovieAsync();
        var client = _fx.Factory.CreateAuthenticatedClient(Guid.NewGuid());

        // Act
        var response = await client.DeleteAsync($"{MoviesUrl}/{movie.Id}/ratings");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task get_user_ratings_without_authentication_is_rejected()
    {
        // Arrange
        await _fx.ResetAsync();
        var client = _fx.Factory.CreateAnonymousClient();

        // Act
        var response = await client.GetAsync(UserRatingsUrl);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task get_user_ratings_returns_only_the_callers_ratings()
    {
        // Arrange
        await _fx.ResetAsync();
        var first = await CreateMovieAsync();
        var second = await CreateMovieAsync();
        var client = _fx.Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var someoneElse = _fx.Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.PutAsync($"{MoviesUrl}/{first.Id}/ratings", new RateMovieRequest { Rating = 2 }.AsJsonContent());
        await client.PutAsync($"{MoviesUrl}/{second.Id}/ratings", new RateMovieRequest { Rating = 5 }.AsJsonContent());
        await someoneElse.PutAsync($"{MoviesUrl}/{first.Id}/ratings", new RateMovieRequest { Rating = 1 }.AsJsonContent());

        // Act
        var response = await client.GetAsync(UserRatingsUrl);
        var ratings = (await response.ReadJsonAsync<IEnumerable<MovieRatingResponse>>())!.ToList();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, ratings.Count);
        Assert.Equal(new[] { 2, 5 }, ratings.Select(r => r.Rating).Order());
    }

    private async Task<MovieResponse> CreateMovieAsync()
    {
        var client = _fx.Factory.CreateAuthenticatedClient(trusted: true);
        var response = await client.PostAsync(MoviesUrl, TestData.CreateMovieRequest.Generate().AsJsonContent());
        response.EnsureSuccessStatusCode();
        return (await response.ReadJsonAsync<MovieResponse>())!;
    }
}
