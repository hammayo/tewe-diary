using Microsoft.AspNetCore.OutputCaching;
using Movies.Api.Auth;
using Movies.Application.Services;

namespace Movies.Api.Endpoints.V2;

public static class DeleteMovieEndpoint
{
    public const string Name = "DeleteMovieV2";

    public static IEndpointRouteBuilder MapDeleteMovie(this IEndpointRouteBuilder app)
    {
        app.MapDelete(ApiEndpoints.Movies.Delete, async (Guid id, IMovieService movieService,
                IOutputCacheStore outputCacheStore, CancellationToken token) =>
            {
                if (!await movieService.DeleteByIdAsync(id, token))
                {
                    return V2Problems.NotFound();
                }

                await outputCacheStore.EvictByTagAsync("movies", token);

                return Results.Ok();
            })
            .WithName(Name)
            .HasApiVersion(2.0)
            .WithTags("Movies")
            .RequireAuthorization(AuthConstants.AdminUserPolicyName)
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
