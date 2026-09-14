# TMDB Import — Re-runnable Refresh & Azure Pipeline

Status: **Draft for review** — no code changed yet.
Date: 2026-09-14
Related: `2026-09-07-tmdb-metadata-provisioning-design.md` (original import),
`2026-09-14-database-migrations-plan.md` (migrations + Azure deploy).

## What's being asked

1. Make the TMDB import **re-runnable to refresh data** — re-running updates existing movies
   with fresh TMDB title/year/genres/raw metadata, not just insert brand-new ones.
2. **Maintain manual data inserts** — movies created through the API (no TMDB origin) must
   never be modified or deleted by an import/refresh. Ratings stay untouched (existing rule).
3. **Handle the Azure pipeline** — the load must work against Azure Database for PostgreSQL
   Flexible Server (remote, TLS, Key Vault secrets), not only the local `db` container.

## Current behaviour (what we're changing)

- `import-transform.sql` builds `_new` = staged rows whose `tmdb_id` is **not already** in
  `movie_metadata` **and** whose slug is not already in `movies`, then INSERTs. So it is
  **insert-only / new-only**: re-running is a safe no-op for known movies but does **not**
  refresh changed data. (Refresh was an explicit non-goal in the 2026-09-07 design.)
- `import-movies.sql` wraps the load in a transaction and uses psql meta-commands:
  `\copy _import(doc) from '/tmp/...ndjson'` and `\i /tmp/import-transform.sql`. This assumes
  a local file path inside a reachable `db` container — **not viable against Flexible Server**.

## Key facts this design leans on

- **Manual vs TMDB origin is already distinguishable:** TMDB movies have a `movie_metadata`
  row (`tmdb_id`); manual/API movies do not. That is the discriminator for "don't touch."
- **Identity must be preserved on refresh:** `ratings.movieid` and `genres.movieid` are FKs to
  `movies.id`. Refresh must **UPDATE the existing `movies` row in place** (same `id`), never
  delete+reinsert, or it would orphan/lose ratings.
- **`import-transform.sql` is already pure SQL**; only the wrapper uses psql meta-commands.
- **The `.NET` load path is already proven:** `ImportTransformTests` loads NDJSON into the
  temp table via Npgsql/Dapper and runs the transform — i.e. the importer already exists in
  test form.

---

## Part 1 — Re-runnable refresh (rewrite `import-transform.sql`)

Change from insert-only to **upsert keyed on `tmdb_id`**, preserving movie identity and
protecting manual data. All steps run in one transaction; `_import(doc jsonb)` is pre-populated
by the loader (Part 2).

### Steps

1. **Stage & dedupe** — parse each `_import.doc` into
   `(tmdb_id, title, yearofrelease, slug, genres, raw)`; keep one row per `tmdb_id`
   (`row_number() … partition by tmdb_id`). Slug computed with the **same formula** as
   `Movie.GenerateSlug` (see slug-parity note). *No* `not exists` filters now — we want both
   new and existing.
2. **Resolve identity** — `left join movie_metadata using (tmdb_id)` to pick up the existing
   `movieid` for known movies; `gen_random_uuid()` for new ones.
3. **Slug-collision guard (protects manual data)** — reject any staged row whose target slug is
   already held by a **different** movie (`movies.slug = staged.slug and movies.id <> resolved
   movieid`). That "different movie" may be a manual insert or another TMDB movie; either way we
   must not steal its slug (there is a unique index on `movies.slug`). Skipped rows are
   collected and surfaced via `raise notice` so the pipeline logs show them.
4. **Upsert `movies`** —
   `insert into movies (id, slug, title, yearofrelease) select … from resolved
   on conflict (id) do update set slug = excluded.slug, title = excluded.title,
   yearofrelease = excluded.yearofrelease;`
   New rows insert; known rows (id from `movie_metadata`) conflict on `id` and update in place,
   keeping ratings/FKs intact. Slug pre-filtering (step 3) guarantees no unique-slug violation.
5. **Upsert `movie_metadata`** —
   `insert into movie_metadata (movieid, tmdb_id, raw, fetched_at) select id, tmdb_id, raw,
   now() … on conflict (movieid) do update set raw = excluded.raw, fetched_at = now();`
6. **Refresh genres for imported movies only** — `delete from genres where movieid in (select
   id from resolved)` then re-insert from the staged genre arrays. Scoped to the batch's
   resolved ids, so **manual movies' genres are never touched**.
7. **Ratings** — never referenced. Untouched by construction.
8. **Report** — `raise notice` counts: inserted / updated / genres-refreshed / slug-skipped.

### Why manual inserts are safe

- The only `movies` rows written are those with `id` either newly generated (new `tmdb_id`) or
  resolved from `movie_metadata`. Manual movies have no `movie_metadata` row → never in the
  set → never updated or deleted.
- Genre deletion is scoped to resolved ids only.
- The slug guard stops a TMDB movie overwriting a manual movie that happens to share a slug.

### Edge cases

