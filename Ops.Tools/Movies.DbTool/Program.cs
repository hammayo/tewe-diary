using System.Diagnostics;
using Dapper;
using Microsoft.Extensions.Configuration;
using Movies.Application.Database;
using Movies.Application.Database.Import;
using Movies.Application.Database.Migrations;

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
            Console.Error.WriteLine("Usage: import <path-to-ndjson> [more-ndjson...]");
            return 1;
        }

        var missing = paths.Where(p => !File.Exists(p)).ToList();
        if (missing.Count > 0)
        {
            Console.Error.WriteLine($"File(s) not found: {string.Join(", ", missing)}");
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
        Console.Error.WriteLine("Usage: Movies.DbTool <migrate | import <path-to-ndjson>>");
        return 1;
}

// Console output for MovieImporter progress, matching scripts/load-movies.sh. Staging redraws one line
// in place on a terminal, or prints every 10% when output is redirected (e.g. CI logs). Then it
// announces the transform, prints its NOTICE summary, and how long the transform took.
internal sealed class ConsoleImportProgress(int total, Stopwatch clock) : IProgress<ImportProgress>
{
    private readonly bool _interactive = !Console.IsOutputRedirected;
    private int _lastDecile = -1;
    private TimeSpan? _transformStarted;

    public void Report(ImportProgress value)
    {
        if (value.Notice is not null)
        {
            var took = clock.Elapsed - (_transformStarted ?? clock.Elapsed);
            Console.WriteLine($"  {value.Notice} (transform {Format(took)})");
            return;
        }

        if (value.Transforming)
        {
            if (_transformStarted is not null)
            {
                return;
            }

            _transformStarted = clock.Elapsed;
            if (_interactive)
            {
                Console.WriteLine();
            }

            Console.WriteLine("[2/3] Applying the upsert transform in one transaction (movies, genres, metadata, details, credits)...");
            return;
        }

        var percent = total == 0 ? 100 : value.Staged * 100 / total;
        var eta = value.Staged == 0 ? "--"
            : Format(TimeSpan.FromTicks(clock.Elapsed.Ticks * (total - value.Staged) / value.Staged));
        var line = $"  {value.Staged}/{total} ({percent}%) · {Format(clock.Elapsed)} elapsed · ETA {eta}";
        if (_interactive)
        {
            Console.Write($"\r{line}   ");
        }
        else if (percent / 10 > _lastDecile)
        {
            _lastDecile = percent / 10;
            Console.WriteLine(line);
        }
    }

    // 75s -> "1m15s", 3700s -> "1h01m40s" (same format as scripts/helpers/progress.sh).
    public static string Format(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h{t.Minutes:00}m{t.Seconds:00}s"
        : t.TotalMinutes >= 1 ? $"{t.Minutes}m{t.Seconds:00}s"
        : $"{t.Seconds}s";
}
