using System.Net;

namespace Movies.Api.Tests;

public class CacheEvictionEndpointTests
{
    private const string EvictUrl = "/api/admin/cache/evict";

    [Fact]
    public async Task Evict_without_api_key_is_rejected()
    {
        using var factory = new MoviesApiFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsync(EvictUrl, content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(factory.CacheStore.EvictedTags);
    }

    [Fact]
    public async Task Evict_with_wrong_api_key_is_rejected()
    {
        using var factory = new MoviesApiFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("x-api-key", "not-the-key");

        var response = await client.PostAsync(EvictUrl, content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(factory.CacheStore.EvictedTags);
    }

    [Fact]
    public async Task Evict_with_valid_api_key_evicts_the_movies_tag()
    {
        using var factory = new MoviesApiFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("x-api-key", MoviesApiFactory.TestApiKey);

        var response = await client.PostAsync(EvictUrl, content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("movies", factory.CacheStore.EvictedTags);
    }

    [Fact]
    public async Task Evict_still_works_behind_a_forwarded_proto_header()
    {
        // Simulates App Service terminating TLS: the container receives X-Forwarded-Proto=https.
        // The ForwardedHeaders middleware must honour it without breaking routing or auth.
        using var factory = new MoviesApiFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("x-api-key", MoviesApiFactory.TestApiKey);
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

        var response = await client.PostAsync(EvictUrl, content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("movies", factory.CacheStore.EvictedTags);
    }
}
