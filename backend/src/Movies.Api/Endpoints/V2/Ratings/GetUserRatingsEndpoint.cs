using Movies.Api.Auth;
using Movies.Api.Mapping;
using Movies.Application.Services;
using Movies.Contracts.Responses;

namespace Movies.Api.Endpoints.V2;

public static class GetUserRatingsEndpoint
{
    public const string Name = "GetUserRatingsV2";

    public static IEndpointRouteBuilder MapGetUserRatings(this IEndpointRouteBuilder app)
    {
        app.MapGet(ApiEndpoints.Ratings.GetUserRatings, async (HttpContext context,
                IRatingService ratingService, CancellationToken token) =>
            {
                var ratings = await ratingService.GetRatingsForUserAsync(context.GetUserId()!.Value, token);

                return Results.Ok(ratings.MapToResponse());
            })
            .WithName(Name)
            .HasApiVersion(2.0)
            .RequireAuthorization()
            .Produces<IEnumerable<MovieRatingResponse>>();

        return app;
    }
}
