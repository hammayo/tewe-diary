using Movies.Contracts.Requests;
using Movies.Contracts.Responses;
using Movies.Tests.Shared;

namespace Movies.Api.Tests;

// Both versions sit on the same service layer and the same "movies" cache tag. The test host swaps the
// output-cache store for a spy, so these tests prove what the handlers return, not cache hits.
[Collection("api")]
public class V1V2CacheTests
{
    private readonly ApiFixture _fx;

    public V1V2CacheTests(ApiFixture fx) => _fx = fx;

    [Fact]
    public async Task a_signed_in_v2_get_returns_the_callers_own_rating()
    {
        // Arrange
        await _fx.ResetAsync();
        var userId = Guid.NewGuid();
        var trusted = _fx.Factory.CreateAuthenticatedClient(trusted: true);
        var created = (await (await trusted.PostAsync("/api/v1/movies", TestData.CreateMovieRequest.Generate().AsJsonContent()))
            .ReadJsonAsync<MovieResponse>())!;
        var user = _fx.Factory.CreateAuthenticatedClient(userId);
        await user.PutAsync($"/api/v1/movies/{created.Id}/ratings", new RateMovieRequest { Rating = 4 }.AsJsonContent());

        // Act
        var signedIn = await (await user.GetAsync($"/api/v2/movies/{created.Id}")).ReadJsonAsync<MovieResponse>();
        var anonymous = await (await _fx.Factory.CreateAnonymousClient().GetAsync($"/api/v2/movies/{created.Id}"))
            .ReadJsonAsync<MovieResponse>();

        // Assert
        Assert.Equal(4, signedIn!.UserRating);
        Assert.Null(anonymous!.UserRating);
    }
}
