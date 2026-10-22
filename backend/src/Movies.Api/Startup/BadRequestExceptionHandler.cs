using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Movies.Api.Startup;

// Minimal API binding failures (bad JSON body, ?page=abc) surface as BadHttpRequestException once
// RouteHandlerOptions.ThrowOnBadRequest is on. [ApiController] answers the same mistakes with a 400
// ValidationProblemDetails, so v2 does too. Registered before GlobalExceptionHandler, which would
// otherwise turn them into a 500.
public class BadRequestExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;

    public BadRequestExceptionHandler(IProblemDetailsService problemDetailsService)
    {
        _problemDetailsService = problemDetailsService;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ValidationProblemDetails(
                new Dictionary<string, string[]> { ["request"] = [exception.Message] })
            {
                Status = StatusCodes.Status400BadRequest,
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1"
            }
        });
    }
}
