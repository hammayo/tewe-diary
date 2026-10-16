using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Movies.Api.Startup;
using NSubstitute;

namespace Movies.Api.Tests;

public class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task unhandled_exception_returns_a_500_problem_details_body()
    {
        // Arrange
        var problemDetails = Substitute.For<IProblemDetailsService>();
        problemDetails.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(new ValueTask<bool>(true));
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance, problemDetails);
        var http = new DefaultHttpContext();
        var exception = new InvalidOperationException("boom");

        // Act
        var handled = await handler.TryHandleAsync(http, exception, CancellationToken.None);

        // Assert
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, http.Response.StatusCode);
        await problemDetails.Received(1).TryWriteAsync(Arg.Is<ProblemDetailsContext>(c =>
            c.Exception == exception && c.ProblemDetails.Status == StatusCodes.Status500InternalServerError));
    }
}
