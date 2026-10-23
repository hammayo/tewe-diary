using Movies.Api.Auth;
using Movies.Application.Services;

namespace Movies.Api.Endpoints.V2;

public static class DeleteRatingEndpoint
{
    public const string Name = "DeleteRatingV2";

    public static IEndpointRouteBuilder MapDeleteRating(this IEndpointRouteBuilder app)
    {
        app.MapDelete(ApiEndpoints.Movies.DeleteRating, async (Guid id, HttpContext context,
                IRatingService ratingService, CancellationToken token) =>
            {
                var deleted = await ratingService.DeleteRatingAsync(id, context.GetUserId()!.Value, token);

                return deleted ? Results.Ok() : V2Problems.NotFound();
            })
            .WithName(Name)
            .HasApiVersion(2.0)
            .WithTags("Ratings")
            .RequireAuthorization()
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
