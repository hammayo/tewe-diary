using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Movies.Tests.Shared;
using NSubstitute;

namespace Movies.Api.Tests;

// Boots the real Movies.Api in-memory against a Testcontainers Postgres (schema already migrated
// by PostgresFixture, so the host itself does not migrate). The output-cache store is swapped for
// an NSubstitute spy so tests can assert the "movies" tag is evicted. JWT settings mirror TestJwt
// so minted tokens satisfy the Trusted/Admin policies.
public class MoviesApiFactory : WebApplicationFactory<Program>
{
    public const string TestApiKey = "test-api-key";

    private readonly string _connectionString;

    public MoviesApiFactory(string connectionString) => _connectionString = connectionString;

    // Records EvictByTagAsync calls; Get/Set return defaults so output caching is inert in tests.
    public IOutputCacheStore CacheStore { get; } = Substitute.For<IOutputCacheStore>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JWT_TOKEN_SECRET"] = TestJwt.Secret,
                ["JWT_ISSUER"] = TestJwt.Issuer,
                ["JWT_AUDIENCE"] = TestJwt.Audience,
                ["API_KEY"] = TestApiKey,
                ["Database:MigrateOnStartup"] = "false",
                ["Database:ConnectionString"] = _connectionString,
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IOutputCacheStore>();
            services.AddSingleton(CacheStore);
        });
    }

    public HttpClient CreateAnonymousClient() => CreateClient();

    public HttpClient CreateAuthenticatedClient(Guid? userId = null, bool trusted = false, bool admin = false)
    {
        var client = CreateClient();
        var token = TestJwt.Create(userId ?? Guid.NewGuid(), trusted, admin);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public HttpClient CreateApiKeyClient(string? apiKey = TestApiKey)
    {
        var client = CreateClient();
        if (apiKey is not null)
        {
            client.DefaultRequestHeaders.Add("x-api-key", apiKey);
        }

        return client;
    }
}
