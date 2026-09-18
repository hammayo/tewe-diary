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
