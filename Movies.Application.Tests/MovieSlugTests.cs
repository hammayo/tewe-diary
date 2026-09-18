using Movies.Application.Models;

namespace Movies.Application.Tests;

// Pure unit tests for Movie.Slug generation — no database, so no [Collection("postgres")].
public class MovieSlugTests
{
    [Theory]
    [InlineData("Toy Story", 1995, "toy-story-1995")]
    [InlineData("Déjà Vu", 1993, "dj-vu-1993")]        // accented letters stripped
    [InlineData("Alien³", 1992, "alien-1992")]          // superscript stripped
    [InlineData("Debtor’s Wife", 2020, "debtors-wife-2020")] // curly apostrophe stripped
    public void slug_strips_non_ascii_characters(string title, int year, string expected)
    {
        // Arrange
        var movie = new Movie { Id = Guid.NewGuid(), Title = title, YearOfRelease = year, Genres = [] };

        // Act
        var slug = movie.Slug;

        // Assert
        Assert.Equal(expected, slug);
    }

    [Fact]
    public void slug_generation_survives_many_non_ascii_titles()
    {
        // Arrange
        // Regression for the get-all 500: computing slugs for many non-ASCII titles in a
        // tight loop (as serializing a large movie list does) must not throw
        // RegexMatchTimeoutException.
        string[] titles =
        {
            "刘德华 热浪劲爆音乐会", "母女汽车中心", "Déjà Vu", "Alien³",
            "Chompoo: Lost & Forgotten", "Ginza no jirochō", "Debtor’s Wife",
        };

        // Act & Assert (must not throw)
        for (var i = 0; i < 500; i++)
        {
            foreach (var title in titles)
            {
                _ = new Movie { Id = Guid.NewGuid(), Title = title, YearOfRelease = 2024, Genres = [] }.Slug;
            }
        }
    }
}
