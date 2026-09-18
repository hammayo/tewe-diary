using Bogus;
using Movies.Application.Models;
using Movies.Contracts.Requests;

namespace Movies.Tests.Shared;

// Central Bogus fakers so every test project builds domain models and request contracts
// the same way — no per-test hand-rolled sample data. Seeded per-faker for repeatable runs.
public static class TestData
{
    private const int Seed = 8675309;

    // Kept in range of MovieValidator (year <= current year) so generated movies are valid.
    private static int RandomYear(Faker f) => f.Random.Int(1950, DateTime.UtcNow.Year);

    public static readonly Faker<Movie> Movie = new Faker<Movie>()
        .UseSeed(Seed)
        .RuleFor(m => m.Id, _ => Guid.NewGuid())
        .RuleFor(m => m.Title, f => $"{f.Company.CatchPhrase()} {f.Random.Number(1000, 9999)}")
        .RuleFor(m => m.YearOfRelease, RandomYear)
        .RuleFor(m => m.Genres, f => f.Make(f.Random.Int(1, 3), () => f.Music.Genre()).Distinct().ToList());

    public static readonly Faker<CreateMovieRequest> CreateMovieRequest = new Faker<CreateMovieRequest>()
        .UseSeed(Seed)
        .RuleFor(m => m.Title, f => $"{f.Company.CatchPhrase()} {f.Random.Number(1000, 9999)}")
        .RuleFor(m => m.YearOfRelease, RandomYear)
        .RuleFor(m => m.Genres, f => f.Make(f.Random.Int(1, 3), () => f.Music.Genre()).Distinct().ToList());

    public static readonly Faker<UpdateMovieRequest> UpdateMovieRequest = new Faker<UpdateMovieRequest>()
        .UseSeed(Seed)
        .RuleFor(m => m.Title, f => $"{f.Company.CatchPhrase()} {f.Random.Number(1000, 9999)}")
        .RuleFor(m => m.YearOfRelease, RandomYear)
        .RuleFor(m => m.Genres, f => f.Make(f.Random.Int(1, 3), () => f.Music.Genre()).Distinct().ToList());

    public static readonly Faker<RateMovieRequest> RateMovieRequest = new Faker<RateMovieRequest>()
        .UseSeed(Seed)
        .RuleFor(m => m.Rating, f => f.Random.Int(1, 5));
}
