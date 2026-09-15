using System.Net;
using Microsoft.AspNetCore.OutputCaching;
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
    public async Task Evict_without_a_valid_api_key_is_rejected(string? apiKey)
    {
        await _fx.ResetAsync();
        var client = _fx.Factory.CreateApiKeyClient(apiKey);

        var response = await client.PostAsync(EvictUrl, content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await _fx.CacheStore.DidNotReceive().EvictByTagAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Evict_with_a_valid_api_key_evicts_the_movies_tag()
    {
        await _fx.ResetAsync();
        var client = _fx.Factory.CreateApiKeyClient();

        var response = await client.PostAsync(EvictUrl, content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _fx.CacheStore.Received().EvictByTagAsync("movies", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Evict_still_works_behind_a_forwarded_proto_header()
    {
        // Simulates App Service terminating TLS: the container receives X-Forwarded-Proto=https.
        // The ForwardedHeaders middleware must honour it without breaking routing or auth.
        await _fx.ResetAsync();
        var client = _fx.Factory.CreateApiKeyClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

        var response = await client.PostAsync(EvictUrl, content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _fx.CacheStore.Received().EvictByTagAsync("movies", Arg.Any<CancellationToken>());
    }
}
