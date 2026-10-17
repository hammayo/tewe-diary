using Asp.Versioning;
using Movies.Api.Auth;
using Movies.Api.Mapping;
using Movies.Application.Services;
using Movies.Contracts.Responses;

namespace Movies.Api.Endpoints.V2;

// v2 is a Minimal API surface beside the v1 controllers: same host, auth, versioning and cache.
// It starts with one read to prove the wiring; the real v2 surface comes with its own PRD.
public static class MoviesV2Endpoints
{
    private static readonly ApiVersion V2 = new(2.0);

    public static IEndpointRouteBuilder MapMoviesV2(this IEndpointRouteBuilder app)
    {
        var versions = app.NewApiVersionSet().HasApiVersion(V2).Build();

        app.MapGet(ApiEndpoints.Movies.Get, GetAsync)
            .WithApiVersionSet(versions)
            .HasApiVersion(V2)
            .CacheOutput("MovieCache")
            .Produces<MovieResponse>()
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> GetAsync(string idOrSlug, HttpContext context,
        IMovieService movieService, TmdbUrlBuilder urls, CancellationToken token)
    {
        var userId = context.GetUserId();

        var movie = Guid.TryParse(idOrSlug, out var id)
            ? await movieService.GetByIdAsync(id, userId, token)
            : await movieService.GetBySlugAsync(idOrSlug, userId, token);

        return movie is null ? Results.NotFound() : Results.Ok(movie.MapToResponse(urls, null));
    }
}
