using Microsoft.Extensions.Configuration;
using Movies.Application.Database;
using Npgsql;

namespace Movies.Application.Tests;

// Pure logic — no database required.
public class DatabaseOptionsTests
{
    [Fact]
    public void BuildConnectionString_returns_a_full_connection_string_verbatim()
    {
        var options = new DatabaseOptions
        {
            ConnectionString = "Host=db;Database=movies;Username=u;Password=p;SSL Mode=Require"
        };

        Assert.Equal("Host=db;Database=movies;Username=u;Password=p;SSL Mode=Require",
            options.BuildConnectionString());
    }

    [Fact]
    public void BuildConnectionString_builds_from_parts_and_applies_ssl_mode()
    {
        var options = new DatabaseOptions
        {
            Host = "db", Port = "5432", Name = "movies", User = "u", Password = "p",
            SslMode = "Require"
        };

        var built = new NpgsqlConnectionStringBuilder(options.BuildConnectionString());

        Assert.Equal("db", built.Host);
        Assert.Equal("movies", built.Database);
        Assert.Equal("u", built.Username);
        Assert.Equal(SslMode.Require, built.SslMode);
    }

    [Fact]
    public void Resolve_falls_back_parts_to_postgres_env_keys()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["POSTGRES_HOST"] = "h",
            ["POSTGRES_PORT"] = "5432",
            ["POSTGRES_DB"] = "movies",
            ["POSTGRES_USER"] = "u",
            ["POSTGRES_PASSWORD"] = "p",
            ["Database:SslMode"] = "Require",
            ["Database:MigrateOnStartup"] = "false"
        }).Build();

        var options = DatabaseConfiguration.Resolve(config);

        Assert.Equal("h", options.Host);
        Assert.Equal("Require", options.SslMode);
        Assert.False(options.MigrateOnStartup);
        Assert.Equal(SslMode.Require,
            new NpgsqlConnectionStringBuilder(options.BuildConnectionString()).SslMode);
    }

    [Fact]
    public void Resolve_prefers_an_explicit_database_connection_string()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:ConnectionString"] = "Host=explicit;Database=movies;Username=u;Password=p",
            ["POSTGRES_HOST"] = "ignored"
        }).Build();

        var options = DatabaseConfiguration.Resolve(config);

        Assert.Equal("Host=explicit;Database=movies;Username=u;Password=p",
            options.BuildConnectionString());
    }
}
