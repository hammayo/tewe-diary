using System.Text.Json;

namespace Movies.Api.Tests;

// The Swagger UI offers one document per API version. Both must describe the same nine operations,
// and v2 must be as well labelled as the controllers behind v1.
[Collection("api")]
public class SwaggerV2Tests
{
    private static readonly string[] HttpMethods = ["get", "post", "put", "delete"];

    private readonly ApiFixture _fx;

    public SwaggerV2Tests(ApiFixture fx) => _fx = fx;

    [Theory]
    [InlineData("1.0")]
    [InlineData("2.0")]
    public async Task each_version_has_its_own_document_with_nine_operations_and_the_bearer_scheme(string version)
    {
        // Arrange
        var document = await GetDocumentAsync(version);

        // Act
        var operations = Operations(document).ToList();
        var schemes = document.GetProperty("components").GetProperty("securitySchemes");

        // Assert
        Assert.Equal(version, document.GetProperty("info").GetProperty("version").GetString());
        Assert.Equal(9, operations.Count);
        Assert.True(schemes.TryGetProperty("Bearer", out _));
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("2.0")]
    public async Task the_description_names_the_version_being_viewed(string version)
    {
        // Arrange
        var document = await GetDocumentAsync(version);

        // Act
        var description = document.GetProperty("info").GetProperty("description").GetString();

        // Assert
        Assert.Equal($"This product {version} uses the TMDB API but is not endorsed or certified by TMDB.", description);
    }

    [Fact]
    public async Task v2_operations_have_unique_ids()
    {
        // Arrange
        var document = await GetDocumentAsync("2.0");

        // Act
        var ids = Operations(document).Select(o => o.Operation.GetProperty("operationId").GetString()).ToList();

        // Assert
        Assert.Equal(9, ids.Distinct().Count());
        Assert.DoesNotContain(null, ids);
    }

    [Fact]
    public async Task v2_operations_are_tagged_like_their_v1_twins()
    {
        // Arrange
        var v1 = Operations(await GetDocumentAsync("1.0")).ToDictionary(o => (o.Method, o.Path), o => Tags(o.Operation));
        var v2 = Operations(await GetDocumentAsync("2.0")).ToDictionary(o => (o.Method, o.Path), o => Tags(o.Operation));

        // Act
        var onlyInOne = v1.Keys.Except(v2.Keys).Concat(v2.Keys.Except(v1.Keys)).ToList();

        // Assert
        Assert.Empty(onlyInOne);
        foreach (var key in v1.Keys)
        {
            Assert.Equal(v1[key], v2[key]);
        }
    }

    [Fact]
    public async Task v2_documents_every_status_its_v1_twin_does()
    {
        // Arrange
        var v1 = Operations(await GetDocumentAsync("1.0")).ToDictionary(o => (o.Method, o.Path), o => Statuses(o.Operation));
        var v2 = Operations(await GetDocumentAsync("2.0")).ToDictionary(o => (o.Method, o.Path), o => Statuses(o.Operation));

        // Act
        var missing = v1.Where(o => !o.Value.IsSubsetOf(v2[o.Key])).Select(o => o.Key).ToList();

        // Assert
        Assert.Empty(missing);
    }

    [Fact]
    public async Task the_swagger_ui_offers_both_versions()
    {
        // Arrange
        var client = _fx.Factory.CreateAnonymousClient();

        // Act
        var script = await client.GetStringAsync("/swagger/index.js");

        // Assert
        Assert.Contains("/swagger/1.0/swagger.json", script);
        Assert.Contains("/swagger/2.0/swagger.json", script);
    }

    private async Task<JsonElement> GetDocumentAsync(string version)
    {
        var response = await _fx.Factory.CreateAnonymousClient().GetAsync($"/swagger/{version}/swagger.json");
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private static IEnumerable<(string Method, string Path, JsonElement Operation)> Operations(JsonElement document) =>
        document.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(m => HttpMethods.Contains(m.Name))
                .Select(m => (m.Name, path.Name, m.Value)));

    private static string Tags(JsonElement operation) =>
        string.Join(",", operation.GetProperty("tags").EnumerateArray().Select(t => t.GetString()));

    private static HashSet<string> Statuses(JsonElement operation) =>
        operation.GetProperty("responses").EnumerateObject().Select(r => r.Name).ToHashSet();
}
