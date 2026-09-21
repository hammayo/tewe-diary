#!/usr/bin/env bash
#
# One-time Azure provisioning for the deploy.yml pipeline. Runs with ZERO edits in the
# common case: it reads the repo `.env`, auto-detects the GitHub repo from `git remote`,
# and derives globally-unique resource names from your subscription id. Override anything
# in the CONFIG block or via environment variables. See _docs/azure-deployment.md.
#
# Usage:
#   bash scripts/azure-setup.sh              # provision
#   bash scripts/azure-setup.sh --dry-run    # print what it would do, change nothing
#
# Assumes `az login` is done (and `az account set` if you have multiple subscriptions).
# Requires: az. Optional: psql (DB roles), gh (GitHub config — otherwise prints values),
# openssl (secret generation).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

DRY_RUN=false
case "${1:-}" in
  --dry-run) DRY_RUN=true ;;
  -h|--help) sed -n '2,17p' "$0"; exit 0 ;;
  "") ;;
  *) echo "Unknown argument: $1 (use --dry-run)"; exit 2 ;;
esac

# Reuse local values from .env (JWT_*, API_KEY, TMDB_API_KEY, POSTGRES_*, ...).
[ -f "$REPO_ROOT/.env" ] && { set -a; . "$REPO_ROOT/.env"; set +a; }

# ── CONFIG — usually nothing to edit; override any value here or via env ──────────
LOCATION="${LOCATION:-uksouth}"
NAME_PREFIX="${NAME_PREFIX:-tewe}"          # base for auto-generated resource names
PG_ADMIN="${PG_ADMIN:-teweadmin}"
# Advanced overrides (blank = auto-derived below):
#   ACR_NAME  KV  PG  MOVIES_APP  IDENTITY_APP  GH_REPO  JWT_ISSUER  JWT_AUDIENCE
# Secrets (blank = taken from .env if present, else auto-generated):
#   JWT_SECRET (<= JWT_TOKEN_SECRET)  API_KEY  TMDB_API_KEY
#   PG_ADMIN_PW  DDL_PW  APP_PW  IMPORTER_PW
# ─────────────────────────────────────────────────────────────────────────────────

step()  { printf '\n\033[1;36m▶ %s\033[0m\n' "$*"; }
gen()   { openssl rand -base64 24 | tr -dc 'A-Za-z0-9' | cut -c1-24; }
run()   { if $DRY_RUN; then printf '  [dry-run] '; printf '%q ' "$@"; printf '\n'; else "$@"; fi; }
secret_set() {  # $1 name  $2 value  (value redacted in dry-run; retries while KV RBAC propagates)
  if $DRY_RUN; then echo "  [dry-run] az keyvault secret set --vault-name $KV --name $1 --value ***"; return; fi
  local i
  for i in $(seq 1 10); do
    if az keyvault secret set --vault-name "$KV" --name "$1" --value "$2" -o none 2>/dev/null; then return 0; fi
    echo "  waiting for Key Vault RBAC to propagate ($i/10)..."; sleep 15
  done
  echo "  ERROR: could not write secret '$1' (Key Vault permission not effective)" >&2; return 1
}

# ── Preflight ────────────────────────────────────────────────────────────────
step "Preflight ($([ "$DRY_RUN" = true ] && echo 'dry-run' || echo 'live'))"
command -v az >/dev/null || { echo "az CLI not found." >&2; exit 1; }
if az account show >/dev/null 2>&1; then
  SUB="$(az account show --query id -o tsv)"
  TENANT="$(az account show --query tenantId -o tsv)"
elif $DRY_RUN; then
  SUB="00000000-0000-0000-0000-000000000000"; TENANT="<tenant-id>"
  echo "  (not logged in; using placeholder subscription for preview)"
else
  echo "Run 'az login' first." >&2; exit 1
fi

