# Movies.Api container image for Azure App Service (Web App for Containers).
ARG DOTNET_VERSION=10.0
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

# Restore layer: copy only what restore needs (with central package management) so this caches
# unless dependencies change.
COPY global.json ./
COPY backend/Directory.Build.props backend/Directory.Packages.props backend/
COPY backend/src/Movies.Api/Movies.Api.csproj backend/src/Movies.Api/
COPY backend/src/Movies.Application/Movies.Application.csproj backend/src/Movies.Application/
COPY backend/src/Movies.Contracts/Movies.Contracts.csproj backend/src/Movies.Contracts/
RUN dotnet restore backend/src/Movies.Api/Movies.Api.csproj

# Build + publish. `scripts/import-transform.sql` (embedded by Movies.Application) is present after
# this copy.
COPY . .
RUN dotnet publish backend/src/Movies.Api/Movies.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final
WORKDIR /app

# curl is used by the container health check (the runtime image has no HTTP client otherwise).
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Run as the non-root `app` user shipped with the .NET images (port 8080 needs no privileges).
USER app

COPY --from=build /app ./

# App Service routes to this port; also set WEBSITES_PORT=8080 on the Web App.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=5s --start-period=40s --retries=5 \
    CMD curl -fsS http://localhost:8080/_health || exit 1
ENTRYPOINT ["dotnet", "Movies.Api.dll"]
