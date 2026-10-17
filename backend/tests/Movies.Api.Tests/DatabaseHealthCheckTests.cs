using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Movies.Api.Health;
using Movies.Application.Database;

namespace Movies.Api.Tests;

[Collection("api")]
public class DatabaseHealthCheckTests
{
    private readonly ApiFixture _fx;

    public DatabaseHealthCheckTests(ApiFixture fx) => _fx = fx;

    [Fact]
    public async Task reachable_database_is_healthy()
    {
        // Arrange
        var check = new DatabaseHealthCheck(
            new NpgsqlConnectionFactory(_fx.ConnectionString), NullLogger<DatabaseHealthCheck>.Instance);

        // Act
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task unreachable_database_is_unhealthy_and_carries_the_exception()
    {
        // Arrange
        // Port 1 refuses the connection straight away, so no container is needed for this case.
        var check = new DatabaseHealthCheck(
            new NpgsqlConnectionFactory("Host=localhost;Port=1;Database=none;Username=none;Password=none;Timeout=2"),
            NullLogger<DatabaseHealthCheck>.Instance);

        // Act
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Database is unhealthy", result.Description);
        Assert.NotNull(result.Exception);
    }
}
