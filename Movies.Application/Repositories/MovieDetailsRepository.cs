using Dapper;
using Movies.Application.Database;
using Movies.Application.Models;

namespace Movies.Application.Repositories;

// Read-only access to a movie's TMDB details. Two queries (details, then credits), mirroring
// MovieRepository.GetByIdAsync + genres. Postgres folds the column aliases to lower case and
// Dapper matches them to the row properties case-insensitively.
public class MovieDetailsRepository : IMovieDetailsRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public MovieDetailsRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<MovieDetails?> GetByMovieIdAsync(Guid movieId, CancellationToken token = default)
    {
        using var connection = await _dbConnectionFactory.CreateConnectionAsync(token);
        var row = await connection.QuerySingleOrDefaultAsync<DetailsRow>(new CommandDefinition("""
            select mm.tmdb_id         as TmdbId,
                   mm.imdb_id         as ImdbId,
                   md.overview        as Overview,
                   md.tagline         as Tagline,
                   md.runtime_minutes as RuntimeMinutes,
                   md.backdrop_path   as BackdropPath,
                   md.trailer_site    as TrailerSite,
                   md.trailer_key     as TrailerKey,
                   md.trailer_name    as TrailerName
            from movie_metadata mm
            left join movie_details md on md.movieid = mm.movieid
            where mm.movieid = @movieId
            """, new { movieId }, cancellationToken: token));

        if (row is null)
        {
            return null;
        }

        var credits = (await connection.QueryAsync<CreditRow>(new CommandDefinition("""
            select credit_type    as CreditType,
                   tmdb_person_id as TmdbPersonId,
                   name           as Name,
                   role           as Role,
                   profile_path   as ProfilePath
            from movie_credits
            where movieid = @movieId
            order by credit_type, ordinal
            """, new { movieId }, cancellationToken: token))).ToList();

        return new MovieDetails
        {
            TmdbId = row.TmdbId,
            ImdbId = row.ImdbId,
            Overview = row.Overview,
            Tagline = row.Tagline,
            RuntimeMinutes = row.RuntimeMinutes,
            BackdropPath = row.BackdropPath,
            Trailer = row is { TrailerSite: not null, TrailerKey: not null }
                ? new MovieTrailer(row.TrailerSite, row.TrailerKey, row.TrailerName)
                : null,
            Directors = CreditsOfType(credits, "director"),
            Writers = CreditsOfType(credits, "writer"),
            Cast = CreditsOfType(credits, "cast")
        };
    }

    private static IReadOnlyList<MovieCredit> CreditsOfType(IEnumerable<CreditRow> credits, string creditType) =>
        credits.Where(c => c.CreditType == creditType)
            .Select(c => new MovieCredit(c.TmdbPersonId, c.Name, c.Role, c.ProfilePath))
            .ToList();

    private sealed class DetailsRow
    {
        public long TmdbId { get; init; }
        public string? ImdbId { get; init; }
        public string? Overview { get; init; }
        public string? Tagline { get; init; }
        public int? RuntimeMinutes { get; init; }
        public string? BackdropPath { get; init; }
        public string? TrailerSite { get; init; }
        public string? TrailerKey { get; init; }
        public string? TrailerName { get; init; }
    }

    private sealed class CreditRow
    {
        public string CreditType { get; init; } = string.Empty;
        public long TmdbPersonId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? Role { get; init; }
        public string? ProfilePath { get; init; }
    }
}
