# Database Migrations Plan (structural items 1 & 2)

Status: **Draft for review** — no code changed yet.
Date: 2026-09-14

## Goals

1. Replace startup DDL (`DbInitializer` + `create table if not exists`) with **versioned,
   ordered migrations** that have history, are safe to re-run, and support forward schema
   change (alter/backfill/drop), not just first-time creation.
2. Make migrations the **single source of truth for schema**, so the app and the
   `scripts/` import pipeline can no longer drift apart.

## Current state (what we're replacing)

- `Movies.Application/Database/DbInitializer.cs` creates, at every boot, with
  `if not exists`: `movies`, unique index `movies_slug_idx` (`create ... concurrently`),
  `genres`, `ratings`, `movie_metadata`.
- `Program.cs` calls `DbInitializer.InitializeAsync()` inside a 10-attempt retry loop
  (DB-not-ready backoff) after `app.StartAsync()`.
- The import pipeline (`scripts/import-transform.sql`, `import-movies.sql`) only
  `INSERT`s / transforms — it **assumes** the schema exists (`movie_metadata.tmdb_id`
  unique, `movies.slug`, `genres`). So schema is defined in one place today, but
  **consumed, uncontrolled, in another** — that is the drift risk item 2 targets.
- Tests instantiate `new DbInitializer(factory).InitializeAsync()` directly in four
  places: `DbInitializerTests`, `MovieRepositoryTests`, `ImportTransformTests` (x3).

## Tool choice: FluentMigrator

Chosen: **FluentMigrator** (`FluentMigrator`, `FluentMigrator.Runner`,
`FluentMigrator.Runner.Postgres`; `dotnet-fm` CLI for the pipeline).

Why, given an Azure slot-swap deploy and an actively evolving schema:
- **Rollback (`Down()`)** and first-class `Alter.*` / `Create.Index` / `Create.ForeignKey`
  make forward schema change far cheaper than hand-writing every `ALTER`.
- **`VersionInfo`** table tracks applied migrations, in order, once each.
- **`TransactionBehavior.None`** cleanly handles the `CONCURRENTLY` case (see gotcha).
- **Dual-mode** out of the box: in-process `IMigrationRunner` for local boot, `dotnet-fm`
  CLI for the Azure pipeline step — matches the runner design below.

Style decision (recommended): **baseline migration uses `Execute.Sql`** with the verbatim
current DDL (keeps SQL as the source of truth for what exists), and **future incremental
migrations use the fluent DSL** (`Alter`/`Create`/`Delete`) where `Down()` and readability
pay off. This keeps the raw-SQL spirit of the codebase while gaining FluentMigrator's
evolution ergonomics.

Rejected alternatives (note in PR, not blocking):
- **DbUp** — pure `.sql`, minimal deps, but no `Down()`, no `Alter` helpers, and you
  hand-write every schema change. Fine for create-once; weaker for an evolving schema.
- **EF Core migrations** — would drag EF into an intentionally Dapper-only app. No.

### Gotcha that drives design — `CREATE INDEX CONCURRENTLY`

`movies_slug_idx` is currently `create unique index concurrently`. `CONCURRENTLY`
**cannot run inside a transaction**, and FluentMigrator wraps each migration in one by
default. Two options:

- **Preferred:** drop `CONCURRENTLY` in the baseline migration. It only exists to avoid
  locking a table that has live traffic; for initial creation (empty/greenfield table)
  a plain unique index is correct and simpler.
- If a future migration ever needs `CONCURRENTLY` on a populated table, mark that migration
  `[Migration(n, TransactionBehavior.None)]` and run the raw statement via `Execute.Sql`.

## Design

### Where migrations live

Migration classes live in `Movies.Application`, discovered by assembly scan, with a single
entrypoint helper so **startup and tests use the exact same migration path** (no second
source of truth):

```
Movies.Application/
  Database/
    Migrations/
      M0001_InitialSchema.cs           ([Migration(1)], Execute.Sql(baseline DDL))
      MigrationRunner.cs               (public static void Run(connectionString, logger?))
```

`MigrationRunner.Run` builds a service provider with
`.AddFluentMigratorCore().ConfigureRunner(r => r.AddPostgres()
.WithGlobalConnectionString(cs).ScanIn(typeof(MigrationRunner).Assembly).For.Migrations())`,
resolves `IMigrationRunner`, and calls `MigrateUp()` (throws on failure).

