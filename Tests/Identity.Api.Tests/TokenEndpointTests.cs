using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text;
using System.Text.Json;
using Identity.Api.Contracts;

namespace Identity.Api.Tests;

public class TokenEndpointTests : IClassFixture<IdentityApiFactory>
{
    private const string TokenUrl = "/token";

    private readonly IdentityApiFactory _factory;

    public TokenEndpointTests(IdentityApiFactory factory) => _factory = factory;

    [Fact]
    public async Task token_is_issued_with_the_standard_claims()
    {
        // Arrange
        var client = _factory.CreateClient();
        var userId = Guid.NewGuid();
        var request = new TokenGenerationRequest { UserId = userId, Email = "user@test.local" };

        // Act
        var response = await client.PostAsync(TokenUrl, AsJson(request));
        var jwt = await ReadTokenAsync(response);
        var email = jwt.Claims.Single(c => c.Type == "email").Value;
        var userIdClaim = jwt.Claims.Single(c => c.Type == "userid").Value;

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(IdentityApiFactory.Issuer, jwt.Issuer);
        Assert.Contains(IdentityApiFactory.Audience, jwt.Audiences);
        Assert.Equal("user@test.local", email);
        Assert.Equal(userId.ToString(), userIdClaim);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("trusted_member")]
    public async Task token_includes_custom_boolean_claims(string claimName)
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new TokenGenerationRequest
        {
            UserId = Guid.NewGuid(),
            Email = "member@test.local",
            CustomClaims = new Dictionary<string, object> { [claimName] = true }
        };

        // Act
        var response = await client.PostAsync(TokenUrl, AsJson(request));
        var jwt = await ReadTokenAsync(response);
        var claimValue = jwt.Claims.Single(c => c.Type == claimName).Value;

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("true", claimValue);
    }

    private static StringContent AsJson(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private static async Task<JwtSecurityToken> ReadTokenAsync(HttpResponseMessage response)
    {
        // The endpoint returns the raw JWT; MVC may wrap a string result in quotes.
        var raw = (await response.Content.ReadAsStringAsync()).Trim('"');
        return new JwtSecurityTokenHandler().ReadJwtToken(raw);
    }
}
