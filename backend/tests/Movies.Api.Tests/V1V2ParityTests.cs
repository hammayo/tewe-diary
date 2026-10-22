using System.Text.Json.Nodes;

namespace Movies.Api.Tests;

// v2 must answer the same request exactly as v1 does, errors included. Each case sends one request
// to both versions and compares the status, the content type and the JSON body (minus the
// per-request traceId).
[Collection("api")]
public class V1V2ParityTests
{
    private readonly ApiFixture _fx;

    public V1V2ParityTests(ApiFixture fx) => _fx = fx;

    [Theory]
    [InlineData("movies/7f9c3d1e-0000-0000-0000-000000000000")] // unknown id
    [InlineData("movies/this-movie-does-not-exist-2020")]       // unknown slug
    public async Task a_get_for_an_unknown_movie_has_the_same_problem_body_in_both_versions(string path)
    {
        // Arrange
        await _fx.ResetAsync();
        var client = _fx.Factory.CreateAnonymousClient();

        // Act
        var v1 = await Describe(await client.GetAsync($"/api/v1/{path}"));
        var v2 = await Describe(await client.GetAsync($"/api/v2/{path}"));

        // Assert
        Assert.Equal(v1, v2);
    }

    private static async Task<string> Describe(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var json = body.Length > 0 ? JsonNode.Parse(body) as JsonObject : null;
        json?.Remove("traceId");

        return $"{(int)response.StatusCode} {response.Content.Headers.ContentType?.MediaType} {json?.ToJsonString() ?? body}";
    }
}
