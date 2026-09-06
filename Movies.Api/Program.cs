using System.Net.Sockets;
using Movies.Api.Mapping;
using Movies.Application;
using Movies.Application.Database;
using Npgsql;

// Load the repo-root .env (walking up from the working directory) so DB credentials
// are picked up as environment variables during local `dotnet run`. Harmless in
// environments where real environment variables are provided instead.
DotNetEnv.Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Prefer a full connection string if one is supplied, otherwise build it from the
// individual Postgres environment variables shared with docker-compose.
var connectionString = config["Database:ConnectionString"]
    ?? $"Server={config["POSTGRES_HOST"]};Port={config["POSTGRES_PORT"]};" +
       $"Database={config["POSTGRES_DB"]};User ID={config["POSTGRES_USER"]};" +
       $"Password={config["POSTGRES_PASSWORD"]};";

builder.Services.AddApplication();
builder.Services.AddDatabase(connectionString);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();

app.UseMiddleware<ValidationMappingMiddleware>();
app.MapControllers();

// Start listening first so the service (and Swagger) is always reachable, even if
// the database is still coming up. Database initialisation then runs with a short
// backoff: the container may lag behind its health check, or not be started at all.
await app.StartAsync();

var dbInitializer = app.Services.GetRequiredService<DbInitializer>();
var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();

const int maxAttempts = 10;
var delay = TimeSpan.FromSeconds(2);
for (var attempt = 1; attempt <= maxAttempts; attempt++)
{
    try
    {
        await dbInitializer.InitializeAsync();
        startupLogger.LogInformation("Database initialised.");
        break;
    }
    catch (NpgsqlException ex) when (ex.InnerException is SocketException)
    {
        if (attempt == maxAttempts)
        {
            startupLogger.LogError(
                "Database still not reachable after {MaxAttempts} attempts: {Message}. The service is running but database calls will fail until Postgres is available. Start it with: docker compose up -d",
                maxAttempts, ex.Message);
            break;
        }

        startupLogger.LogWarning(
            "Database not reachable (attempt {Attempt}/{MaxAttempts}): {Message}. Retrying in {Delay}s...",
            attempt, maxAttempts, ex.Message, delay.TotalSeconds);
        await Task.Delay(delay);
    }
}

await app.WaitForShutdownAsync();
