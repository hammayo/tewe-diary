# CI/CD & deployment

GitHub Actions workflows live in `.github/workflows/`
(see the [workflows reference](../.github/workflows/_README.md) for operational detail).

To provision Azure and wire up the secrets/variables this pipeline needs, follow the
step-by-step [Azure deployment runbook](azure-deployment.md).

## Pipeline

```
build ─→ test ─→ build images (Movies.Api + Identity.Api) ─→ migrate (DDL role) ─→ deploy both
```

- **Build + test** run against `TeweRestApi.sln`.
- **Images** are built for both APIs and pushed to Azure Container Registry.
- **Migrate** applies FluentMigrator migrations **before** deploy, using a
  least-privilege **DDL role** (separate from the app's runtime role).
- **Deploy** ships both APIs to Azure (Web App + slot).

## Secrets & configuration

- Secrets come from **Azure Key Vault** via managed identity (`KeyVault__Uri`, Azure-only).
- `JWT_TOKEN_SECRET`, `JWT_ISSUER`, `JWT_AUDIENCE` **must match** between Identity.Api and
  Movies.Api or issued tokens are rejected. Share them via Key Vault references.
- `Database__MigrateOnStartup` is `false` in Azure — the pipeline owns migrations, not the app.
- `Database__SslMode=Require` against Azure Flexible Server.

## Database role separation

Credentials are split by privilege:

- **DDL role** — runs schema migrations (pipeline only).
- **Importer role** — least-privilege, used for TMDB data loads
  (`scripts/helpers/create-importer-role.sql`).
- **Runtime role** — the app's day-to-day connection.

## Data import in CI

`.github/workflows/import.yml` fetches from TMDB and imports via the CLI:

```
scripts/fetch-tmdb.sh  ─→  Data/tmdb-movies.ndjson  ─→  dotnet run --project Ops.Tools/Movies.DbTool -- import Data/tmdb-movies.ndjson
```

See the [TMDB import runbook](tmdb-import.md) for the full data path.
