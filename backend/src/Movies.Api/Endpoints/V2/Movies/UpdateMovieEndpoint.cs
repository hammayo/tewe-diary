using Microsoft.AspNetCore.OutputCaching;
using Movies.Api.Auth;
using Movies.Api.Mapping;
using Movies.Application.Services;
using Movies.Contracts.Requests;
using Movies.Contracts.Responses;

namespace Movies.Api.Endpoints.V2;

public static class UpdateMovieEndpoint
{
    public const string Name = "UpdateMovieV2";

    public static IEndpointRouteBuilder MapUpdateMovie(this IEndpointRouteBuilder app)
    {
        app.MapPut(ApiEndpoints.Movies.Update, async (Guid id, UpdateMovieRequest request, HttpContext context,
                IMovieService movieService, IOutputCacheStore outputCacheStore, TmdbUrlBuilder urls,
                MovieLinkBuilder links, CancellationToken token) =>
            {
                var updated = await movieService.UpdateAsync(request.MapToMovie(id), context.GetUserId(), token);
                if (updated is null)
                {
                    return V2Problems.NotFound();
                }

                await outputCacheStore.EvictByTagAsync("movies", token);

                return Results.Ok(updated.MapToResponse(urls, links));
            })
            .WithName(Name)
            .HasApiVersion(2.0)
            .WithTags("Movies")
            .RequireAuthorization(AuthConstants.TrustedMemberPolicyName)
            .Produces<MovieResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces<ValidationFailureResponse>(StatusCodes.Status400BadRequest);

        return app;
    }
}
