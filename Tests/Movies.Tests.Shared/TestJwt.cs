using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Movies.Tests.Shared;

// Mints JWTs that Movies.Api's JwtBearer setup will accept, so integration tests can exercise
// the [Authorize] policies (Trusted / Admin) without standing up Identity.Api. The secret,
// issuer and audience must match what the test host is configured with.
public static class TestJwt
{
    // Long enough for HS256; the test host is configured with this same value.
    public const string Secret = "integration-test-secret-value-long-enough-for-hs256-000";
    public const string Issuer = "https://test.local";
    public const string Audience = "https://test.local";

    public static string Create(Guid userId, bool trusted = false, bool admin = false)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Sub, $"{userId}@test.local"),
            new(JwtRegisteredClaimNames.Email, $"{userId}@test.local"),
            new("userid", userId.ToString())
        };

        if (trusted)
        {
            claims.Add(new Claim("trusted_member", "true"));
        }

        if (admin)
        {
            claims.Add(new Claim("admin", "true"));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret));
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
