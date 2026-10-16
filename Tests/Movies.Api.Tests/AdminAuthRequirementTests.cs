using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Movies.Api.Auth;

namespace Movies.Api.Tests;

public class AdminAuthRequirementTests
{
    private const string ApiKey = "test-api-key";

    private static AuthorizationHandlerContext ContextFor(AdminAuthRequirement requirement, object? resource, ClaimsPrincipal? user = null) =>
        new([requirement], user ?? new ClaimsPrincipal(new ClaimsIdentity()), resource);

    [Fact]
    public async Task admin_claim_succeeds_without_an_api_key()
    {
        // Arrange
        var requirement = new AdminAuthRequirement(ApiKey);
        var admin = new ClaimsPrincipal(new ClaimsIdentity([new Claim(AuthConstants.AdminUserClaimName, "true")]));
        var context = ContextFor(requirement, new DefaultHttpContext(), admin);

        // Act
        await requirement.HandleAsync(context);

        // Assert
        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task non_http_resource_neither_succeeds_nor_fails()
    {
        // Arrange
        var requirement = new AdminAuthRequirement(ApiKey);
        var context = ContextFor(requirement, resource: null);

        // Act
        await requirement.HandleAsync(context);

        // Assert
        Assert.False(context.HasSucceeded);
        Assert.False(context.HasFailed);
    }

    [Fact]
    public async Task missing_api_key_header_fails()
    {
        // Arrange
        var requirement = new AdminAuthRequirement(ApiKey);
        var http = new DefaultHttpContext();
        var context = ContextFor(requirement, http, http.User);

        // Act
        await requirement.HandleAsync(context);

        // Assert
        Assert.True(context.HasFailed);
    }

    [Fact]
    public async Task wrong_api_key_fails()
    {
        // Arrange
        var requirement = new AdminAuthRequirement(ApiKey);
        var http = new DefaultHttpContext();
        http.Request.Headers[AuthConstants.ApiKeyHeaderName] = "not-the-key";
        var context = ContextFor(requirement, http, http.User);

        // Act
        await requirement.HandleAsync(context);

        // Assert
        Assert.True(context.HasFailed);
    }

    [Fact]
    public async Task correct_api_key_succeeds_and_adds_a_userid_claim()
    {
        // Arrange
        var requirement = new AdminAuthRequirement(ApiKey);
        var http = new DefaultHttpContext();
        http.Request.Headers[AuthConstants.ApiKeyHeaderName] = ApiKey;
        var context = ContextFor(requirement, http, http.User);

        // Act
        await requirement.HandleAsync(context);

        // Assert
        Assert.True(context.HasSucceeded);
        Assert.True(http.User.HasClaim(c => c.Type == "userid"));
    }
}
