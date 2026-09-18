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
        var path = args.ElementAtOrDefault(1);
        if (string.IsNullOrWhiteSpace(path))
        {
            Console.Error.WriteLine("Usage: import <path-to-ndjson>");
            return 1;
        }

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"File not found: {path}");
            return 1;
        }

        var importer = new MovieImporter(new NpgsqlConnectionFactory(connectionString));
        var staged = await importer.ImportFileAsync(path);
        Console.WriteLine($"Imported {staged} line(s) from {path}.");
        return 0;

    default:
        Console.Error.WriteLine("Usage: Movies.DbTool <migrate | import <path-to-ndjson>>");
        return 1;
}
