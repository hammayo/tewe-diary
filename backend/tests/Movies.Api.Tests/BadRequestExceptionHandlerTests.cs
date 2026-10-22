using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Movies.Api.Startup;
using NSubstitute;

namespace Movies.Api.Tests;

public class BadRequestExceptionHandlerTests
{
    [Fact]
    public async Task a_binding_failure_becomes_a_400_validation_problem()
    {
        // Arrange
        var problemDetails = Substitute.For<IProblemDetailsService>();
        problemDetails.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(new ValueTask<bool>(true));
        var handler = new BadRequestExceptionHandler(problemDetails);
        var http = new DefaultHttpContext();

        // Act
        var handled = await handler.TryHandleAsync(
            http, new BadHttpRequestException("Failed to read parameter \"int page\" from the query string."),
            CancellationToken.None);

        // Assert
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status400BadRequest, http.Response.StatusCode);
        await problemDetails.Received(1).TryWriteAsync(Arg.Is<ProblemDetailsContext>(c =>
            c.ProblemDetails is ValidationProblemDetails && c.ProblemDetails.Status == StatusCodes.Status400BadRequest));
    }

    [Fact]
    public async Task any_other_exception_is_left_for_the_next_handler()
    {
        // Arrange
        var problemDetails = Substitute.For<IProblemDetailsService>();
        var handler = new BadRequestExceptionHandler(problemDetails);

        // Act
        var handled = await handler.TryHandleAsync(
            new DefaultHttpContext(), new InvalidOperationException("boom"), CancellationToken.None);

        // Assert
        Assert.False(handled);
        await problemDetails.DidNotReceiveWithAnyArgs().TryWriteAsync(default);
    }
}
