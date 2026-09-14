# Movies.Api container image for Azure App Service (Web App for Containers).
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore layer: copy only what restore needs (with central package management) so this caches
# unless dependencies change.
COPY Directory.Packages.props ./
COPY Movies.Api/Movies.Api.csproj Movies.Api/
COPY Movies.Application/Movies.Application.csproj Movies.Application/
COPY Movies.Contracts/Movies.Contracts.csproj Movies.Contracts/
RUN dotnet restore Movies.Api/Movies.Api.csproj

# Build + publish. `scripts/import-transform.sql` (embedded by Movies.Application) is present after
# this copy.
COPY . .
RUN dotnet publish Movies.Api/Movies.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# curl is used by the container health check (the runtime image has no HTTP client otherwise).
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app ./

# App Service routes to this port; also set WEBSITES_PORT=8080 on the Web App.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=5s --start-period=40s --retries=5 \
    CMD curl -fsS http://localhost:8080/_health || exit 1
ENTRYPOINT ["dotnet", "Movies.Api.dll"]
