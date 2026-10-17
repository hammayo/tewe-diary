using Movies.Application.Models;

namespace Movies.Application.Services;

public interface IMovieService
{
    Task<bool> CreateAsync(Movie movie, CancellationToken token = default);
    
    Task<Movie?> GetByIdAsync(Guid id, Guid? userid = default, CancellationToken token = default);
    
    Task<Movie?> GetBySlugAsync(string slug, Guid? userid = default, CancellationToken token = default);
    
    // Resolves a GUID as an id and anything else as a slug, so v1 and v2 share one lookup rule.
    Task<Movie?> GetByIdOrSlugAsync(string idOrSlug, Guid? userid = default, CancellationToken token = default);
    
    Task<IEnumerable<Movie>> GetAllAsync(GetAllMoviesOptions options, CancellationToken token = default);
    
    Task<Movie?> UpdateAsync(Movie movie, Guid? userid = default, CancellationToken token = default);
    
    Task<bool> DeleteByIdAsync(Guid id, CancellationToken token = default);
    
    Task<int> GetCountAsync(string? title, int? yearOfRelease, CancellationToken token = default);
}
