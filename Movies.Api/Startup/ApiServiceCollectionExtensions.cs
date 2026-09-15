using System.Text;
using Asp.Versioning;
using Azure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Movies.Api.Auth;
using Movies.Api.Health;
using Movies.Api.Startup;
using Movies.Api.Swagger;
using Movies.Application;
using Movies.Application.Database;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Movies.Api.Startup;

// Composition root for Movies.Api. Program.cs stays a thin bootstrap; each concern (auth,
// versioning, caching, docs, persistence, infrastructure) registers itself here.
public static class ApiServiceCollectionExtensions
{
    public static WebApplicationBuilder AddApiServices(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;

        // In Azure, secrets (DB connection string, JWT secret, API key) come from Key Vault via the
        // app's managed identity. Locally KeyVault:Uri is unset, so this is skipped and .env / env
        // vars are used instead. Secret names map to config keys with `--` → `:`.
        var keyVaultUri = config["KeyVault:Uri"];
        if (!string.IsNullOrWhiteSpace(keyVaultUri))
        {
            config.AddAzureKeyVault(new Uri(keyVaultUri), new DefaultAzureCredential());
        }

        builder.Services
            .AddApiAuthentication(config)
            .AddApiAuthorization()
            .AddApiVersioningInternal()
            .AddApiCaching()
            .AddApiDocumentation()
            .AddApiInfrastructure()
            .AddApiPersistence(config);

        return builder;
    }

    private static IServiceCollection AddApiAuthentication(this IServiceCollection services, IConfiguration config)
    {
        services.AddAuthentication(x =>
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

        return services;
    }

    private static IServiceCollection AddApiAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(x =>
        {
            x.AddPolicy(AuthConstants.AdminUserPolicyName,
                p => p.RequireClaim(AuthConstants.AdminUserClaimName, "true"));

            x.AddPolicy(AuthConstants.TrustedMemberPolicyName,
                p => p.RequireAssertion(c =>
                    c.User.HasClaim(m => m is { Type: AuthConstants.AdminUserClaimName, Value: "true" }) ||
                    c.User.HasClaim(m => m is { Type: AuthConstants.TrustedMemberClaimName, Value: "true" })));
        });

        services.AddScoped<ApiKeyAuthFilter>();

        return services;
    }

    private static IServiceCollection AddApiVersioningInternal(this IServiceCollection services)
    {
        services.AddApiVersioning(x =>
        {
            x.DefaultApiVersion = new ApiVersion(1.0);
            x.AssumeDefaultVersionWhenUnspecified = true;
            x.ReportApiVersions = true;
            x.ApiVersionReader = new MediaTypeApiVersionReader("api-version");
        }).AddMvc().AddApiExplorer();

        return services;
    }

    private static IServiceCollection AddApiCaching(this IServiceCollection services)
    {
        services.AddOutputCache(x =>
        {
            x.AddBasePolicy(c => c.Cache());
            x.AddPolicy("MovieCache", c =>
                c.Cache()
                    .Expire(TimeSpan.FromMinutes(1))
                    .SetVaryByQuery(new[] { "title", "year", "sortBy", "page", "pageSize" })
                    .Tag("movies"));
        });

        return services;
    }

    private static IServiceCollection AddApiDocumentation(this IServiceCollection services)
    {
        services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>();
        services.AddSwaggerGen(x => x.OperationFilter<SwaggerDefaultValues>());

        return services;
    }

    private static IServiceCollection AddApiInfrastructure(this IServiceCollection services)
    {
        services.AddControllers();

        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();

        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>(DatabaseHealthCheck.Name);

        // Behind Azure App Service, TLS terminates at the front end and the container is reached over
        // HTTP with the original scheme in X-Forwarded-Proto. Honour it so UseHttpsRedirection (and
        // generated URLs) see "https" rather than looping. The proxy IP isn't fixed, so the known
        // networks/proxies allow-lists are cleared.
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
        });

        return services;
    }

    private static IServiceCollection AddApiPersistence(this IServiceCollection services, IConfiguration config)
    {
        // Connection details + SSL are resolved through DatabaseOptions (shared with Movies.DbTool). A
        // full connection string (e.g. from Key Vault) wins; otherwise it's built from the POSTGRES_*
        // vars shared with docker-compose, with SSL applied for managed Postgres.
        var databaseOptions = DatabaseConfiguration.Resolve(config);
        services.AddSingleton(databaseOptions);

        services.AddApplication();
        services.AddDatabase(databaseOptions.BuildConnectionString());

        return services;
    }
}
