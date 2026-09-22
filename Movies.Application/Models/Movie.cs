using System.Text.RegularExpressions;

namespace Movies.Application.Models;

public partial class Movie
{
    public required Guid Id { get; init; }
    
    public required string Title { get; set; }

    public string Slug => GenerateSlug();
    
    public float? Rating { get; set; }
    
    public int? UserRating { get; set; }

    public required int YearOfRelease { get; set; }
    
    public required List<string> Genres { get; init; } = new();

    // TMDB poster path (movie_details.poster_path); null for manual movies. Filled by every read.
    public string? PosterPath { get; set; }

    // TMDB details; loaded only on single-movie reads (MovieService.GetByIdAsync / GetBySlugAsync).
    public MovieDetails? Details { get; set; }

    private string GenerateSlug()
    {
        var sluggedTitle = SlugRegex().Replace(Title, string.Empty)
            .ToLower().Replace(" ", "-");
        return $"{sluggedTitle}-{YearOfRelease}";
    }

    // NonBacktracking is linear-time, so no match timeout is needed (and a tight one
    // spuriously throws RegexMatchTimeoutException on Unicode-heavy titles under load).
    [GeneratedRegex("[^0-9A-Za-z _-]", RegexOptions.NonBacktracking)]
    private static partial Regex SlugRegex();
}
