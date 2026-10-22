using Movies.Api.Auth;
using Movies.Api.Mapping;
using Movies.Application.Services;
using Movies.Contracts.Requests;
using Movies.Contracts.Responses;

namespace Movies.Api.Endpoints.V2;

public static class GetAllMoviesEndpoint
{
    public const string Name = "GetMoviesV2";

    public static IEndpointRouteBuilder MapGetAllMovies(this IEndpointRouteBuilder app)
    {
        app.MapGet(ApiEndpoints.Movies.GetAll, async ([AsParameters] GetAllMoviesRequest request,
                HttpContext context, IMovieService movieService, TmdbUrlBuilder urls, MovieLinkBuilder links,
                CancellationToken token) =>
            {
                var options = request.MapToOptions().WithUser(context.GetUserId());
                var movies = await movieService.GetAllAsync(options, token);
                var movieCount = await movieService.GetCountAsync(options.Title, options.YearOfRelease, token);

                return Results.Ok(movies.MapToResponse(urls, links, options.Page, options.PageSize, movieCount));
            })
            .WithName(Name)
            .HasApiVersion(2.0)
            .CacheOutput("MovieCache")
            .Produces<MoviesResponse>();

        return app;
    }
}
