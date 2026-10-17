namespace Movies.Contracts.Responses;

public class TrailerResponse
{
    // YouTube or Vimeo.
    public required string Site { get; init; }

    public required string Key { get; init; }

    public string? Name { get; init; }

    // Watch page.
    public required string Url { get; init; }

    // For an <iframe> player.
    public required string EmbedUrl { get; init; }
}
