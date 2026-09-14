using System.Net.Sockets;
using System.Text;
using Asp.Versioning;
using Azure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Movies.Api;
using Movies.Api.Auth;
using Movies.Api.Health;
using Movies.Api.Mapping;
using Movies.Api.Swagger;
using Movies.Application;
using Movies.Application.Database;
using Movies.Application.Database.Migrations;
using Npgsql;
using Swashbuckle.AspNetCore.SwaggerGen;

// Load the repo-root .env (walking up from the working directory) so DB credentials
// are picked up as environment variables during local `dotnet run`. Harmless in
// environments where real environment variables are provided instead.
DotNetEnv.Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// In Azure, secrets (DB connection string, JWT secret, API key) come from Key Vault via the
// app's managed identity. Locally KeyVault:Uri is unset, so this is skipped and .env / env vars
// are used instead. Secret names map to config keys with `--` → `:` (e.g. Database--ConnectionString).
var keyVaultUri = config["KeyVault:Uri"];
if (!string.IsNullOrWhiteSpace(keyVaultUri))
{
    builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUri), new DefaultAzureCredential());
}

builder.Services.AddAuthentication(x =>
{
    x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    x.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(x =>
{
    var tokenSecret = config["JWT_TOKEN_SECRET"]
                      ?? throw new InvalidOperationException("JWT_TOKEN_SECRET is not configured.");

    x.TokenValidationParameters = new TokenValidationParameters
    {
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(tokenSecret)),
        ValidateIssuerSigningKey = true,
        ValidateLifetime = true,
        ValidIssuer = config["JWT_ISSUER"],
        ValidAudience = config["JWT_AUDIENCE"],
        ValidateIssuer = true,
        ValidateAudience = true
    };
});

builder.Services.AddAuthorization(x =>
{
    x.AddPolicy(AuthConstants.AdminUserPolicyName,
        p => p.RequireClaim(AuthConstants.AdminUserClaimName, "true"));
    
    x.AddPolicy(AuthConstants.TrustedMemberPolicyName,
        p => p.RequireAssertion(c => 
            c.User.HasClaim(m => m is { Type: AuthConstants.AdminUserClaimName, Value: "true" }) || 
            c.User.HasClaim(m => m is { Type: AuthConstants.TrustedMemberClaimName, Value: "true" })));
});

builder.Services.AddScoped<ApiKeyAuthFilter>();

builder.Services.AddApiVersioning(x =>
{
    x.DefaultApiVersion = new ApiVersion(1.0);
    x.AssumeDefaultVersionWhenUnspecified = true;
    x.ReportApiVersions = true;
    x.ApiVersionReader = new MediaTypeApiVersionReader("api-version");
}).AddMvc().AddApiExplorer();

//builder.Services.AddResponseCaching();
builder.Services.AddOutputCache(x =>
{
    x.AddBasePolicy(c => c.Cache());
    x.AddPolicy("MovieCache", c => 
        c.Cache()
        .Expire(TimeSpan.FromMinutes(1))
        .SetVaryByQuery(new[] { "title", "year", "sortBy", "page", "pageSize" })
        .Tag("movies"));
});

builder.Services.AddControllers();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>(DatabaseHealthCheck.Name);

builder.Services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>();
builder.Services.AddSwaggerGen(x => x.OperationFilter<SwaggerDefaultValues>());

// Connection details + SSL are resolved through DatabaseOptions. A full connection string
// (e.g. from Key Vault) wins; otherwise it's built from the POSTGRES_* vars shared with
// docker-compose, with SSL applied for managed Postgres.
var databaseOptions = config.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()
                      ?? new DatabaseOptions();
databaseOptions.Host ??= config["POSTGRES_HOST"];
databaseOptions.Port ??= config["POSTGRES_PORT"];
databaseOptions.Name ??= config["POSTGRES_DB"];
databaseOptions.User ??= config["POSTGRES_USER"];
databaseOptions.Password ??= config["POSTGRES_PASSWORD"];

builder.Services.Configure<DatabaseOptions>(config.GetSection(DatabaseOptions.SectionName));

var connectionString = databaseOptions.BuildConnectionString();

builder.Services.AddApplication();
builder.Services.AddDatabase(connectionString);

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(x =>
    {
        foreach (var description in app.DescribeApiVersions())
        {
            x.SwaggerEndpoint( $"/swagger/{description.GroupName}/swagger.json",
                description.GroupName);
        }
    });
}

app.MapHealthChecks("_health");

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

//app.UseCors();
//app.UseResponseCaching();
app.UseOutputCache();

app.UseMiddleware<ValidationMappingMiddleware>();
app.MapControllers();

// Start listening first, so the service (and Swagger) is always reachable, even if
// the database is still coming up. Migration then runs with a short backoff: the
// container may lag behind its health check or not be started at all.
await app.StartAsync();

var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();

// In Azure the deploy pipeline applies migrations with an elevated role before the new
// version goes live, so the web process must not self-migrate (set Database:MigrateOnStartup
// to false there). Locally this defaults on for zero-friction `dotnet run` / `./run.sh`.
if (databaseOptions.MigrateOnStartup)
{
    const int maxAttempts = 10;
    var delay = TimeSpan.FromSeconds(2);
    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            MigrationRunner.Run(connectionString);
            startupLogger.LogInformation("Database migrations applied.");
            break;
        }
        catch (Exception ex) when (IsDatabaseUnavailable(ex))
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
}

await app.WaitForShutdownAsync();

// FluentMigrator wraps the underlying Npgsql/socket failure it hits when Postgres isn't up
// yet, so matching the top-level type isn't enough — walk the inner-exception chain.
static bool IsDatabaseUnavailable(Exception ex)
{
    for (Exception? e = ex; e is not null; e = e.InnerException)
    {
        if (e is SocketException)
        {
            return true;
        }
    }

    return false;
}

// Exposed so the integration test host (WebApplicationFactory<Program>) can boot the app.
public partial class Program;
