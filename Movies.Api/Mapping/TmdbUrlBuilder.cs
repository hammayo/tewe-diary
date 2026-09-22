using Microsoft.Extensions.Options;

namespace Movies.Api.Mapping;

// Turns TMDB image paths and video keys stored in the database into absolute URLs. Image host and
// sizes are configuration (TmdbImageOptions); trailer URLs are fixed platform formats. Unknown
// trailer sites yield null so the caller can drop the trailer rather than emit a broken link.
public class TmdbUrlBuilder
{
    private readonly TmdbImageOptions _images;

    public TmdbUrlBuilder(IOptions<TmdbImageOptions> images)
    {
        _images = images.Value;
    }

    public string? Poster(string? path) => Image(_images.PosterSize, path);

    public string? Backdrop(string? path) => Image(_images.BackdropSize, path);

    public string? Profile(string? path) => Image(_images.ProfileSize, path);

    public string? TrailerUrl(string site, string key) => site switch
    {
        "YouTube" => $"https://www.youtube.com/watch?v={Uri.EscapeDataString(key)}",
        "Vimeo" => $"https://vimeo.com/{Uri.EscapeDataString(key)}",
        _ => null
    };

    public string? TrailerEmbedUrl(string site, string key) => site switch
    {
        "YouTube" => $"https://www.youtube.com/embed/{Uri.EscapeDataString(key)}",
        "Vimeo" => $"https://player.vimeo.com/video/{Uri.EscapeDataString(key)}",
        _ => null
    };

    private string? Image(string size, string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? null
            : $"{_images.BaseUrl.TrimEnd('/')}/{size.Trim('/')}/{path.TrimStart('/')}";
}
