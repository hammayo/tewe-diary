
using System.Text.Json;
using Bogus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Movies.Api.Sdk;
using Movies.Api.Sdk.Consumer;
using Movies.Contracts.Requests;
using Refit;

// Load the repo-root .env (walking up from the working directory) so the API URLs
// are picked up as environment variables during local `dotnet run`. Harmless in
// environments where real environment variables are provided instead.
DotNetEnv.Env.TraversePath().Load();

var config = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .Build();

var moviesApiUrl = config["MOVIES_API_URL"] ?? "https://localhost:5001";

var services = new ServiceCollection();

services
    .AddSingleton<IConfiguration>(config)
    .AddHttpClient()
    .AddSingleton<AuthTokenProvider>()
    .AddRefitClient<IMoviesApi>(s => new RefitSettings
    {
        AuthorizationHeaderValueGetter = async (_, _) => await s.GetRequiredService<AuthTokenProvider>().GetTokenAsync()
    })
    .ConfigureHttpClient(x =>
        x.BaseAddress = new Uri(moviesApiUrl));

var provider = services.BuildServiceProvider();

var moviesApi = provider.GetRequiredService<IMoviesApi>();

// Bogus-generated sample movie so the demo doesn't depend on any seeded data.
var movieFaker = new Faker<CreateMovieRequest>()
    .RuleFor(m => m.Title, f => f.Company.CatchPhrase())
    .RuleFor(m => m.YearOfRelease, f => f.Random.Int(1950, DateTime.UtcNow.Year))
    .RuleFor(m => m.Genres, f => f.Make(f.Random.Int(1, 3), () => f.Music.Genre()).Distinct().ToList());

var newMovie = await moviesApi.CreateMovieAsync(movieFaker.Generate());

// Read it back via the slug the API generated for it.
var movie = await moviesApi.GetMovieAsync(newMovie.Slug);

await moviesApi.UpdateMovieAsync(newMovie.Id, new UpdateMovieRequest
{
    Title = movie.Title,
    YearOfRelease = movie.YearOfRelease,
    Genres = movie.Genres.Append(new Faker().Music.Genre()).Distinct()
});

await moviesApi.DeleteMovieAsync(newMovie.Id);

var request = new GetAllMoviesRequest
{
    Title = null,
    Year = null,
    SortBy = null,
    Page = 1,
    PageSize = 3
};

var movies = await moviesApi.GetMoviesAsync(request);

foreach (var movieResponse in movies.Items)
{
    Console.WriteLine(JsonSerializer.Serialize(movieResponse));
}