- **Same movie, changed title/year** → slug changes; UPDATE sets the new slug (allowed once the
  guard confirms it's free). Ratings preserved (same `id`).
- **Two TMDB movies → identical slug** (same title+year) → dedupe keeps the lowest `tmdb_id`
  (as today); the other is reported skipped.
- **Manual movie later gaining a `tmdb_id`** → out of scope; there is no manual→TMDB linking
  path. Documented, not handled.

### Slug-parity dependency (previously flagged)

Refresh recomputes slugs in SQL when title/year change, so the SQL formula and
`Movie.GenerateSlug()` **must stay byte-identical** — this is the three-way duplication flagged
in the migrations plan. Recommend landing the **slug-parity test** (shared fixtures asserting
C# and SQL agree) *with* this work, since refresh makes drift actively corrupting rather than
merely cosmetic.

---

## Part 2 — Azure-compatible load

`import-transform.sql` stays pure SQL (the transform is the source of truth). Only the
**loader** — getting NDJSON into `_import` and invoking the transform — needs an Azure path.

### Recommended: promote the test loader into a small .NET importer (Option B)

A `MovieImporter` in `Movies.Application` (the logic `ImportTransformTests` already contains):
open a connection (via the shared `DatabaseOptions`, so TLS/secret handling is identical to the
app), `create temp table _import`, stream the NDJSON with Npgsql `COPY`
(`BeginTextImport`)/batched inserts, run the embedded `import-transform.sql`, commit.

- **Azure-native:** no `psql` binary in the pipeline image; reuses TLS + Key Vault wiring from
  the migrations plan's `DatabaseOptions`.
- **Testable:** the existing `ImportTransformTests` become tests of the real importer, not a
  parallel copy of the load logic (removes duplication).
- **Invocation:** a thin console entrypoint (or a verb on the same tool used for `dotnet-fm`),
  run as a pipeline step / Azure Container Instance with the importer DB role.

### Alternative: psql `\copy` from the pipeline (Option A, least change)

Keep SQL-only, run `psql` from the pipeline agent or a `postgres-client` container image against
Flexible Server: `\copy` is **client-side**, so it reads the NDJSON artefact on the agent and
streams it to the remote DB over TLS — no container file-copy needed. Requires `psql` available
in the pipeline and `sslmode=require` in the connection. `import-transform.sql` is invoked with
`\i` and stays unchanged.

Recommendation: **Option B** — it removes the psql dependency, unifies with the migration
tooling and `DatabaseOptions`, and turns the existing tests into real coverage. Option A is the
fallback if we want zero new C#.

### Pipeline shape (Azure App Service target)

Ordering matters — **schema before data**:

```
fetch (curl+jq, TMDB_API_KEY from Key Vault)  →  tmdb-movies.ndjson (pipeline artefact)
migrate (dotnet-fm, DDL role)                 →  schema up to date      [from migrations plan]
import  (MovieImporter, importer DML role)    →  upsert movies/genres/metadata
```

- **Separate concerns / privileges:** the import is a **DML** job (INSERT/UPDATE/DELETE + CREATE
  TEMP), *not* DDL. Give it a dedicated **`movies_importer`** role — distinct from both the
  app's runtime role and the migration DDL role. It must run **after** migrations.
- **Re-runnable (ad-hoc):** it's an idempotent upsert, safe to re-run any time with no reset.
  Triggered manually for now (decision 2); could be scheduled later with no code change.
- **Secrets/TLS:** importer connection string from Key Vault, `SSL Mode=Require` — same
  `DatabaseOptions` path as the app and migrations.

### Interaction with the API output cache

The API evicts the `"movies"` OutputCache tag only on writes **through** the API. A bulk import
writes directly to the DB, bypassing that, so cached list/detail responses would otherwise be
stale until expiry. **Resolved by decision 3:** the pipeline calls the admin evict-cache
endpoint after import. See "Cache eviction endpoint" under Decisions locked for the endpoint,
auth, and the multi-instance caveat.

---

## Test impact

- Repoint `ImportTransformTests` at the new `MovieImporter` (Option B) so the tests exercise the
  real load path, not a parallel copy.
- **New refresh tests** (Testcontainers, one fresh DB each):
  - re-import same batch twice → second run updates 0 identities, row counts stable (idempotent).
  - change a staged movie's title/year → existing `movies` row updated in place, **same `id`**,
    **ratings for that movie preserved**.
  - a **manual** movie (inserted with no `movie_metadata`) is untouched across an import; its
    genres and ratings intact.
  - slug collision: staged TMDB movie whose slug matches a manual movie → skipped + reported,
    manual movie unchanged.
- **Evict-cache endpoint test:** unauthenticated call rejected; valid `x-api-key` returns success
  and triggers `EvictByTagAsync("movies")`.
- **Slug-parity test** (C# vs SQL) landed alongside (see Part 1).

## Decisions locked

1. **Loader:** Option B — .NET `MovieImporter` (Npgsql `COPY` + shared `DatabaseOptions`).
2. **Cadence:** **ad-hoc only** — manually-triggered pipeline run. No scheduled/nightly job.
3. **Cache:** add an **authenticated admin evict-cache endpoint**; the pipeline calls it after
   a successful import (see below).
4. **Role:** introduce a dedicated **`movies_importer`** DML role.

### Cache eviction endpoint (decision 3)

- New endpoint, e.g. `POST /api/admin/cache/evict` (versioned like the rest), which calls
  `IOutputCacheStore.EvictByTagAsync("movies", …)` — the same tag the controllers already use.
- **Auth:** protect it with the existing **API key** (`ApiKeyAuthFilter` / `x-api-key`) so the
  pipeline can call it machine-to-machine without minting a JWT; or the `AdminUser` policy if we
  prefer JWT. Recommend the API key for a pipeline caller.
- **Pipeline step:** after the import step succeeds, `curl -sf -X POST …/api/admin/cache/evict
  -H "x-api-key: <from Key Vault>"`. Non-fatal if it fails (cache still expires naturally).
- **Multi-instance caveat:** App Service `OutputCache` is **in-memory per instance** by default,
  so an evict call only clears the instance that handles it. This is fine while App Service runs
  **a single instance**. If/when it scales out, either move OutputCache to a **distributed store
  (Redis)** so eviction is global, or accept per-instance natural expiry. Flagged for whoever
  enables scale-out; not blocking for the initial single-instance deploy.

### Cadence note (decision 2)

Because refresh is an idempotent upsert it *could* be scheduled later with no code change, but
for now it is **operator-triggered only**. No cron/Container Apps job is created.
