using System.Text;
using System.Text.Json.Nodes;

namespace Movies.Api.Tests;

// v2 must answer the same request exactly as v1 does, errors included. Each case sends one request
// to both versions and compares what the client can rely on (see Describe).
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

    [Fact]
    public async Task a_malformed_query_gets_the_same_validation_problem_shape_in_both_versions()
    {
        // Arrange
        await _fx.ResetAsync();
        var client = _fx.Factory.CreateAnonymousClient();

        // Act
        var v1 = await Describe(await client.GetAsync("/api/v1/movies?page=abc"));
        var v2 = await Describe(await client.GetAsync("/api/v2/movies?page=abc"));

        // Assert
        Assert.Equal(v1, v2);
    }

    [Theory]
    [InlineData("POST", "movies", "none", """{"title":"x","yearOfRelease":2000,"genres":["a"]}""")]   // not signed in
    [InlineData("POST", "movies", "trusted", "{ this is not json")]                                  // malformed body
    [InlineData("POST", "movies", "trusted", """{"title":"","yearOfRelease":2000,"genres":["a"]}""")] // validation
    [InlineData("PUT", "movies/7f9c3d1e-0000-0000-0000-000000000000", "trusted", """{"title":"x","yearOfRelease":2000,"genres":["a"]}""")] // missing
    [InlineData("DELETE", "movies/7f9c3d1e-0000-0000-0000-000000000000", "trusted", null)]           // wrong role
    [InlineData("DELETE", "movies/7f9c3d1e-0000-0000-0000-000000000000", "admin", null)]             // missing
    [InlineData("PUT", "movies/7f9c3d1e-0000-0000-0000-000000000000/ratings", "user", """{"rating":4}""")] // missing movie
    [InlineData("PUT", "movies/7f9c3d1e-0000-0000-0000-000000000000/ratings", "none", """{"rating":4}""")] // not signed in
    [InlineData("PUT", "movies/7f9c3d1e-0000-0000-0000-000000000000/ratings", "user", "{ not json")]         // malformed body
    [InlineData("DELETE", "movies/7f9c3d1e-0000-0000-0000-000000000000/ratings", "user", null)]             // nothing to remove
    [InlineData("GET", "ratings/me", "none", null)]                                                        // not signed in
    public async Task a_write_gets_the_same_answer_in_both_versions(string method, string path, string caller, string? body)
    {
        // Arrange
        await _fx.ResetAsync();
        var client = caller switch
        {
            "trusted" => _fx.Factory.CreateAuthenticatedClient(trusted: true),
            "admin" => _fx.Factory.CreateAuthenticatedClient(admin: true),
            "user" => _fx.Factory.CreateAuthenticatedClient(Guid.NewGuid()),
            _ => _fx.Factory.CreateAnonymousClient()
        };

        HttpRequestMessage Build(string version) => new(new HttpMethod(method), $"/api/{version}/{path}")
        {
            Content = body is null ? null : new StringContent(body, Encoding.UTF8, "application/json")
        };

        // Act
        var v1 = await Describe(await client.SendAsync(Build("v1")));
        var v2 = await Describe(await client.SendAsync(Build("v2")));

        // Assert
        Assert.Equal(v1, v2);
    }

    // Status, content type and the body's shape: its property names, plus the values of the
    // problem fields that identify the error. Message text and per-request ids are not compared.
    private static async Task<string> Describe(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var status = $"{(int)response.StatusCode} {response.Content.Headers.ContentType?.MediaType}";
        var isJson = response.Content.Headers.ContentType?.MediaType?.Contains("json") == true;
        if (!isJson || body.Length == 0 || JsonNode.Parse(body) is not JsonObject json)
        {
            return $"{status} {body}";
        }

        var names = string.Join(",", json.Select(p => p.Key).Order());
        return $"{status} [{names}] {json["title"]} {json["type"]} {json["status"]}";
    }
}
