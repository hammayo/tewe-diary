namespace Movies.Api.Mapping;

// Bound from "Tmdb:Images". Defaults are TMDB's documented values (GET /3/configuration), so the API
// works with no configuration; override to change the image host or sizes without a code change.
public class TmdbImageOptions
{
    public const string SectionName = "Tmdb:Images";

    public string BaseUrl { get; set; } = "https://image.tmdb.org/t/p/";

    public string PosterSize { get; set; } = "w500";

    public string BackdropSize { get; set; } = "w1280";

    public string ProfileSize { get; set; } = "w185";
}
