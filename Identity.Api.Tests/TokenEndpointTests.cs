using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Identity.Api.Tests;

public class TokenEndpointTests : IClassFixture<IdentityApiFactory>
{
    private const string TokenUrl = "/token";

    private readonly IdentityApiFactory _factory;

    public TokenEndpointTests(IdentityApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Token_is_issued_with_the_standard_claims()
    {
        var client = _factory.CreateClient();
        var userId = Guid.NewGuid();
        var request = new TokenGenerationRequest { UserId = userId, Email = "user@test.local" };

        var response = await client.PostAsync(TokenUrl, AsJson(request));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var jwt = await ReadTokenAsync(response);
        Assert.Equal(IdentityApiFactory.Issuer, jwt.Issuer);
        Assert.Contains(jwt.Audiences, a => a == IdentityApiFactory.Audience);
        Assert.Equal("user@test.local", jwt.Claims.Single(c => c.Type == "email").Value);
        Assert.Equal(userId.ToString(), jwt.Claims.Single(c => c.Type == "userid").Value);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("trusted_member")]
    public async Task Token_includes_custom_boolean_claims(string claimName)
    {
        var client = _factory.CreateClient();
        var request = new TokenGenerationRequest
        {
            UserId = Guid.NewGuid(),
            Email = "member@test.local",
            CustomClaims = new Dictionary<string, object> { [claimName] = true }
        };

        var response = await client.PostAsync(TokenUrl, AsJson(request));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var jwt = await ReadTokenAsync(response);
        Assert.Equal("true", jwt.Claims.Single(c => c.Type == claimName).Value);
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
