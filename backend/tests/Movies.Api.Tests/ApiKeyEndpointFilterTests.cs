using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Movies.Api.Auth;
using Movies.Api.Endpoints.V2;

namespace Movies.Api.Tests;

public class ApiKeyEndpointFilterTests
{
    private const string Key = "secret";

    [Theory]
    [InlineData(null, "API Key missing")]
    [InlineData("wrong", "Invalid API Key")]
    public async Task a_missing_or_wrong_key_returns_401_with_the_v1_text(string? header, string expected)
    {
        // Arrange
        var filter = CreateFilter(Key);
        var context = CreateContext(header);
        var nextCalled = false;

        // Act
        var result = await filter.InvokeAsync(context, _ => { nextCalled = true; return ValueTask.FromResult<object?>(null); });
        await ((IResult)result!).ExecuteAsync(context.HttpContext);

        // Assert
        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.HttpContext.Response.StatusCode);
        Assert.Equal(expected, await ReadBody(context.HttpContext));
    }

    [Fact]
    public async Task the_right_key_runs_the_endpoint()
    {
        // Arrange
        var filter = CreateFilter(Key);
        var context = CreateContext(Key);

        // Act
        var result = await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>("ok"));

        // Assert
        Assert.Equal("ok", result);
    }

    [Fact]
    public async Task an_unset_api_key_setting_throws()
    {
        // Arrange
        var filter = CreateFilter(null);
        var context = CreateContext("anything");

        // Act
        var act = async () => await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>(null));

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    private static ApiKeyEndpointFilter CreateFilter(string? apiKey) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["API_KEY"] = apiKey })
            .Build());

    private static DefaultEndpointFilterInvocationContext CreateContext(string? header)
    {
        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider() };
        http.Response.Body = new MemoryStream();
        if (header is not null)
        {
            http.Request.Headers[AuthConstants.ApiKeyHeaderName] = header;
        }

        return new DefaultEndpointFilterInvocationContext(http);
    }

    private static async Task<string> ReadBody(HttpContext http)
    {
        http.Response.Body.Position = 0;
        return await new StreamReader(http.Response.Body).ReadToEndAsync();
    }
}