For the Azure pipeline the **`dotnet-fm` CLI** runs the same assembly's migrations — no
duplicate logic. `DbInitializer` is **deleted** once callers move over (see test impact).

### Baseline migration content — `M0001_InitialSchema`

`[Migration(1)]` whose `Up()` calls `Execute.Sql(...)` with a verbatim port of today's DDL,
keeping `if not exists` so it is a **safe baseline** that applies cleanly to:
- a fresh database, and
- an existing deployed database whose tables were already created by the old
  `DbInitializer` (FluentMigrator records version 1 as applied; the `if not exists` guards
  make the SQL a no-op).

`Down()` drops the tables (real rollback for a fresh environment). Only change vs today's
DDL: unique index **without** `concurrently` (see gotcha).

After M0001, later migrations use the fluent DSL (`Alter`/`Create`/`Delete`) with real
`Up()`/`Down()` — that is what unlocks cheap schema evolution.

### Program.cs wiring

Replace the `DbInitializer` retry loop body with `MigrationRunner.Run(connectionString, ...)`
(guarded by `Database:MigrateOnStartup`, default true locally / false in Azure), keeping the
existing 10-attempt / 2s-backoff `NpgsqlException + SocketException` handling (DB-not-ready on
boot). Local behaviour is unchanged: app comes up, waits for Postgres, migrates, serves.

### Single source of truth (item 2)

- Migrations own **all** schema DDL. `scripts/import-transform.sql` and
  `import-movies.sql` keep only `INSERT`/transform logic (they already do — this just makes
  the boundary explicit and documented at the top of each script).
- **Drift guard already exists**: `ImportTransformTests` runs the schema setup and then the
  import transform end-to-end. Repointed at `MigrationRunner`, it fails the build if a
  migration ever changes a column the import depends on. We keep and lean on this test.

### Out of scope but flagged (related single-source-of-truth debt)

