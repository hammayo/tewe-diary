
using System.Text.Json;
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

var movie = await moviesApi.GetMovieAsync("star-wars-1977");

var newMovie = await moviesApi.CreateMovieAsync(new CreateMovieRequest
{
    Title = "Spiderman 2",
    YearOfRelease = 2002,
    Genres = new []{ "Action"}
});

await moviesApi.UpdateMovieAsync(newMovie.Id, new UpdateMovieRequest()
{
    Title = "Spiderman 2",
    YearOfRelease = 2002,
    Genres = new []{ "Action", "Adventure"}
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