# ── Derive names / repo / JWT / secrets ──────────────────────────────────────
SUFFIX="$(printf '%s' "$SUB" | tr -dc 'a-z0-9' | cut -c1-6)"
RG="${RG:-${NAME_PREFIX}-rg}"
ACR_NAME="${ACR_NAME:-${NAME_PREFIX}acr${SUFFIX}}"
KV="${KV:-${NAME_PREFIX}-kv-${SUFFIX}}"
PG="${PG:-${NAME_PREFIX}-pg-${SUFFIX}}"
PLAN="${PLAN:-${NAME_PREFIX}-plan}"
MOVIES_APP="${MOVIES_APP:-${NAME_PREFIX}-movies-${SUFFIX}}"
IDENTITY_APP="${IDENTITY_APP:-${NAME_PREFIX}-identity-${SUFFIX}}"
GH_REPO="${GH_REPO:-$(git -C "$REPO_ROOT" remote get-url origin 2>/dev/null \
  | sed -E 's#.*github\.com[:/]([^/]+/[^/.]+)(\.git)?#\1#')}"
[ -z "$GH_REPO" ] && { echo "Could not detect GH_REPO; set it in CONFIG." >&2; exit 1; }

PGHOST="$PG.postgres.database.azure.com"
ACR_LOGIN_SERVER="$ACR_NAME.azurecr.io"
ACR_ID="/subscriptions/$SUB/resourceGroups/$RG/providers/Microsoft.ContainerRegistry/registries/$ACR_NAME"
KV_ID="/subscriptions/$SUB/resourceGroups/$RG/providers/Microsoft.KeyVault/vaults/$KV"
KV_URI="https://$KV.vault.azure.net/"

# JWT + issuer/audience: .env values win; else derive from the app hostnames.
JWT_SECRET="${JWT_SECRET:-${JWT_TOKEN_SECRET:-$(openssl rand -base64 48 | tr -d '\n')}}"
JWT_ISSUER="${JWT_ISSUER:-https://${IDENTITY_APP}.azurewebsites.net}"
JWT_AUDIENCE="${JWT_AUDIENCE:-https://${MOVIES_APP}.azurewebsites.net}"
API_KEY="${API_KEY:-$(gen)}"
TMDB_API_KEY="${TMDB_API_KEY:-}"
: "${PG_ADMIN_PW:=$(gen)}"; : "${DDL_PW:=$(gen)}"; : "${APP_PW:=$(gen)}"; : "${IMPORTER_PW:=$(gen)}"

# Key Vault secret names (recorded as GitHub variables)
KV_RUNTIME_CONN="Database--ConnectionString"   # → config Database:ConnectionString
KV_MIGRATION_CONN="pg-migration-connection"
KV_IMPORTER_CONN="pg-importer-connection"
KV_TMDB="tmdb-api-key"
KV_MOVIES_API_KEY="movies-api-key"

cat <<EOF
  subscription : $SUB
  resource grp : $RG   ($LOCATION)
  acr / kv / pg: $ACR_NAME / $KV / $PG
  web apps     : $MOVIES_APP , $IDENTITY_APP
  github repo  : $GH_REPO
  jwt issuer   : $JWT_ISSUER
  jwt audience : $JWT_AUDIENCE
EOF

# ── 1. Resource group ────────────────────────────────────────────────────────
step "1. Resource group"
run az group create -n "$RG" -l "$LOCATION" \
  --tags project=tewe-rest-api env=demo managedBy=azure-setup.sh -o none

# ── 2. Container registry ────────────────────────────────────────────────────
step "2. Container registry ($ACR_NAME)"
run az acr create -n "$ACR_NAME" -g "$RG" --sku Basic -o none

# ── 3. Key Vault (RBAC) ──────────────────────────────────────────────────────
step "3. Key Vault ($KV)"
run az keyvault create -n "$KV" -g "$RG" -l "$LOCATION" --enable-rbac-authorization true -o none
if $DRY_RUN; then ME_ID="<your-object-id>"; else ME_ID="$(az ad signed-in-user show --query id -o tsv)"; fi
run az role assignment create --assignee "$ME_ID" --role "Key Vault Secrets Officer" --scope "$KV_ID" -o none || true

