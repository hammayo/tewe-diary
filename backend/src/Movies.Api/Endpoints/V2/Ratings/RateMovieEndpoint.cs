using Movies.Api.Auth;
using Movies.Application.Services;
using Movies.Contracts.Requests;
using Movies.Contracts.Responses;

namespace Movies.Api.Endpoints.V2;

public static class RateMovieEndpoint
{
    public const string Name = "RateMovieV2";

    public static IEndpointRouteBuilder MapRateMovie(this IEndpointRouteBuilder app)
    {
        app.MapPut(ApiEndpoints.Movies.Rate, async (Guid id, RateMovieRequest request, HttpContext context,
                IRatingService ratingService, CancellationToken token) =>
            {
                var rated = await ratingService.RateMovieAsync(id, request.Rating, context.GetUserId()!.Value, token);

                return rated ? Results.Ok() : V2Problems.NotFound();
            })
            .WithName(Name)
            .HasApiVersion(2.0)
            .RequireAuthorization()
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces<ValidationFailureResponse>(StatusCodes.Status400BadRequest);

        return app;
    }
}
