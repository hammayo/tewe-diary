using Asp.Versioning;

namespace Movies.Api.Endpoints.V2;

public static class EndpointsExtensions
{
    // One version set (1.0 and 2.0) shared by every Minimal API endpoint. Each endpoint then declares
    // the version it serves with HasApiVersion; v1 stays on the controllers.
    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder app)
    {
        var versions = app.NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1.0))
            .HasApiVersion(new ApiVersion(2.0))
            .ReportApiVersions()
            .Build();

        var group = app.MapGroup(string.Empty).WithApiVersionSet(versions);

        group.MapMovieEndpoints();
        group.MapRatingEndpoints();

        return app;
    }
}
