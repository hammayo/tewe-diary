using Microsoft.AspNetCore.OutputCaching;
using Movies.Tests.Shared;
using NSubstitute;

namespace Movies.Api.Tests;

// One Postgres container + one API host shared by every endpoint test class in the "api"
// collection. Each test calls ResetAsync() first for an empty DB and a clean cache spy.
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();

    public MoviesApiFactory Factory { get; private set; } = null!;

    public IOutputCacheStore CacheStore => Factory.CacheStore;

    public async Task InitializeAsync()
    {
        await _postgres.InitializeAsync();
        // Program resolves the DB connection string eagerly (before WebApplicationFactory's
        // in-memory config is applied), so it must come from an environment variable the host
        // reads at startup — otherwise it falls back to the local .env/POSTGRES_* values.
        Environment.SetEnvironmentVariable("Database__ConnectionString", _postgres.ConnectionString);
        Environment.SetEnvironmentVariable("Database__MigrateOnStartup", "false");
        Factory = new MoviesApiFactory(_postgres.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        Factory.Dispose();
        await _postgres.DisposeAsync();
    }

    public async Task ResetAsync()
    {
        await _postgres.ResetAsync();
        CacheStore.ClearReceivedCalls();
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>;
