namespace Movies.Contracts.Responses;

public class CreditResponse
{
    public required long TmdbPersonId { get; init; }

    public required string Name { get; init; }

    // Character (cast) or job (writer); null for directors.
    public string? Role { get; init; }

    public string? ProfileUrl { get; init; }
}