The **slug formula is duplicated in three places** and must stay byte-identical:
`Movie.GenerateSlug()` (C#), and the `regexp_replace(...)` in `import-transform.sql`.
This is *application* logic, not schema, so it's out of scope for this plan — but it is the
same class of drift risk. Recommend a follow-up: a single documented slug contract with a
test asserting the SQL and C# produce the same slug for a shared fixture set. Noted here so
it isn't lost.

## Test impact (must be done in the same change)

Four call sites construct `DbInitializer` directly. Migrate them to the shared runner:

- `DbInitializerTests.cs` → rename/retarget to assert `MigrationRunner.Run` produces the
  expected tables (keep the `movie_metadata` assertion).
- `MovieRepositoryTests.cs`, `ImportTransformTests.cs` (x3) → replace
  `new DbInitializer(factory).InitializeAsync()` with `MigrationRunner.Run(fx.ConnectionString)`.
- `Movies.Application.Tests.csproj` gains FluentMigrator (transitively via
  `Movies.Application`) — no new direct test dependency expected.

Because each Testcontainers fixture is a fresh DB, `MigrateUp()` runs M0001 from clean
every time.

## Step-by-step execution order (for the implementation PR)

1. Add `FluentMigrator`, `FluentMigrator.Runner`, `FluentMigrator.Runner.Postgres` to
   `Movies.Application` (Central Package Management entries).
2. Add `MigrationRunner.cs` + `M0001_InitialSchema.cs` (baseline via `Execute.Sql`).
3. Repoint the four test call sites at `MigrationRunner`; delete `DbInitializer` +
   `DbInitializerTests`' obsolete assertions (or convert it to a MigrationRunner test).
4. Rewire `Program.cs` to call `MigrationRunner.Run` inside the existing retry loop, gated by
   `Database:MigrateOnStartup`; remove the `DbInitializer` service registration in
   `ApplicationServiceCollectionExtensions`.
5. Add a one-line header to `import-transform.sql` / `import-movies.sql` stating that schema
   is owned by migrations and these scripts must not define structure.
6. `dotnet build` + `dotnet test` (Testcontainers spins Postgres) — expect all green.
7. (Deploy, separate follow-up) add `dotnet-fm` migration step + slot-swap to the Azure
   pipeline; add `DatabaseOptions` + `SSL Mode` + Key Vault wiring.

## Risks & mitigations

- **Existing prod DB already has tables** → `if not exists` baseline makes M0001 a no-op;
  FluentMigrator records version 1 as applied. Safe.
- **Concurrent migration runs** → not a concern in Azure, because migration runs **once** as a
  pipeline step (not from the web containers). For the local in-process path, boots are
  single-instance. If a shared runner is ever needed, wrap `MigrateUp()` in a Postgres
  advisory lock (`pg_advisory_lock`) — FluentMigrator does not take one automatically.
- **`CONCURRENTLY` in transaction** → handled by dropping it in the baseline (see gotcha);
  future need → `[Migration(n, TransactionBehavior.None)]`.

## Cloud deployment (Azure / AWS) impact

Running on managed platforms changes the recommendation on *where* migrations run. The
local, single-instance "migrate in-process at boot" model has three problems in the cloud:

### 1. Multiple replicas + rolling deployments
Azure App Service / Container Apps and AWS ECS/Fargate/EKS run **N replicas**, and a rolling
deploy means **old and new app versions run concurrently** against one database during the
rollout.
- *N-replica race:* if every instance self-migrates at boot, N runners hit the schema at
  once. Neither FluentMigrator nor DbUp locks automatically, so this must be avoided — which
  is exactly why migration is moved to a single pipeline step below.
- *Version skew:* during a rollout the old image is still serving while the new schema is
  applied. This makes **backward-compatible (expand/contract) migrations mandatory** for
  zero-downtime: additive first (add nullable column / new table), deploy code that writes
  both, backfill, then a *later* release drops the old shape. A migration that renames/drops
  in one step will break the still-running old instances. This reframes the "clean forward
  DDL" idea below.

### 2. Least privilege
In-process migration means the **app's runtime DB identity needs DDL rights**
(CREATE/ALTER/DROP). Best practice on RDS/Azure PG is a runtime user with **DML only** and a
separate, elevated migration role used once per release. That argues for running migrations
as a **distinct step**, not inside the web process.

### 3. Startup probes vs migration time
Current `Program.cs` calls `app.StartAsync()` and then migrates, so the app reports listening
(and `/_health` — which only checks *connectivity*, not *schema*) **before the schema
exists**. On K8s/App Service, readiness would go green and route traffic to an app whose DB
calls then fail. Either the health check must gate on migration completion, or migration must
finish before the app is registered as ready.

### Managed-Postgres specifics (both clouds)
- **TLS is required by default** on Azure Database for PostgreSQL (Flexible Server) and
  AWS RDS/Aurora. The current connection-string builder sets no `SSL Mode` — it will fail
  against managed PG. Add `SSL Mode=Require;Trust Server Certificate=...` (or proper CA
  validation). This makes structural item 7 (`DatabaseOptions`) worth doing alongside.
- **Secrets:** connection string / password come from **Azure Key Vault** or **AWS Secrets
  Manager / SSM Parameter Store**, injected as env vars or via references — not `.env`. The
  existing `Database:ConnectionString` override slot already supports this; `.env` stays
  local-dev only.
- **`gen_random_uuid()`** (used in `import-transform.sql`) is built-in on PostgreSQL 13+, so
  no `pgcrypto` extension privileges are needed on managed instances. Fine as-is.
- **Testcontainers in CI:** needs a Docker daemon on the build agent — GitHub Actions and
  Azure DevOps hosted agents are fine; AWS CodeBuild needs privileged mode.

### Revised recommendation
`MigrationRunner.Run(connectionString)` (in-process `IMigrationRunner.MigrateUp()`) plus the
**`dotnet-fm` CLI** pointed at the same `Movies.Application` assembly — so the *same* migration
classes execute two ways with no duplicate logic:
- **Local dev:** in-process call at boot for zero-friction `./run.sh` / `dotnet run`.
- **Cloud:** `dotnet-fm migrate` as a **dedicated pre-deploy step** with the elevated
  migration role, gated *before* the new app version receives traffic.

Guard the in-process path behind config (e.g. `Database:MigrateOnStartup`, default `true`
locally, `false` in cloud) so production never self-migrates from the web process.

## Target platform: Azure App Service — Web App for Containers

**Decided:** the app ships as a Docker image to Azure App Service (Web App for Containers),
backed by **Azure Database for PostgreSQL Flexible Server**. Concrete decisions this pins down:

### How migrations run in this target
App Service has no init-container/job primitive, so migrations run as an **explicit CI/CD
pipeline step**, not from the web container:

1. Build & push the app image to ACR (Azure Container Registry).
2. **Migration step:** run **`dotnet-fm migrate`** against the built `Movies.Application`
   assembly using the **elevated migration credential** from Key Vault — either as a step on
   the pipeline agent, or as a one-off **Azure Container Instance** using the same image with a
   command override. Same migration classes as local; no separate console project needed.
3. **Deploy to a staging slot**, warm it, then **slot-swap** staging → production for
   near-zero-downtime cutover.

`MigrateOnStartup=false` is set in the App Service configuration, so the running web
container never migrates. Because migration runs once in the pipeline, the N-replica race and
the least-privilege problem both disappear — the web app's runtime identity gets **DML-only**.

### Zero-downtime with slots ⇒ expand/contract is required
A slot swap means the old production image serves until the instant of swap, while the new
schema is already applied. So migrations **must be backward-compatible** (add before remove;
a drop/rename ships a release later). Confirms open question 2 in favour of expand/contract.

### Connectivity & secrets (Azure specifics)
- **TLS:** Flexible Server requires TLS. The connection string must set
  `SSL Mode=Require` (or `VerifyFull` with the Azure CA). Both the app and the migration step
  need this. → do structural item 7 (`DatabaseOptions`) here so SSL is set in one place.
- **Secrets:** store the connection string / passwords in **Key Vault**; expose to App Service
  via **Key Vault references** in app settings (surfaces as the existing
  `Database:ConnectionString` override). `.env` stays strictly local-dev.
- **Two DB roles:** app setting carries the **DML-only** connection string; the pipeline's
  migration step uses a **separate DDL-capable** credential (Key Vault secret scoped to CI).
- **Networking caveat:** if Flexible Server is **private (VNet-integrated)**, the pipeline
  agent can't reach it directly. Options: run the migration step as an **ACI in the VNet**, use
  a **self-hosted agent** in the VNet, or (least preferred) temporarily allow the agent IP via
  the firewall. If Flexible Server is public with firewall rules, allow the agent/ACI egress
  IP. **This needs deciding before implementation** (see open questions).
- **App container health:** App Service health check should point at `/_health`; but note it
  only tests connectivity. With migrations moved to the pipeline (run before swap), the schema
  is guaranteed present before the slot goes live, so the earlier "ready before schema exists"
  race is resolved by construction.

### CI runner
Testcontainers-based tests need a Docker daemon — use GitHub Actions or Azure DevOps
Microsoft-hosted agents (both provide Docker); no privileged-mode workaround needed.

## Decisions locked
- **Target:** Azure App Service (Web App for Containers) + Azure Database for PostgreSQL
  Flexible Server.
- **Tool:** FluentMigrator (baseline via `Execute.Sql`, fluent DSL for later changes).
- **Runner:** dual-mode — in-process `MigrateUp()` for local (`MigrateOnStartup=true`),
  `dotnet-fm` CLI step in the pipeline for Azure (`MigrateOnStartup=false`).
- **Migration style:** expand/contract backward-compatible (forced by slot-swap deploys).
- **Scope add:** structural item 7 (`DatabaseOptions` + `SSL Mode`) comes into this work,
  because managed PG needs TLS and it belongs in one place.

## Open questions for you

1. **Flexible Server networking:** public-with-firewall or private/VNet-integrated? This
   decides how the pipeline migration step reaches the DB (agent IP allow-list vs ACI/
   self-hosted agent inside the VNet). Blocks the deploy design, not the code.
2. **Pipeline:** GitHub Actions or Azure DevOps? (Changes only where I put the migration +
   slot-swap steps, not the app code.)
3. **Baseline strategy:** keep `if not exists` in 0001 (safe for the existing DB), or treat
   this as greenfield and write clean DDL (simpler, but you'd need to reset/mark existing
   environments)?
4. Include the slug-consolidation follow-up in this work, or track it separately?
