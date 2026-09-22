using Microsoft.Extensions.Options;
using Movies.Api.Mapping;

namespace Movies.Api.Tests;

public class TmdbUrlBuilderTests
{
    private static readonly TmdbUrlBuilder DefaultBuilder = new(Options.Create(new TmdbImageOptions()));

    [Theory]
    [InlineData("poster", "/p.jpg", "https://image.tmdb.org/t/p/w500/p.jpg")]
    [InlineData("backdrop", "/b.jpg", "https://image.tmdb.org/t/p/w1280/b.jpg")]
    [InlineData("profile", "/f.jpg", "https://image.tmdb.org/t/p/w185/f.jpg")]
    [InlineData("poster", "p.jpg", "https://image.tmdb.org/t/p/w500/p.jpg")]
    [InlineData("poster", null, null)]
    [InlineData("poster", " ", null)]
    public void image_urls_use_the_default_base_and_size(string kind, string? path, string? expected)
    {
        // Arrange
        var builder = DefaultBuilder;

        // Act
        var actual = kind switch
        {
            "poster" => builder.Poster(path),
            "backdrop" => builder.Backdrop(path),
            _ => builder.Profile(path)
        };

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void image_urls_honour_configured_base_and_size()
    {
        // Arrange
        var builder = new TmdbUrlBuilder(Options.Create(new TmdbImageOptions
        {
            BaseUrl = "https://cdn.example.test/t/p",
            PosterSize = "w342"
        }));

        // Act
        var actual = builder.Poster("/p.jpg");

        // Assert
        Assert.Equal("https://cdn.example.test/t/p/w342/p.jpg", actual);
    }

    [Theory]
    [InlineData("YouTube", "Mzw2ttJD2qQ", "https://www.youtube.com/watch?v=Mzw2ttJD2qQ", "https://www.youtube.com/embed/Mzw2ttJD2qQ")]
    [InlineData("Vimeo", "12345", "https://vimeo.com/12345", "https://player.vimeo.com/video/12345")]
    [InlineData("Dailymotion", "x1", null, null)]
    public void trailer_urls_follow_the_site_format(string site, string key, string? expectedUrl, string? expectedEmbedUrl)
    {
        // Arrange
        var builder = DefaultBuilder;

        // Act
        var url = builder.TrailerUrl(site, key);
        var embedUrl = builder.TrailerEmbedUrl(site, key);

        // Assert
        Assert.Equal(expectedUrl, url);
        Assert.Equal(expectedEmbedUrl, embedUrl);
    }
}
