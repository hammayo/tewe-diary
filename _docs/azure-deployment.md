# Azure deployment runbook

One-time provisioning to make the `deploy.yml` pipeline work, driven from your terminal.
**Assumes you have already run `az login`** (and selected the right subscription with
`az account set --subscription <id>`). GitHub config uses the `gh` CLI; every `gh` step
has a UI equivalent under **Settings → Secrets and variables → Actions**.

The pipeline itself (build → test → push images → migrate → deploy to slots → swap) is
described in [ci-cd.md](ci-cd.md); this doc creates the Azure + GitHub resources it needs.

> **Prefer to script it? (recommended)** `scripts/azure-setup.sh` automates every step
> below and needs **no edits** in the common case — it reads the repo `.env`, auto-detects
> the GitHub repo from `git remote`, and derives globally-unique resource names from your
> subscription id. Preview first, then run:
>
> ```bash
> bash scripts/azure-setup.sh --dry-run   # prints every action, changes nothing
> bash scripts/azure-setup.sh             # provision
> ```
>
> This document is the reference for what that script does and how to do it by hand.

## 0. Choose names (edit, then paste into your shell)

The script derives all of these automatically; set them explicitly only for the manual
path below. Secrets and `JWT_*` are reused from the repo `.env` when present.

```bash
export LOCATION=uksouth
export RG=tewe-rg
export ACR_NAME=tewemoviesacr$RANDOM         # globally unique, lowercase alphanumeric
export ACR_LOGIN_SERVER=$ACR_NAME.azurecr.io
export KV=tewe-kv-$RANDOM                     # globally unique
export PG=tewe-pg-$RANDOM                     # globally unique
export PG_ADMIN=teweadmin
export PG_ADMIN_PW='<a-strong-password>'
export PLAN=tewe-plan
export MOVIES_APP=tewe-movies-api            # globally unique (becomes *.azurewebsites.net)
export IDENTITY_APP=tewe-identity-api        # globally unique
export SLOT=staging
export GH_REPO=$(git remote get-url origin | sed -E 's#.*github\.com[:/]([^/]+/[^/.]+)(\.git)?#\1#')
export JWT_SECRET="${JWT_TOKEN_SECRET:-<a-long-random-64+char-secret>}"   # from .env
export JWT_ISSUER="${JWT_ISSUER:-https://$IDENTITY_APP.azurewebsites.net}"
export JWT_AUDIENCE="${JWT_AUDIENCE:-https://$MOVIES_APP.azurewebsites.net}"
export API_KEY="${API_KEY:-<a-random-api-key>}"                          # from .env
export SUB=$(az account show --query id -o tsv)
export TENANT=$(az account show --query tenantId -o tsv)
export PGHOST=$PG.postgres.database.azure.com
```

## 1. Resource group

```bash
az group create -n "$RG" -l "$LOCATION"
```

## 2. Container registry

```bash
az acr create -n "$ACR_NAME" -g "$RG" --sku Basic
```

## 3. Key Vault (RBAC) + let yourself write secrets

```bash
az keyvault create -n "$KV" -g "$RG" -l "$LOCATION" --enable-rbac-authorization true
az role assignment create \
  --assignee "$(az ad signed-in-user show --query id -o tsv)" \
  --role "Key Vault Secrets Officer" \
  --scope "$(az keyvault show -n "$KV" --query id -o tsv)"
```

## 4. PostgreSQL Flexible Server

```bash
az postgres flexible-server create -g "$RG" -n "$PG" -l "$LOCATION" \
  --tier Burstable --sku-name Standard_B1ms --version 16 \
  --admin-user "$PG_ADMIN" --admin-password "$PG_ADMIN_PW" \
  --database-name movies --public-access None --yes

# Let Azure-hosted services (the Web Apps) reach it:
az postgres flexible-server firewall-rule create -g "$RG" -n "$PG" \
  --rule-name AllowAzureServices --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0

# Temporary rule for your machine, so you can create roles in the next step:
MYIP=$(curl -fsSL https://api.ipify.org)
az postgres flexible-server firewall-rule create -g "$RG" -n "$PG" \
  --rule-name myip --start-ip-address "$MYIP" --end-ip-address "$MYIP"
```

## 5. Database roles (least privilege)

Three roles: **DDL** (migrations), **importer** (data loads), **app** (runtime, DML-only).
The importer role is created by the repo script; create the other two inline. Requires `psql`.

