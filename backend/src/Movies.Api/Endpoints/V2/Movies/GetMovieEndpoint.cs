using Movies.Api.Auth;
using Movies.Api.Mapping;
using Movies.Application.Services;
using Movies.Contracts.Responses;

namespace Movies.Api.Endpoints.V2;

public static class GetMovieEndpoint
{
    public const string Name = "GetMovieV2";

    public static IEndpointRouteBuilder MapGetMovie(this IEndpointRouteBuilder app)
    {
        app.MapGet(ApiEndpoints.Movies.Get, async (string idOrSlug, HttpContext context,
                IMovieService movieService, TmdbUrlBuilder urls, CancellationToken token) =>
            {
                var movie = await movieService.GetByIdOrSlugAsync(idOrSlug, context.GetUserId(), token);

                return movie is null ? Results.NotFound() : Results.Ok(movie.MapToResponse(urls, null));
            })
            .WithName(Name)
            .HasApiVersion(2.0)
            .CacheOutput("MovieCache")
            .Produces<MovieResponse>()
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
