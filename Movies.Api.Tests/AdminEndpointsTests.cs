using System.Net;
using NSubstitute;

namespace Movies.Api.Tests;

// Consolidates the former CacheEvictionEndpointTests. The eviction endpoint is API-key protected
// (no JWT) and must survive TLS termination at App Service (X-Forwarded-Proto=https).
[Collection("api")]
public class AdminEndpointsTests
{
    private const string EvictUrl = "/api/admin/cache/evict";

    private readonly ApiFixture _fx;

    public AdminEndpointsTests(ApiFixture fx) => _fx = fx;

    [Theory]
    [InlineData(null)]              // no x-api-key header at all
    [InlineData("not-the-key")]    // wrong key
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

    [Theory]
    [InlineData(false)] // direct request
    [InlineData(true)]  // App Service terminates TLS: container receives X-Forwarded-Proto=https
    public async Task evict_with_a_valid_api_key_evicts_the_movies_tag(bool behindForwardedProto)
    {
        // Arrange
        await _fx.ResetAsync();
        var client = _fx.Factory.CreateApiKeyClient();
        if (behindForwardedProto)
        {
            // The ForwardedHeaders middleware must honour it without breaking routing or auth.
            client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        }

        // Act
        var response = await client.PostAsync(EvictUrl, content: null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _fx.CacheStore.Received().EvictByTagAsync("movies", Arg.Any<CancellationToken>());
    }
}
