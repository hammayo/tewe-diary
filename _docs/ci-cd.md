# CI/CD & deployment

GitHub Actions workflows live in `.github/workflows/`
(see the [workflows reference](../.github/workflows/_README.md) for operational detail).

To provision Azure and wire up the secrets/variables this pipeline needs, follow the
step-by-step [Azure deployment runbook](azure-deployment.md).

## Pipeline

```
build ─→ test ─→ build images (Movies.Api + Identity.Api) ─→ migrate (DDL role) ─→ deploy both
```

- **Build + test** run against `backend/TeweDiary.sln`. Every workflow reads the SDK version from `global.json`.
- **Path filters**: `deploy.yml` and `sonarcloud.yml` ignore `frontend/**`, and `frontend.yml` runs only for
  `frontend/**`, so a backend change does not build the React app and the other way round.
- **Images** are built for both APIs and pushed to Azure Container Registry.
- **Migrate** applies FluentMigrator migrations **before** deploy, using a
  least-privilege **DDL role** (separate from the app's runtime role).
- **Deploy** ships both APIs straight to their Azure Web Apps (Basic B1 plan — no slots).
- **Deployment is off until `DEPLOY_ENABLED` is `true`.** The `deploy` job in `deploy.yml`, and `import.yml`, run only when the
  repository variable `DEPLOY_ENABLED` is `true`. Without it, a push to `main` runs the `test` job, skips `deploy` and ships
  nothing to Azure, yet the workflow still shows green (so does the README's Deploy badge). The runbook sets the variable as
  its last step in [step 9](azure-deployment.md#9-github-secrets--variables). `gh variable list` shows whether it is set.
- **Rollback** re-points both Web Apps at the previous commit's image (tags are commit SHAs); see
  [Roll back](azure-deployment.md#roll-back-a-bad-deploy).

- **Frontend** (`frontend.yml`) builds the `test` target (lint, typecheck, unit tests) and then the production image from
  `frontend/Dockerfile`, for `frontend/**` changes only. It is the same Docker targets as local work.

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
scripts/fetch-tmdb.sh  ─→  data/tmdb-<fetch>.ndjson  ─→  dotnet run --project backend/tools/Movies.DbTool -- import data/*.ndjson
```

See the [TMDB import runbook](tmdb-import.md) for the full data path.
