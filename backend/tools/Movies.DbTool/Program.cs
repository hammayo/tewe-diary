using System.Diagnostics;
using Dapper;
using Microsoft.Extensions.Configuration;
using Movies.Application.Database;
using Movies.Application.Database.Import;
using Movies.Application.Database.Migrations;
using Movies.DbTool;

// Ops CLI for the Azure deploy pipeline (and local use). Runs schema migrations and the TMDB
// import against the same connection resolution the API uses. In the pipeline the connection
// string / SSL come from env vars injected from Key Vault (Database__ConnectionString,
// Database__SslMode); the import step runs as the least-privilege movies_importer role.
//
//   dotnet Movies.DbTool.dll migrate
//   dotnet Movies.DbTool.dll import <path-to-ndjson>

// Load a local .env when present (walking up); harmless when real env vars are provided instead.
DotNetEnv.Env.TraversePath().Load();

var config = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .Build();

var connectionString = DatabaseConfiguration.Resolve(config).BuildConnectionString();

var command = args.FirstOrDefault();
switch (command)
{
    case "migrate":
        MigrationRunner.Run(connectionString);
        Console.WriteLine("Migrations applied.");
        return 0;

    case "import":
        // One or more NDJSON files: they are staged together, so a movie in two files is imported once
        // (the transform dedupes on tmdb_id) and movies already in the db are refreshed in place.
        var paths = args.Skip(1).ToList();
        if (paths.Count == 0)
        {
            await Console.Error.WriteLineAsync("Usage: import <path-to-ndjson> [more-ndjson...]");
            return 1;
        }

        var missing = paths.Where(p => !File.Exists(p)).ToList();
        if (missing.Count > 0)
        {
            await Console.Error.WriteLineAsync($"File(s) not found: {string.Join(", ", missing)}");
            return 1;
        }

        var clock = Stopwatch.StartNew();
        var lines = paths.SelectMany(File.ReadLines).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        var withDetails = lines.Count(l => l.Contains("\"credits\":"));
        Console.WriteLine($"[1/3] Staging {lines.Count} movies ({withDetails} with details) from {paths.Count} file(s)...");

        var factory = new NpgsqlConnectionFactory(connectionString);
        var staged = await new MovieImporter(factory).ImportAsync(lines, new ConsoleImportProgress(lines.Count, clock));

        using (var connection = await factory.CreateConnectionAsync())
        {
            var (tmdb, enriched) = await connection.QuerySingleAsync<(long, long)>("""
                select (select count(*) from movie_metadata),
                       (select count(*) from movie_metadata where raw ? 'credits')
                """);
            Console.WriteLine($"[3/3] TMDB movies with details: {enriched} / {tmdb}");
        }

        Console.WriteLine($"Imported {staged} line(s) from {string.Join(", ", paths)} in {ConsoleImportProgress.Format(clock.Elapsed)}.");
        return 0;

    default:
        await Console.Error.WriteLineAsync("Usage: Movies.DbTool <migrate | import <path-to-ndjson>>");
        return 1;
}
