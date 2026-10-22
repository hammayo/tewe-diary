namespace Movies.Api.Endpoints.V2;

public static class MovieEndpointExtensions
{
    public static IEndpointRouteBuilder MapMovieEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGetAllMovies();
        app.MapGetMovie();
        app.MapCreateMovie();
        app.MapUpdateMovie();
        app.MapDeleteMovie();

        return app;
    }
}