# ── 4. PostgreSQL Flexible Server ────────────────────────────────────────────
step "4. PostgreSQL Flexible Server ($PG)"
run az postgres flexible-server create -g "$RG" -n "$PG" -l "$LOCATION" \
  --tier Burstable --sku-name Standard_B1ms --version 16 \
  --admin-user "$PG_ADMIN" --admin-password "$PG_ADMIN_PW" \
  --database-name movies --public-access None --yes -o none
run az postgres flexible-server firewall-rule create -g "$RG" -n "$PG" \
  --rule-name AllowAzureServices --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0 -o none || true
if $DRY_RUN; then MYIP="<your-ip>"; else MYIP="$(curl -fsSL https://api.ipify.org)"; fi
run az postgres flexible-server firewall-rule create -g "$RG" -n "$PG" \
  --rule-name myip --start-ip-address "$MYIP" --end-ip-address "$MYIP" -o none || true

# ── 5. Database roles (needs psql) ───────────────────────────────────────────
step "5. Database roles (DDL / importer / app)"
if $DRY_RUN; then
  echo "  [dry-run] create movies_ddl / movies_app / movies_importer via psql"
elif command -v psql >/dev/null; then
  CONN="host=$PGHOST port=5432 dbname=movies user=$PG_ADMIN sslmode=require"
  PGPASSWORD="$PG_ADMIN_PW" psql "$CONN" -v ON_ERROR_STOP=0 \
    -v dbname=movies -v importer_password="$IMPORTER_PW" \
    -f "$SCRIPT_DIR/helpers/create-importer-role.sql"
  PGPASSWORD="$PG_ADMIN_PW" psql "$CONN" -v ON_ERROR_STOP=0 <<SQL
DO \$\$ BEGIN
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname='movies_ddl') THEN
    CREATE ROLE movies_ddl LOGIN PASSWORD '$DDL_PW';
  END IF;
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname='movies_app') THEN
    CREATE ROLE movies_app LOGIN PASSWORD '$APP_PW';
  END IF;
END \$\$;
GRANT ALL ON DATABASE movies TO movies_ddl;
GRANT ALL ON SCHEMA public TO movies_ddl;
GRANT CONNECT ON DATABASE movies TO movies_app;
GRANT USAGE ON SCHEMA public TO movies_app;

-- Migrations run AS movies_ddl and create the tables. Default privileges must be set
-- FOR ROLE movies_ddl (not the admin's own future objects) so movies_app / movies_importer
-- automatically get access to tables the migrations create later. The admin must be a
-- member of movies_ddl to ALTER its default privileges.
GRANT movies_ddl TO current_user;
ALTER DEFAULT PRIVILEGES FOR ROLE movies_ddl IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO movies_app;
ALTER DEFAULT PRIVILEGES FOR ROLE movies_ddl IN SCHEMA public
  GRANT USAGE, SELECT ON SEQUENCES TO movies_app;
ALTER DEFAULT PRIVILEGES FOR ROLE movies_ddl IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO movies_importer;

-- Also cover any tables that already exist (e.g. re-running after a migration).
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO movies_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO movies_app;
SQL
else
  echo "  psql not found — create movies_ddl / movies_app / movies_importer manually"
  echo "  (see _docs/azure-deployment.md step 5), then re-run."
fi

# ── 6. Key Vault secrets ─────────────────────────────────────────────────────
step "6. Key Vault secrets"
CS_BASE="Host=$PGHOST;Port=5432;Database=movies;Ssl Mode=Require"
secret_set "$KV_RUNTIME_CONN"   "$CS_BASE;Username=movies_app;Password=$APP_PW"
secret_set "$KV_MIGRATION_CONN" "$CS_BASE;Username=movies_ddl;Password=$DDL_PW"
secret_set "$KV_IMPORTER_CONN"  "$CS_BASE;Username=movies_importer;Password=$IMPORTER_PW"
secret_set "$KV_MOVIES_API_KEY" "$API_KEY"
[ -n "$TMDB_API_KEY" ] && secret_set "$KV_TMDB" "$TMDB_API_KEY"

# ── 7. GitHub OIDC identity ──────────────────────────────────────────────────
step "7. GitHub OIDC app registration + role assignments"
if $DRY_RUN; then
  APP_ID="<app-id>"
else
  APP_ID="$(az ad app list --display-name tewe-github-oidc --query '[0].appId' -o tsv)"
  [ -z "$APP_ID" ] && APP_ID="$(az ad app create --display-name tewe-github-oidc --query appId -o tsv)"
  az ad sp show --id "$APP_ID" >/dev/null 2>&1 || az ad sp create --id "$APP_ID" -o none
fi
run az ad app federated-credential create --id "$APP_ID" --parameters "{
  \"name\": \"gh-production\",
  \"issuer\": \"https://token.actions.githubusercontent.com\",
  \"subject\": \"repo:$GH_REPO:environment:production\",
  \"audiences\": [\"api://AzureADTokenExchange\"]
}" -o none || true
run az role assignment create --assignee "$APP_ID" --role Contributor \
  --scope "/subscriptions/$SUB/resourceGroups/$RG" -o none || true