```bash
export DDL_PW='<ddl-role-password>'
export APP_PW='<app-role-password>'
export IMPORTER_PW='<importer-role-password>'
CONN="host=$PGHOST port=5432 dbname=movies user=$PG_ADMIN sslmode=require"

# Least-privilege importer role (repo script)
PGPASSWORD="$PG_ADMIN_PW" psql "$CONN" \
  -v dbname=movies -v importer_password="$IMPORTER_PW" \
  -f scripts/helpers/create-importer-role.sql

# DDL role (runs migrations) and app role (runtime, DML only)
PGPASSWORD="$PG_ADMIN_PW" psql "$CONN" <<SQL
CREATE ROLE movies_ddl LOGIN PASSWORD '$DDL_PW';
GRANT ALL ON DATABASE movies TO movies_ddl;
GRANT ALL ON SCHEMA public TO movies_ddl;

CREATE ROLE movies_app LOGIN PASSWORD '$APP_PW';
GRANT CONNECT ON DATABASE movies TO movies_app;
GRANT USAGE ON SCHEMA public TO movies_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO movies_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO movies_app;
SQL
```

## 6. Store secrets in Key Vault

Connection strings are Npgsql format. The **runtime** secret is named `Database--ConnectionString`
so the Key Vault config provider maps it to the app's `Database:ConnectionString` key; the
migration/importer/api-key/tmdb secrets are read by the pipeline via `az keyvault secret show`,
so their names are your choice (recorded as GitHub variables in step 9).

```bash
CS_BASE="Host=$PGHOST;Port=5432;Database=movies;Ssl Mode=Require"

# App runtime (DML role) — mapped into config as Database:ConnectionString
az keyvault secret set --vault-name "$KV" --name "Database--ConnectionString" \
  --value "$CS_BASE;Username=movies_app;Password=$APP_PW"

# Pipeline: migrations (DDL role) and imports (importer role)
az keyvault secret set --vault-name "$KV" --name "pg-migration-connection" \
  --value "$CS_BASE;Username=movies_ddl;Password=$DDL_PW"
az keyvault secret set --vault-name "$KV" --name "pg-importer-connection" \
  --value "$CS_BASE;Username=movies_importer;Password=$IMPORTER_PW"

# Pipeline: TMDB key (for import.yml) and the Movies API key (for cache-evict after import)
az keyvault secret set --vault-name "$KV" --name "tmdb-api-key"  --value "<your-tmdb-key>"
az keyvault secret set --vault-name "$KV" --name "movies-api-key" --value "$API_KEY"
```

## 7. GitHub OIDC identity (passwordless login)

```bash
APP_ID=$(az ad app create --display-name "tewe-github-oidc" --query appId -o tsv)
az ad sp create --id "$APP_ID"

# Federated credential — subject MUST match the deploy job's environment (production)
az ad app federated-credential create --id "$APP_ID" --parameters "{
  \"name\": \"gh-production\",
  \"issuer\": \"https://token.actions.githubusercontent.com\",
  \"subject\": \"repo:$GH_REPO:environment:production\",
  \"audiences\": [\"api://AzureADTokenExchange\"]
}"

# Permissions for the CI identity
KV_ID=$(az keyvault show -n "$KV" --query id -o tsv)
ACR_ID=$(az acr show -n "$ACR_NAME" --query id -o tsv)
az role assignment create --assignee "$APP_ID" --role Contributor \
  --scope "/subscriptions/$SUB/resourceGroups/$RG"     # firewall rules + slot swaps
az role assignment create --assignee "$APP_ID" --role AcrPush --scope "$ACR_ID"
az role assignment create --assignee "$APP_ID" --role "Key Vault Secrets User" --scope "$KV_ID"
```

## 8. Web Apps for Containers (Movies + Identity)

```bash
az appservice plan create -g "$RG" -n "$PLAN" --is-linux --sku B1

for APP in "$MOVIES_APP" "$IDENTITY_APP"; do
  az webapp create -g "$RG" -p "$PLAN" -n "$APP" \
    --deployment-container-image-name mcr.microsoft.com/dotnet/samples:aspnetapp   # placeholder until first deploy
  az webapp deployment slot create -g "$RG" -n "$APP" --slot "$SLOT"
  # System-assigned identity on production + staging slots
  az webapp identity assign -g "$RG" -n "$APP"
  az webapp identity assign -g "$RG" -n "$APP" --slot "$SLOT"
done

# Grant AcrPull to each app/slot identity so App Service can pull from ACR
for TARGET in "$MOVIES_APP" "$IDENTITY_APP"; do
  for SCOPE_SLOT in "" "--slot $SLOT"; do
    PID=$(az webapp identity show -g "$RG" -n "$TARGET" $SCOPE_SLOT --query principalId -o tsv)
    az role assignment create --assignee-object-id "$PID" --assignee-principal-type ServicePrincipal \
      --role AcrPull --scope "$ACR_ID"
  done
done

# Movies app/slot identities also need to read Key Vault secrets
for SCOPE_SLOT in "" "--slot $SLOT"; do
  PID=$(az webapp identity show -g "$RG" -n "$MOVIES_APP" $SCOPE_SLOT --query principalId -o tsv)
  az role assignment create --assignee-object-id "$PID" --assignee-principal-type ServicePrincipal \
    --role "Key Vault Secrets User" --scope "$KV_ID"
done
```

### App settings

