# CI/CD workflows

Two workflows target Azure App Service (Web App for Containers) + Azure Database for PostgreSQL
Flexible Server.

- **`deploy.yml`** — on push to `main` (or manual): tests, builds & pushes **two images**
  (`Movies.Api` and `Identity.Api`) to ACR, applies migrations (DDL role) *before* deploying,
  deploys both to their staging slots, warms them, then swaps both into production. Migrations
  must stay backward-compatible (expand/contract) because the old image serves until the swap.
- **`import.yml`** — manual only: fetches from TMDB, runs the idempotent upsert import as the
  least-privilege `movies_importer` role, then evicts the API's `movies` cache.

## Database networking

Flexible Server uses **public access**. Each DB step adds a **temporary firewall rule** for the
runner's egress IP and removes it on completion (`trap ... EXIT`). No VNet or self-hosted runner
is required, and access is not opened to all Azure services.

## Azure auth

Passwordless via **OIDC** (`azure/login`). Create a federated credential on an app registration
for this repo (and the `production` environment), and grant that identity:

- **AcrPush** (or Contributor) on the ACR,
- rights to manage Flexible Server firewall rules and swap Web App slots (Contributor on the RG
  is simplest),
- **get** on the Key Vault secrets.

## Required GitHub configuration

Secrets:
- `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`

Variables:
- `ACR_NAME`, `ACR_LOGIN_SERVER` (e.g. `myacr.azurecr.io`)
- `RESOURCE_GROUP`
- `WEBAPP_NAME`, `SLOT_NAME` (Movies.Api Web App + slot, e.g. `staging`)
- `IDENTITY_WEBAPP_NAME`, `IDENTITY_SLOT_NAME` (Identity.Api Web App + slot)
- `PG_SERVER_NAME` (Flexible Server name)
- `KEY_VAULT_NAME`
- `MIGRATION_CONNECTION_SECRET`, `IMPORTER_CONNECTION_SECRET`, `TMDB_API_KEY_SECRET`,
  `MOVIES_API_KEY_SECRET` (Key Vault secret names)

## Web Apps (one-time)

Two Web Apps for Containers, each with a `staging` deployment slot and a managed identity holding
**AcrPull** on the ACR; set `WEBSITES_PORT=8080` on both.

- **Movies.Api** (`WEBAPP_NAME`): identity also needs **get/list** on the Key Vault. App settings:
  `KeyVault:Uri=<vault uri>`, `Database:SslMode=Require`, `Database:MigrateOnStartup=false`. The
  app reads the runtime connection string / API key from Key Vault via `KeyVault:Uri`; the
  pipeline reads the migration/importer connection strings separately.
- **Identity.Api** (`IDENTITY_WEBAPP_NAME`): stateless JWT issuer, no database. App settings
  `JWT_TOKEN_SECRET`, `JWT_ISSUER`, `JWT_AUDIENCE` **must match** the values Movies.Api validates
  with (share them via Key Vault references or identical settings), otherwise Movies.Api rejects
  the tokens it issues. Its usable endpoint is `POST /token`.

## HTTPS behind App Service

App Service terminates TLS at its front end and forwards to the container over HTTP with the
original scheme in `X-Forwarded-Proto`. The app enables `ForwardedHeaders` middleware (see
`Program.cs`) so `UseHttpsRedirection` and generated URLs honour that scheme instead of looping.
No extra Web App configuration is required for this; just don't strip the `X-Forwarded-*` headers.

## Database roles

Create the least-privilege import role once (see `scripts/create-importer-role.sql`). Use a
DDL-capable role for `MIGRATION_CONNECTION_SECRET`, the `movies_importer` role for
`IMPORTER_CONNECTION_SECRET`, and a DML-only role for the app's runtime connection string.
