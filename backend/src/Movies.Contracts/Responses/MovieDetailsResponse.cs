namespace Movies.Contracts.Responses;

// TMDB-sourced details; present on GET /movies/{idOrSlug} for TMDB movies, null otherwise.
public class MovieDetailsResponse
{
    public required long TmdbId { get; init; }

    public string? ImdbId { get; init; }

    public string? Overview { get; init; }

    public string? Tagline { get; init; }

    public int? RuntimeMinutes { get; init; }

    public string? BackdropUrl { get; init; }

    public TrailerResponse? Trailer { get; init; }

    public required IEnumerable<CreditResponse> Directors { get; init; } = Enumerable.Empty<CreditResponse>();

    public required IEnumerable<CreditResponse> Writers { get; init; } = Enumerable.Empty<CreditResponse>();

    public required IEnumerable<CreditResponse> Cast { get; init; } = Enumerable.Empty<CreditResponse>();
}