```bash
KV_URI=$(az keyvault show -n "$KV" --query properties.vaultUri -o tsv)

# Movies.Api — production + staging slot
for SCOPE_SLOT in "" "--slot $SLOT"; do
  az webapp config appsettings set -g "$RG" -n "$MOVIES_APP" $SCOPE_SLOT --settings \
    WEBSITES_PORT=8080 \
    KeyVault__Uri="$KV_URI" \
    Database__SslMode=Require \
    Database__MigrateOnStartup=false \
    JWT_TOKEN_SECRET="$JWT_SECRET" \
    JWT_ISSUER="$JWT_ISSUER" \
    JWT_AUDIENCE="$JWT_AUDIENCE" \
    API_KEY="$API_KEY"
done

# Identity.Api — production + staging slot. JWT_* MUST match Movies.Api exactly.
for SCOPE_SLOT in "" "--slot $SLOT"; do
  az webapp config appsettings set -g "$RG" -n "$IDENTITY_APP" $SCOPE_SLOT --settings \
    WEBSITES_PORT=8080 \
    JWT_TOKEN_SECRET="$JWT_SECRET" \
    JWT_ISSUER="$JWT_ISSUER" \
    JWT_AUDIENCE="$JWT_AUDIENCE"
done
```

> `Database__SslMode`/`Database__MigrateOnStartup` use `__` (double underscore) because App
> Service maps that to the `:` config separator. The app pulls `Database:ConnectionString`
> from Key Vault at startup via `KeyVault:Uri`; the pipeline migrates separately (so
> `MigrateOnStartup=false`).

## 9. GitHub secrets & variables

Requires `gh auth login` (or set these in the repo UI). The `production` environment must
exist to match the OIDC subject:

```bash
gh api -X PUT "repos/$GH_REPO/environments/production" >/dev/null

# Secrets (OIDC login)
gh secret set AZURE_CLIENT_ID       -R "$GH_REPO" -b "$APP_ID"
gh secret set AZURE_TENANT_ID       -R "$GH_REPO" -b "$TENANT"
gh secret set AZURE_SUBSCRIPTION_ID -R "$GH_REPO" -b "$SUB"

# Variables (resource names the workflow reads)
gh variable set ACR_NAME                   -R "$GH_REPO" -b "$ACR_NAME"
gh variable set ACR_LOGIN_SERVER           -R "$GH_REPO" -b "$ACR_LOGIN_SERVER"
gh variable set RESOURCE_GROUP             -R "$GH_REPO" -b "$RG"
gh variable set KEY_VAULT_NAME             -R "$GH_REPO" -b "$KV"
gh variable set PG_SERVER_NAME             -R "$GH_REPO" -b "$PG"
gh variable set WEBAPP_NAME                -R "$GH_REPO" -b "$MOVIES_APP"
gh variable set SLOT_NAME                  -R "$GH_REPO" -b "$SLOT"
gh variable set IDENTITY_WEBAPP_NAME       -R "$GH_REPO" -b "$IDENTITY_APP"
gh variable set IDENTITY_SLOT_NAME         -R "$GH_REPO" -b "$SLOT"
gh variable set MIGRATION_CONNECTION_SECRET -R "$GH_REPO" -b "pg-migration-connection"
gh variable set IMPORTER_CONNECTION_SECRET  -R "$GH_REPO" -b "pg-importer-connection"
gh variable set TMDB_API_KEY_SECRET         -R "$GH_REPO" -b "tmdb-api-key"
gh variable set MOVIES_API_KEY_SECRET       -R "$GH_REPO" -b "movies-api-key"

# Finally, un-skip the deploy job (the gate added in deploy.yml)
gh variable set DEPLOY_ENABLED -R "$GH_REPO" -b "true"
```

## 10. Deploy & verify

```bash
gh workflow run deploy.yml -R "$GH_REPO"     # or just push to main
gh run watch -R "$GH_REPO"

# After the swap:
curl -fsSL "https://$MOVIES_APP.azurewebsites.net/_health"     && echo OK
curl -fsSL "https://$IDENTITY_APP.azurewebsites.net/_health"   && echo OK
```

## 11. Optional: seed data

```bash
gh workflow run import.yml -R "$GH_REPO"      # fetch from TMDB → import → evict cache
```

See the [TMDB import runbook](tmdb-import.md) for the data path.

## 12. Teardown

```bash
az group delete -n "$RG" --yes --no-wait
az ad app delete --id "$APP_ID"
```

---

**Notes**
- The pipeline opens a **temporary Postgres firewall rule** for the runner's IP during
  migration and removes it on exit — you don't pre-authorise the runner.
- Keep migrations **backward-compatible** (expand/contract): they run *before* the slot swap,
  so the old image keeps serving until the swap instant.
- `JWT_TOKEN_SECRET`/`JWT_ISSUER`/`JWT_AUDIENCE` must be **identical** on both apps or Movies.Api
  rejects Identity.Api's tokens. Prefer real Key Vault references over inline values in production.
