using FluentValidation;
using Movies.Application.Models;
using Movies.Application.Repositories;

namespace Movies.Application.Services;

public class MovieService : IMovieService
{
    private readonly IMovieRepository _movieRepository;
    private readonly IValidator<Movie> _movieValidator;
    private readonly IRatingRepository _ratingRepository;
    private readonly IValidator<GetAllMoviesOptions> _optionsValidator;
    private readonly IMovieDetailsRepository _movieDetailsRepository;

    public MovieService(IMovieRepository movieRepository, IValidator<Movie> movieValidator, IRatingRepository ratingRepository, IValidator<GetAllMoviesOptions> optionsValidator, IMovieDetailsRepository movieDetailsRepository)
    {
        _movieRepository = movieRepository;
        _movieValidator = movieValidator;
        _ratingRepository = ratingRepository;
        _optionsValidator = optionsValidator;
        _movieDetailsRepository = movieDetailsRepository;
    }

    public async Task<bool> CreateAsync(Movie movie, CancellationToken token = default)
    {
        await _movieValidator.ValidateAndThrowAsync(movie, cancellationToken: token);
        return await _movieRepository.CreateAsync(movie, token);
    }

    public async Task<Movie?> GetByIdAsync(Guid id, Guid? userid = default, CancellationToken token = default)
    {
        var movie = await _movieRepository.GetByIdAsync(id, userid, token);
        return await WithDetailsAsync(movie, token);
    }

    public async Task<Movie?> GetBySlugAsync(string slug, Guid? userid = default, CancellationToken token = default)
    {
        var movie = await _movieRepository.GetBySlugAsync(slug, userid, token);
        return await WithDetailsAsync(movie, token);
    }

    public async Task<IEnumerable<Movie>> GetAllAsync(GetAllMoviesOptions options, CancellationToken token = default)
    {
        await _optionsValidator.ValidateAndThrowAsync(options, token);
        
        return await _movieRepository.GetAllAsync(options, token);
    }

    public async Task<Movie?> UpdateAsync(Movie movie, Guid? userid = default, CancellationToken token = default)
    {
        await _movieValidator.ValidateAndThrowAsync(movie, cancellationToken: token);
        var movieExists = await _movieRepository.ExistsByIdAsync(movie.Id, token);
        if (!movieExists)
        {
            return null;
        }

        await _movieRepository.UpdateAsync(movie, token);

        if (!userid.HasValue)
        {
            var rating = await _ratingRepository.GetRatingAsync(movie.Id, token);
            movie.Rating = rating;
            return movie;
        }
        
        var ratings = await _ratingRepository.GetRatingAsync(movie.Id, userid.Value, token);
        movie.Rating = ratings.Rating;
        movie.UserRating = ratings.UserRating;
        return movie;
    }

    public Task<bool> DeleteByIdAsync(Guid id, CancellationToken token = default)
    {
        return _movieRepository.DeleteByIdAsync(id, token);
    }

    public Task<int> GetCountAsync(string? title, int? yearOfRelease, CancellationToken token = default)
    {
        return _movieRepository.GetCountAsync(title, yearOfRelease, token);
    }

    // Single-movie reads carry TMDB details (PRD FR-9). List, create and update deliberately don't,
    // which keeps the list free of an N+1 query.
    private async Task<Movie?> WithDetailsAsync(Movie? movie, CancellationToken token)
    {
        if (movie is not null)
        {
            movie.Details = await _movieDetailsRepository.GetByMovieIdAsync(movie.Id, token);
        }

        return movie;
    }
}
