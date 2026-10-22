using System.Text.Json;

namespace Movies.Api.Tests;

// The Swagger UI offers one document per API version. v2 describes every v1 operation except the admin cache
// evict (a machine call for the deploy pipeline, so v1 only), and is as well labelled as the controllers behind v1.
[Collection("api")]
public class SwaggerV2Tests
{
    private static readonly string[] HttpMethods = ["get", "post", "put", "delete"];
    private static readonly (string Method, string Path) V1OnlyOperation = ("post", "/api/v{version}/admin/cache/evict");

    private readonly ApiFixture _fx;

    public SwaggerV2Tests(ApiFixture fx) => _fx = fx;

    [Theory]
    [InlineData("1.0", 9)]
    [InlineData("2.0", 8)]
    public async Task each_version_has_its_own_document_with_its_operations_and_the_bearer_scheme(string version, int expected)
    {
        // Arrange
        var document = await GetDocumentAsync(version);

        // Act
        var operations = Operations(document).ToList();
        var schemes = document.GetProperty("components").GetProperty("securitySchemes");

        // Assert
        Assert.Equal(version, document.GetProperty("info").GetProperty("version").GetString());
        Assert.Equal(expected, operations.Count);
        Assert.True(schemes.TryGetProperty("Bearer", out _));
    }

    [Fact]
    public async Task the_swagger_page_styles_the_version_badge()
    {
        // Arrange
        var client = _fx.Factory.CreateAnonymousClient();

        // Act
        var page = await client.GetStringAsync("/swagger/index.html");

        // Assert
        Assert.Contains(".info .title small:not(.version-stamp)", page);
    }

    [Fact]
    public async Task v2_operations_have_unique_ids()
    {
        // Arrange
        var document = await GetDocumentAsync("2.0");

        // Act
        var ids = Operations(document).Select(o => o.Operation.GetProperty("operationId").GetString()).ToList();

        // Assert
        Assert.Equal(8, ids.Distinct().Count());
        Assert.DoesNotContain(null, ids);
    }

    [Fact]
    public async Task v2_operations_are_tagged_like_their_v1_twins()
    {
        // Arrange
        var v1 = Operations(await GetDocumentAsync("1.0")).ToDictionary(o => (o.Method, o.Path), o => Tags(o.Operation));
        var v2 = Operations(await GetDocumentAsync("2.0")).ToDictionary(o => (o.Method, o.Path), o => Tags(o.Operation));

        // Act
        var onlyInV1 = v1.Keys.Except(v2.Keys).ToList();
        var onlyInV2 = v2.Keys.Except(v1.Keys).ToList();

        // Assert
        Assert.Equal(new[] { V1OnlyOperation }, onlyInV1);
        Assert.Empty(onlyInV2);
        foreach (var key in v2.Keys)
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
        var missing = v1.Where(o => v2.ContainsKey(o.Key) && !o.Value.IsSubsetOf(v2[o.Key])).Select(o => o.Key).ToList();

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
