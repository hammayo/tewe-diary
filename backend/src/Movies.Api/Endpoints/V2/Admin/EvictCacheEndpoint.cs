using Microsoft.AspNetCore.OutputCaching;

namespace Movies.Api.Endpoints.V2;

// Evicts the "movies" output-cache tag. The TMDB import writes to the database directly, bypassing
// the endpoints' cache eviction, so the deploy pipeline calls this after a successful import.
// API-key protected (no JWT), like its v1 twin.
public static class EvictCacheEndpoint
{
    public const string Name = "EvictCacheV2";

    public static IEndpointRouteBuilder MapEvictCache(this IEndpointRouteBuilder app)
    {
        app.MapPost(ApiEndpoints.Admin.EvictCache, async (IOutputCacheStore outputCacheStore, CancellationToken token) =>
            {
                await outputCacheStore.EvictByTagAsync("movies", token);

                return Results.Ok();
            })
            .AddEndpointFilter<ApiKeyEndpointFilter>()
            .WithName(Name)
            .HasApiVersion(2.0)
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }
}