run az role assignment create --assignee "$APP_ID" --role AcrPush --scope "$ACR_ID" -o none || true
run az role assignment create --assignee "$APP_ID" --role "Key Vault Secrets User" --scope "$KV_ID" -o none || true

# ── 8. Web Apps for Containers (identities + settings) ───────────────────────
# Basic B1 plan (cheapest for containers) — no deployment slots; deploy.yml ships
# straight to production.
step "8. Web Apps ($MOVIES_APP, $IDENTITY_APP)"
run az appservice plan create -g "$RG" -n "$PLAN" --is-linux --sku B1 -o none
PLACEHOLDER="mcr.microsoft.com/dotnet/samples:aspnetapp"
for APP in "$MOVIES_APP" "$IDENTITY_APP"; do
  run az webapp create -g "$RG" -p "$PLAN" -n "$APP" --deployment-container-image-name "$PLACEHOLDER" -o none
  if $DRY_RUN; then PID="<principal-id>"
  else PID="$(az webapp identity assign -g "$RG" -n "$APP" --query principalId -o tsv)"; fi
  run az role assignment create --assignee-object-id "$PID" --assignee-principal-type ServicePrincipal \
    --role AcrPull --scope "$ACR_ID" -o none || true
  if [ "$APP" = "$MOVIES_APP" ]; then
    run az role assignment create --assignee-object-id "$PID" --assignee-principal-type ServicePrincipal \
      --role "Key Vault Secrets User" --scope "$KV_ID" -o none || true
  fi
  # AcrPull on the identity is not enough — App Service must be told to pull with it.
  run az webapp config set -g "$RG" -n "$APP" \
    --generic-configurations '{"acrUseManagedIdentityCreds": true}' -o none
  # Cheap, no-cost hardening: force HTTPS, TLS 1.2 floor, disable FTPS.
  run az webapp update -g "$RG" -n "$APP" --https-only true -o none
  run az webapp config set -g "$RG" -n "$APP" --min-tls-version 1.2 --ftps-state Disabled -o none
done

step "8b. App settings"
if $DRY_RUN; then
  echo "  [dry-run] set Movies app settings (KeyVault__Uri, Database__*, JWT_* ***, API_KEY ***, WEBSITES_PORT)"
  echo "  [dry-run] set Identity app settings (JWT_* ***, WEBSITES_PORT)"
else
  az webapp config appsettings set -g "$RG" -n "$MOVIES_APP" -o none --settings \
    WEBSITES_PORT=8080 KeyVault__Uri="$KV_URI" Database__SslMode=Require \
    Database__MigrateOnStartup=false JWT_TOKEN_SECRET="$JWT_SECRET" \
    JWT_ISSUER="$JWT_ISSUER" JWT_AUDIENCE="$JWT_AUDIENCE" API_KEY="$API_KEY"
  az webapp config appsettings set -g "$RG" -n "$IDENTITY_APP" -o none --settings \
    WEBSITES_PORT=8080 JWT_TOKEN_SECRET="$JWT_SECRET" \
    JWT_ISSUER="$JWT_ISSUER" JWT_AUDIENCE="$JWT_AUDIENCE"
