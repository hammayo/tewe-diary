# Design decisions & notes

Why the code looks the way it does — the deliberate trade-offs, and what would change
them. Lightweight ADR style: **decision → rationale → revisit when**.

## Contents

1. [One `Movies.Application` project, not separate Domain/Application/Infrastructure](#1-one-moviesapplication-project-not-separate-domainapplicationinfrastructure)
2. [Dapper, not an ORM](#2-dapper-not-an-orm)
3. [Schema owned by FluentMigrator migrations, never by import scripts](#3-schema-owned-by-fluentmigrator-migrations-never-by-import-scripts)
4. [The import transform is embedded, shared with the ops path](#4-the-import-transform-is-embedded-shared-with-the-ops-path)
5. [Two services, one shared JWT signing secret](#5-two-services-one-shared-jwt-signing-secret)
6. [Policy-based authorization plus an API-key filter](#6-policy-based-authorization-plus-an-api-key-filter)
7. [Output caching with tag-based eviction](#7-output-caching-with-tag-based-eviction)
8. [URL-segment API versioning from day one](#8-url-segment-api-versioning-from-day-one)
9. [Integration tests hit a real Postgres](#9-integration-tests-hit-a-real-postgres)
10. [Repository housekeeping](#10-repository-housekeeping)
11. [Deployment hosting and cost (Basic B1, no slots)](#11-deployment-hosting-and-cost-basic-b1-no-slots)
12. [Getting to $0 (free-tier options and limitations)](#12-getting-to-0-free-tier-options-and-limitations)
13. [Deployment gotchas fixed (ACR pull, DB grants, KV propagation)](#13-deployment-gotchas-fixed-acr-pull-db-grants-kv-propagation)

## 1. One `Movies.Application` project, not separate Domain/Application/Infrastructure

**Decision.** Domain, application, and infrastructure live in a single project, separated
by folder (`Models/`, `Services/`+`Validators/`, `Repositories/`+`Database/`).

**Rationale.** The dependency rule that matters (core references nothing outward, DTOs
don't leak in) is already enforced at the project boundary. A three-project split at this
size adds ceremony without protecting anything more.

**Revisit when.** A second data store or a non-Postgres adapter appears — the folders map
1:1 to projects, so the split is mechanical. See [architecture.md](architecture.md).

## 2. Dapper, not an ORM

**Decision.** Data access is Dapper over Npgsql with hand-written SQL.

**Rationale.** The queries are simple and explicit; Dapper keeps them visible and fast
without change-tracking or migration magic in the query path. Schema ownership is separate
(see #3).

**Revisit when.** The domain grows aggregates with complex object graphs where an ORM's
identity map/change tracking would genuinely reduce hand-written plumbing.

## 3. Schema owned by FluentMigrator migrations, never by import scripts

**Decision.** `Movies.Application/Database/Migrations` is the single owner of DDL. The
data scripts and CLI import are **data-only**.

**Rationale.** One authority for schema shape; migrations are versioned, ordered, and run
in CI with a dedicated DDL role. Import paths can't silently drift the schema.

## 4. The import transform is embedded, shared with the ops path

**Decision.** `scripts/helpers/import-transform.sql` is embedded into `Movies.Application`
as a resource (`MovieImporter`) **and** used by the psql-based `load-movies.sh`.

**Rationale.** Single source of truth: the CLI import and the manual ops import run the
*exact same* transform, so behaviour can't diverge between them.

**Note.** The `.csproj` references the SQL by relative path with a fixed `LogicalName`; the
code reads it by that logical name, so moving the file only touches the `<EmbeddedResource>` path.

## 5. Two services, one shared JWT signing secret

**Decision.** `Identity.Api` issues JWTs; `Movies.Api` validates them. Both read the same
`JWT_TOKEN_SECRET`/`JWT_ISSUER`/`JWT_AUDIENCE`.

**Rationale.** Separates token issuance from the resource API (a realistic boundary) while
keeping validation local — no network hop or introspection call per request.

**Revisit when.** Going multi-issuer or public-client — move to asymmetric keys (RS256) and
JWKS instead of a shared symmetric secret.

## 6. Policy-based authorization plus an API-key filter

**Decision.** Roles (`Admin`, `Trusted`) are ASP.NET Core **policies** driven by JWT claims;
the admin cache-eviction endpoint uses an **API-key** filter (`x-api-key`) instead.

**Rationale.** User-facing actions authorize on claims; machine/ops actions authorize on a
shared key — different trust models for different callers. See [api.md](api.md).

## 7. Output caching with tag-based eviction

**Decision.** Reads are output-cached; every write **evicts the movie cache tag**, and an
admin endpoint can evict on demand.

**Rationale.** Cheap read scaling without stale reads after mutations — correctness is kept
by tying eviction to writes rather than short TTL guesses.

## 8. URL-segment API versioning from day one

**Decision.** Routes are `/api/v{version}` via `Asp.Versioning`, defaulting to v1, with
versions reported in a response header and one Swagger doc per version.

**Rationale.** Lets a v2 surface (planned as Minimal APIs, in a feature branch) run beside
v1 without breaking existing clients.

## 9. Integration tests hit a real Postgres

**Decision.** No mocking of the data layer — integration tests spin up Postgres via
Testcontainers.

**Rationale.** The SQL *is* the logic here; testing against the real engine catches what a
mocked `IDbConnection` never would. Cost: Docker required to run the full suite.
See [testing.md](testing.md).

## 10. Repository housekeeping

- **`scripts/` stays at the repo root** even though `Movies.DbTool` moved under `Ops.Tools/` —
  keeping it at root preserves the scripts' `$SCRIPT_DIR/..` repo-root detection and every
  external caller (`.run`, CI, docs), so only non-executable helpers were grouped into
  `scripts/helpers/`.
- **`Data/` (generated ndjson) and `_task/` (working specs/plans) are gitignored** — generated
  data and in-flight planning docs don't belong in version control.
- **Postman collection** lives under `Ops.Tools/Postman/` alongside the DB CLI as operational tooling.

## 11. Deployment hosting and cost (Basic B1, no slots)

**Decision.** Both APIs deploy to **Azure App Service for Containers on a Basic (B1) plan**,
**directly to production** — no staging slots, no blue/green swap.

**Rationale.** This is a demo/portfolio project, not production. Staging slots require the
**Standard (S1)** tier (~5× the B1 price, ≈ $69/mo vs ≈ $13/mo) and Basic doesn't support them
at all. Paying for zero-downtime swaps isn't justified when a brief restart is acceptable.

**What changed in the code (2026-09).** The original pipeline was slot-based; it was simplified:

- `deploy.yml`: removed the *deploy-to-staging-slot → warm-slot → swap* steps; now does
  *deploy image to production → `az webapp restart` → health-check* for both apps. Dropped the
  `SLOT_NAME` / `IDENTITY_SLOT_NAME` variables.
- `scripts/azure-setup.sh`: removed slot creation and the per-slot identity / app-settings loops
  (one system-assigned identity and one app-settings set per app); dropped the `SLOT` config var
  and the two slot GitHub variables.
- Docs (`azure-deployment.md`, `ci-cd.md`, `.github/workflows/_README.md`) updated to match.

**Limitation.** No zero-downtime deploys — a deploy restarts the container, so there's a short
outage and a brief window where new migrations meet the old image. Migrations are kept
**backward-compatible (expand/contract)** to keep that window safe. **Revisit** by moving to
S1 + slots only if this ever needs real uptime.

**Cost (approx, pay-as-you-go).** ACR Basic ~$5/mo · Postgres Flexible Server B1ms + ~32 GB
~$15–25/mo · App Service B1 ~$13/mo · Key Vault pennies ≈ **$35–45/mo**. `az group delete`
(setup script step 12) stops all charges. A dry-run (`scripts/azure-setup.sh --dry-run`) costs
nothing.

## 12. Getting to $0 (free-tier options and limitations)

Truly $0-forever isn't achievable on this *exact* Azure stack, because: Linux App Service
**Free (F1) can't run custom containers** (code only); **PostgreSQL Flexible Server has no
perpetual free tier** (new accounts get 12 months free, then paid); and **ACR Basic isn't free**.
The options, with their trade-offs:

| Option                                 | How                                                                                                                                                                                                                | Cost                                 | Limitation                                                                                                                 |
|----------------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|--------------------------------------|----------------------------------------------------------------------------------------------------------------------------|
| **Local Docker** (recommended default) | `bash scripts/stack-up.sh`                                                                                                                                                                                         | $0, no cloud                         | No public URL; demo via screenshots/GIF                                                                                    |
| **Temporary Azure**                    | Current design + new-account **$200/30-day credit** and **12-month free Postgres**; tear down after                                                                                                                | $0 out-of-pocket while within credit | Not permanent; must `az group delete` to avoid later charges                                                               |
| **$0 public (different stack)**        | **Azure Container Apps** (consumption free grant + scale-to-zero) + **GitHub Container Registry** (free public images) + free managed Postgres (**Neon**/**Supabase**) + **no Key Vault** (config via env/secrets) | ~$0 for low traffic                  | Real rework of `deploy.yml` + `azure-setup.sh`; no longer "App Service"; cold starts from scale-to-zero; free DB size caps |

**Decision (current).** Keep the App Service B1 design (see #11) and treat **local Docker** as the
default $0 demo path, with **temporary Azure under the free credit** to show it live. The
Container Apps + ghcr + Neon path is documented as the route to a *permanently-live* $0 URL but
is **not implemented** — it would swap the hosting/registry/DB and drop Key Vault.

**Notes on the $0-public path (if pursued later).**
- The app is host-agnostic: it reads `Database:ConnectionString` from config, so pointing it at
  Neon/Supabase needs no code change.
- Key Vault is already optional — the app skips it when `KeyVault:Uri` is unset (see #5-ish wiring
  in `ApiServiceCollectionExtensions`), so a $0 build just supplies config via env vars/secrets.
- Free tiers change; verify current Container Apps grant, ghcr limits, and Neon/Supabase caps
  before relying on them.

## 13. Deployment gotchas fixed (ACR pull, DB grants, KV propagation)

Three runtime gaps that a dry-run can't catch (they're ordering/timing issues, not syntax) were
found and fixed in `scripts/azure-setup.sh` (2026-09).

**1. ACR pull needs managed-identity creds explicitly enabled.**
Granting the Web App's identity `AcrPull` is necessary but **not sufficient** — App Service still
defaults to admin/anonymous pulls, so a private-ACR image fails to pull and the app never starts.
*Fix:* after assigning the identity, enable it per app:
```bash
az webapp config set -g "$RG" -n "$APP" --generic-configurations '{"acrUseManagedIdentityCreds": true}'
```

**2. DB grants must target the DDL role's future tables.**
Migrations run **as `movies_ddl`** and create the tables *after* setup. The original grants ran
`GRANT … ON ALL TABLES` (zero tables existed yet) and `ALTER DEFAULT PRIVILEGES IN SCHEMA public`
**without `FOR ROLE movies_ddl`** — so they only covered the admin's future objects, not the
migration-created tables. Result: `movies_app` / `movies_importer` get *permission denied* at
runtime and `/_health` fails the deploy. *Fix:* make the admin a member of `movies_ddl` and set
default privileges **for that role** (plus sequences), so tables created by later migrations are
auto-granted:
```sql
GRANT movies_ddl TO current_user;
ALTER DEFAULT PRIVILEGES FOR ROLE movies_ddl IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO movies_app;
ALTER DEFAULT PRIVILEGES FOR ROLE movies_ddl IN SCHEMA public
  GRANT USAGE, SELECT ON SEQUENCES TO movies_app;
ALTER DEFAULT PRIVILEGES FOR ROLE movies_ddl IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO movies_importer;
```
(The named-table grants in `create-importer-role.sql` are harmless no-ops pre-migration and still
apply locally where tables already exist.)

**3. Key Vault RBAC propagation.**
Step 3 grants the operator *Key Vault Secrets Officer*, then step 6 writes secrets immediately —
RBAC can take minutes to propagate, so a fast run 403s. *Fix:* `secret_set` now retries (10× / 15s)
until the permission is effective.

**Cheap, no-cost hardening — now applied.** These add security without changing the demo bill,
so `azure-setup.sh` now sets them: **`--https-only true`**, **min TLS 1.2**, **FTPS disabled** on
both Web Apps, and **resource-group tags** (`project`/`env`/`managedBy`).

**Deliberately skipped for a demo.** **Key Vault purge protection** is *not* enabled: it's
irreversible and blocks reuse of the vault name for 90 days after `az group delete`, which fights
the spin-up → demo → tear-down loop this project relies on. Enable it for a real environment.

**Still open (documented, not fixed) — production hardening.** IaC (Bicep/Terraform) instead of
imperative CLI; private networking (Private Endpoint/VNet) instead of public Postgres + the broad
"Allow Azure services" rule; a **narrower CI role than Contributor on the RG** (kept broad because
managing Flexible Server firewall rules + Web App config has no tidy built-in least-privilege set,
and getting it wrong silently breaks the pipeline); App Insights / diagnostics; RS256 + JWKS for
JWT (see #5). These are intentional demo-tier trade-offs, not oversights.
