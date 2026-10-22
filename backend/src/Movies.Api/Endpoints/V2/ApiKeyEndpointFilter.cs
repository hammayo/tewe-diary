using Movies.Api.Auth;

namespace Movies.Api.Endpoints.V2;

// Endpoint-filter twin of ApiKeyAuthFilter (the MVC filter v1 uses), with the same checks and the
// same plain-text 401 bodies. The few lines are repeated so the v1 filter stays untouched.
public class ApiKeyEndpointFilter : IEndpointFilter
{
    private readonly IConfiguration _configuration;

    public ApiKeyEndpointFilter(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!context.HttpContext.Request.Headers.TryGetValue(AuthConstants.ApiKeyHeaderName, out var extractedApiKey))
        {
            return Results.Text("API Key missing", statusCode: StatusCodes.Status401Unauthorized);
        }

        var apiKey = _configuration["API_KEY"]
                     ?? throw new InvalidOperationException("API_KEY is not configured.");
        if (apiKey != extractedApiKey)
        {
            return Results.Text("Invalid API Key", statusCode: StatusCodes.Status401Unauthorized);
        }

        return await next(context);
    }
}
