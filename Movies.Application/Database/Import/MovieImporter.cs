using Dapper;

namespace Movies.Application.Database.Import;

// Loads TMDB NDJSON into the database and applies the upsert transform, all in one transaction.
// This is the Azure-native load path (no psql dependency): it reuses the app's connection
// factory, so TLS/secret handling is identical to the running service. Ratings are never
// touched, and movies without a movie_metadata row (manual / API-created) are left alone.
public class MovieImporter
{
    // Embedded copy of scripts/helpers/import-transform.sql (see Movies.Application.csproj).
    private static readonly string TransformSql = LoadTransformSql();

    private readonly IDbConnectionFactory _connectionFactory;

    public MovieImporter(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    // Imports every non-blank NDJSON line. Returns the number of lines staged.
    public async Task<int> ImportAsync(IEnumerable<string> ndjsonLines, CancellationToken token = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(token);
        using var transaction = connection.BeginTransaction();

        await connection.ExecuteAsync(new CommandDefinition(
            "create temp table _import (doc jsonb) on commit drop;",
            transaction: transaction, cancellationToken: token));

        var staged = 0;
        foreach (var line in ndjsonLines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "insert into _import (doc) values (@doc::jsonb);",
                new { doc = line }, transaction, cancellationToken: token));
            staged++;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            TransformSql, transaction: transaction, cancellationToken: token));

        transaction.Commit();
        return staged;
    }

    // Convenience for the pipeline / CLI: import from an NDJSON file on disk.
    public Task<int> ImportFileAsync(string path, CancellationToken token = default) =>
        ImportAsync(File.ReadLines(path), token);

    private static string LoadTransformSql()
    {
        var assembly = typeof(MovieImporter).Assembly;
        const string resourceName = "Movies.Application.import-transform.sql";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
