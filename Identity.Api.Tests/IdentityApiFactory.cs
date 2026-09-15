using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Identity.Api.Tests;

// Boots the real Identity.Api in-memory with a known signing secret/issuer/audience so tests can
// verify the JWTs it issues. The token endpoint has no database or auth dependency.
public class IdentityApiFactory : WebApplicationFactory<Program>
{
    public const string Secret = "identity-test-secret-value-long-enough-for-hs256-00000";
    public const string Issuer = "https://identity.test.local";
    public const string Audience = "https://movies.test.local";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JWT_TOKEN_SECRET"] = Secret,
                ["JWT_ISSUER"] = Issuer,
                ["JWT_AUDIENCE"] = Audience,
            });
        });
    }
}
