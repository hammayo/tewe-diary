using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;

namespace Movies.Application.Database.Migrations;

// Single entry point for applying migrations. Used in-process at startup for local dev
// (gated by Database:MigrateOnStartup) and by the `dotnet-fm` CLI against this same assembly
// in the Azure pipeline — one set of migration classes, two ways to run them.
public static class MigrationRunner
{
    public static void Run(string connectionString)
    {
        using var serviceProvider = new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddPostgres()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(MigrationRunner).Assembly).For.Migrations())
            .AddLogging(lb => lb.AddFluentMigratorConsole())
            .BuildServiceProvider(validateScopes: false);

        using var scope = serviceProvider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
        runner.MigrateUp();
    }
}