fi

# ── 9. GitHub secrets & variables ────────────────────────────────────────────
step "9. GitHub secrets & variables"
gh_secret() { if $DRY_RUN; then echo "  [dry-run] gh secret set $1 = ***"; else gh secret set "$1" -R "$GH_REPO" -b "$2"; fi; }
gh_var()    { if $DRY_RUN; then echo "  [dry-run] gh variable set $1 = $2"; else gh variable set "$1" -R "$GH_REPO" -b "$2"; fi; }
if command -v gh >/dev/null && { $DRY_RUN || gh auth status >/dev/null 2>&1; }; then
  $DRY_RUN || gh api -X PUT "repos/$GH_REPO/environments/production" >/dev/null
  gh_secret AZURE_CLIENT_ID           "$APP_ID"
  gh_secret AZURE_TENANT_ID           "$TENANT"
  gh_secret AZURE_SUBSCRIPTION_ID     "$SUB"
  gh_var ACR_NAME                     "$ACR_NAME"
  gh_var ACR_LOGIN_SERVER            "$ACR_LOGIN_SERVER"
  gh_var RESOURCE_GROUP              "$RG"
  gh_var KEY_VAULT_NAME             "$KV"
  gh_var PG_SERVER_NAME            "$PG"
  gh_var WEBAPP_NAME             "$MOVIES_APP"
  gh_var IDENTITY_WEBAPP_NAME  "$IDENTITY_APP"
  gh_var MIGRATION_CONNECTION_SECRET "$KV_MIGRATION_CONN"
  gh_var IMPORTER_CONNECTION_SECRET  "$KV_IMPORTER_CONN"
  gh_var TMDB_API_KEY_SECRET         "$KV_TMDB"
  gh_var MOVIES_API_KEY_SECRET       "$KV_MOVIES_API_KEY"
  gh_var DEPLOY_ENABLED              "true"
else
  cat <<EOF
  gh not available/authenticated — set these in the repo UI
  (Settings → Secrets and variables → Actions), and create a 'production' environment:

  Secrets:   AZURE_CLIENT_ID=$APP_ID   AZURE_TENANT_ID=$TENANT   AZURE_SUBSCRIPTION_ID=$SUB
  Variables: ACR_NAME=$ACR_NAME  ACR_LOGIN_SERVER=$ACR_LOGIN_SERVER  RESOURCE_GROUP=$RG
             KEY_VAULT_NAME=$KV  PG_SERVER_NAME=$PG  WEBAPP_NAME=$MOVIES_APP
             IDENTITY_WEBAPP_NAME=$IDENTITY_APP
             MIGRATION_CONNECTION_SECRET=$KV_MIGRATION_CONN  IMPORTER_CONNECTION_SECRET=$KV_IMPORTER_CONN
             TMDB_API_KEY_SECRET=$KV_TMDB  MOVIES_API_KEY_SECRET=$KV_MOVIES_API_KEY  DEPLOY_ENABLED=true
EOF
fi

# ── Summary ──────────────────────────────────────────────────────────────────
step "Done ($([ "$DRY_RUN" = true ] && echo 'dry-run — nothing changed' || echo 'provisioned'))"
if ! $DRY_RUN; then
cat <<EOF
Connection strings / API key / TMDB key are stored in Key Vault '$KV'. These are NOT
stored anywhere retrievable — copy them somewhere safe now:
  POSTGRES_ADMIN ($PG_ADMIN) password: $PG_ADMIN_PW
  JWT_TOKEN_SECRET=$JWT_SECRET
  API_KEY=$API_KEY

Next:
  git push origin main        # or: gh workflow run deploy.yml -R $GH_REPO
  gh run watch -R $GH_REPO
  curl -fsSL https://$MOVIES_APP.azurewebsites.net/_health   && echo OK
EOF
fi
