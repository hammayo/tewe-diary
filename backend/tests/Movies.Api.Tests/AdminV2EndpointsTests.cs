using System.Net;
using NSubstitute;

namespace Movies.Api.Tests;

// v2 twins of AdminEndpointsTests: the evict endpoint is API-key protected (no JWT) and must survive
// TLS termination at App Service (X-Forwarded-Proto=https).
[Collection("api")]
public class AdminV2EndpointsTests
{
    private const string EvictUrl = "/api/v2/admin/cache/evict";

    private readonly ApiFixture _fx;

    public AdminV2EndpointsTests(ApiFixture fx) => _fx = fx;

    [Theory]
    [InlineData(null)]
    [InlineData("not-the-key")]
    public async Task evict_without_a_valid_api_key_is_rejected(string? apiKey)
    {
        // Arrange
        await _fx.ResetAsync();
        var client = _fx.Factory.CreateApiKeyClient(apiKey);

        // Act
        var response = await client.PostAsync(EvictUrl, content: null);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await _fx.CacheStore.DidNotReceive().EvictByTagAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task a_jwt_is_not_enough_to_evict()
    {
        // Arrange
        await _fx.ResetAsync();
        var client = _fx.Factory.CreateAuthenticatedClient(admin: true);

        // Act
        var response = await client.PostAsync(EvictUrl, content: null);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task evict_with_a_valid_api_key_evicts_the_movies_tag(bool behindForwardedProto)
    {
        // Arrange
        await _fx.ResetAsync();
        var client = _fx.Factory.CreateApiKeyClient();
        if (behindForwardedProto)
        {
            client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        }

        // Act
        var response = await client.PostAsync(EvictUrl, content: null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _fx.CacheStore.Received().EvictByTagAsync("movies", Arg.Any<CancellationToken>());
    }
}
