using Asp.Versioning;
using Microsoft.AspNetCore.OutputCaching;
using Movies.Api.Auth;
using Movies.Api.Mapping;
using Movies.Application.Services;
using Movies.Contracts.Requests;
using Movies.Contracts.Responses;

namespace Movies.Api.Endpoints.V2;

public static class CreateMovieEndpoint
{
    public const string Name = "CreateMovieV2";

    public static IEndpointRouteBuilder MapCreateMovie(this IEndpointRouteBuilder app)
    {
        app.MapPost(ApiEndpoints.Movies.Create, async (CreateMovieRequest request, HttpContext context, IMovieService movieService,
                IOutputCacheStore outputCacheStore, TmdbUrlBuilder urls, MovieLinkBuilder links,
                CancellationToken token) =>
            {
                var movie = request.MapToMovie();
                await movieService.CreateAsync(movie, token);
                await outputCacheStore.EvictByTagAsync("movies", token);

                return TypedResults.CreatedAtRoute(movie.MapToResponse(urls, links), GetMovieEndpoint.Name,
                    new { idOrSlug = movie.Id, version = context.GetRequestedApiVersion()?.ToString() });
            })
            .WithName(Name)
            .HasApiVersion(2.0)
            .WithTags("Movies")
            .RequireAuthorization(AuthConstants.TrustedMemberPolicyName)
            .Produces<MovieResponse>(StatusCodes.Status201Created)
            .Produces<ValidationFailureResponse>(StatusCodes.Status400BadRequest);

        return app;
    }
}
