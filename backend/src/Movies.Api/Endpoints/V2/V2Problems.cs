namespace Movies.Api.Endpoints.V2;

// [ApiController] gives v1 a ProblemDetails body on a bare NotFound(); Results.NotFound() has none.
// v2 goes through here so both versions answer a missing resource in the same way.
public static class V2Problems
{
    public static IResult NotFound() => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound);
}
