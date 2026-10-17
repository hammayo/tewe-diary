using Movies.Application.Models;

namespace Movies.Application.Repositories;

public interface IMovieDetailsRepository
{
    // Null when the movie has no TMDB metadata (manual / API-created movies).
    Task<MovieDetails?> GetByMovieIdAsync(Guid movieId, CancellationToken token = default);
}
