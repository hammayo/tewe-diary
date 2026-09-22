using Movies.Application.Models;
using Movies.Contracts.Requests;
using Movies.Contracts.Responses;

namespace Movies.Api.Mapping;

public static class ContractMapping
{
    public static Movie MapToMovie(this CreateMovieRequest request)
    {
        return new Movie
        {
            Id = Guid.NewGuid(),
            Title = request.Title,
            YearOfRelease = request.YearOfRelease,
            Genres = request.Genres.ToList()
        };
    }
    
    public static Movie MapToMovie(this UpdateMovieRequest request, Guid id)
    {
        return new Movie
        {
            Id = id,
            Title = request.Title,
            YearOfRelease = request.YearOfRelease,
            Genres = request.Genres.ToList()
        };
    }

    public static MovieResponse MapToResponse(this Movie movie, TmdbUrlBuilder urls)
    {
        return new MovieResponse
        {
            Id = movie.Id,
            Title = movie.Title,
            Slug = movie.Slug,
            Rating = movie.Rating,
            UserRating = movie.UserRating,
            YearOfRelease = movie.YearOfRelease,
            Genres = movie.Genres,
            PosterUrl = urls.Poster(movie.PosterPath),
            Details = movie.Details?.MapToResponse(urls)
        };
    }

    public static MoviesResponse MapToResponse(this IEnumerable<Movie> movies, TmdbUrlBuilder urls,
        int page, int pageSize, int totalCount)
    {
        return new MoviesResponse
        {
            Items = movies.Select(movie => movie.MapToResponse(urls)),
            Page = page,
            PageSize = pageSize,
            Total = totalCount
        };
    }

    private static MovieDetailsResponse MapToResponse(this MovieDetails details, TmdbUrlBuilder urls)
    {
        return new MovieDetailsResponse
        {
            TmdbId = details.TmdbId,
            ImdbId = details.ImdbId,
            Overview = details.Overview,
            Tagline = details.Tagline,
            RuntimeMinutes = details.RuntimeMinutes,
            BackdropUrl = urls.Backdrop(details.BackdropPath),
            Trailer = details.Trailer?.MapToResponse(urls),
            Directors = details.Directors.Select(credit => credit.MapToResponse(urls)).ToList(),
            Writers = details.Writers.Select(credit => credit.MapToResponse(urls)).ToList(),
            Cast = details.Cast.Select(credit => credit.MapToResponse(urls)).ToList()
        };
    }

    // Null when the site has no known URL format, so clients never receive a broken player link.
    private static TrailerResponse? MapToResponse(this MovieTrailer trailer, TmdbUrlBuilder urls)
    {
        var url = urls.TrailerUrl(trailer.Site, trailer.Key);
        var embedUrl = urls.TrailerEmbedUrl(trailer.Site, trailer.Key);
        if (url is null || embedUrl is null)
        {
            return null;
        }

        return new TrailerResponse
        {
            Site = trailer.Site,
            Key = trailer.Key,
            Name = trailer.Name,
            Url = url,
            EmbedUrl = embedUrl
        };
    }

    private static CreditResponse MapToResponse(this MovieCredit credit, TmdbUrlBuilder urls)
    {
        return new CreditResponse
        {
            TmdbPersonId = credit.TmdbPersonId,
            Name = credit.Name,
            Role = credit.Role,
            ProfileUrl = urls.Profile(credit.ProfilePath)
        };
    }
    
    public static IEnumerable<MovieRatingResponse> MapToResponse(this IEnumerable<MovieRating> ratings)
    {
        return ratings.Select(x => new MovieRatingResponse
        {
            Rating = x.Rating,
            Slug = x.Slug,
            MovieId = x.MovieId
        });
    }
    
    public static GetAllMoviesOptions MapToOptions(this GetAllMoviesRequest request)
    {
        // Default to newest-first when no sort is supplied.
        const string defaultSortBy = "-yearOfRelease";
        var sortBy = string.IsNullOrWhiteSpace(request.SortBy) ? defaultSortBy : request.SortBy;
        return new GetAllMoviesOptions
        {
            Title = request.Title,
            YearOfRelease = request.Year,
            SortField = sortBy.Trim('+', '-'),
            SortOrder = sortBy.StartsWith('-') ? SortOrder.Descending : SortOrder.Ascending,
            Page = request.Page.GetValueOrDefault(PagedRequest.DefaultPage),
            PageSize = request.PageSize.GetValueOrDefault(PagedRequest.DefaultPageSize)
        };
    }
    
    public static GetAllMoviesOptions WithUser(this GetAllMoviesOptions options,
        Guid? userId)
    {
        options.UserId = userId;
        return options;
    }
}
