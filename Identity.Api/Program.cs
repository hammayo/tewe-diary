// Load the repo-root .env (walking up from the working directory) so secrets are
// picked up as environment variables during local `dotnet run`. Harmless in
// environments where real environment variables are provided instead.
DotNetEnv.Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

var app = builder.Build();

app.UseAuthorization();

app.MapControllers();

app.Run();
