namespace Movies.Application.Models;

// TMDB-sourced details of a movie (PRD FR-1…FR-8). Read-only: filled by the TMDB import, never by
// the API. Image fields are TMDB paths; the API layer turns them into URLs.
public class MovieDetails
{
    public required long TmdbId { get; init; }

    public string? ImdbId { get; init; }

    public string? Overview { get; init; }

    public string? Tagline { get; init; }

    public int? RuntimeMinutes { get; init; }

    public string? BackdropPath { get; init; }

    public MovieTrailer? Trailer { get; init; }

    public required IReadOnlyList<MovieCredit> Directors { get; init; }

    public required IReadOnlyList<MovieCredit> Writers { get; init; }

    public required IReadOnlyList<MovieCredit> Cast { get; init; }
}
