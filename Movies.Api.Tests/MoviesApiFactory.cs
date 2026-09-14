using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Movies.Api.Tests;

// Boots the real API in-memory. Supplies enough config to start without a database
// (MigrateOnStartup=false) and swaps in a spy output-cache store so the eviction endpoint's
// effect can be asserted. The admin endpoint under test touches neither the DB nor JWT auth.
public class MoviesApiFactory : WebApplicationFactory<Program>
{
    public const string TestApiKey = "test-api-key";

    public SpyOutputCacheStore CacheStore { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JWT_TOKEN_SECRET"] = "integration-test-secret-value-long-enough-for-hs256-000",
                ["JWT_ISSUER"] = "https://test.local",
                ["JWT_AUDIENCE"] = "https://test.local",
                ["API_KEY"] = TestApiKey,
                ["Database:MigrateOnStartup"] = "false",
                ["Database:ConnectionString"] = "Host=localhost;Database=none;Username=none;Password=none",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IOutputCacheStore>();
            services.AddSingleton<IOutputCacheStore>(CacheStore);
        });
    }
}
