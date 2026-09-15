using Movies.Api.Startup;

// Load the repo-root .env (walking up from the working directory) so DB credentials
// are picked up as environment variables during local `dotnet run`. Harmless in
// environments where real environment variables are provided instead.
DotNetEnv.Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);
builder.AddApiServices();

var app = builder.Build();
app.UseApiPipeline();

// Start listening first, so the service (and Swagger) is always reachable, even if the database
// is still coming up. MigrateDatabaseAsync then runs with a short backoff (a no-op when
// Database:MigrateOnStartup is false, as in Azure where the pipeline migrates instead).
await app.StartAsync();
await app.MigrateDatabaseAsync();
await app.WaitForShutdownAsync();

// Exposed so the integration test host (WebApplicationFactory<Program>) can boot the app.
public partial class Program;
