namespace Movies.Api.Endpoints.V2;

public static class AdminEndpointExtensions
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapEvictCache();

        return app;
    }
}
