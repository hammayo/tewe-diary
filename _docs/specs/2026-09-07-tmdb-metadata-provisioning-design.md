# TMDB Metadata Provisioning — Design

**Date:** 2026-09-07
**Status:** Draft for review
**Scope:** Add an on-demand pipeline that fetches movie data from TMDB into an
inspectable JSON artefact, imports it into the Postgres `db` container, and
stages the full TMDB payload for later imports and API enhancements.

## Goal

Give the Movies API a repeatable way to populate `movies` and `genres` from
TMDB, while retaining the complete TMDB metadata per movie so future features
can surface more fields without re-crawling TMDB. The `ratings` table is left
untouched — it belongs to real app users.

## Decisions (settled during brainstorming)

| Decision          | Choice                                                                 |
|-------------------|------------------------------------------------------------------------|
| Data source       | TMDB discovery lists (e.g. `/movie/popular`), paged                    |
| Ratings table     | Untouched — no synthetic/aggregate ratings                             |
| Fetch vs load     | Decoupled: fetch → JSON file on demand, then import into the container |
| Metadata staging  | Separate `movie_metadata` table (`tmdb_id` + raw `jsonb`)              |
| Typed columns now | Only `tmdb_id`; everything else stays in `raw` jsonb (YAGNI)           |
| Import mechanism  | Pure SQL via `psql` into the running `db` container (Option A)         |
| Idempotency key   | `tmdb_id` (stable integer), not the reconstructed slug                 |

## Architecture

Three loosely-coupled units, each independently runnable and testable:

```
┌─────────────────┐   NDJSON file    ┌───────────────────┐   psql -f         ┌─────────────┐
│ fetch-tmdb.sh   │ ───────────────▶ │ Resources/        │ ────────────────▶ │ Postgres    │
│ (curl + jq)     │                  │ tmdb-movies.ndjson│  import-movies.sql│ db container│
└─────────────────┘                  └───────────────────┘                   └─────────────┘
   talks to TMDB                      inspectable artefact                   movies / genres
                                                                             / movie_metadata
```

- **Fetch** is the only unit that talks to TMDB. Output: NDJSON on disk.
- **Artefact** is the contract between fetch and load — one JSON object per line.
- **Load** is pure SQL; it only knows the NDJSON shape and the DB schema.

## 1. Schema changes (additive)

Added to `Movies.Application/Database/DbInitializer.cs` as a fourth
`create table if not exists`, following the existing pattern. No existing
table is altered.

```sql
create table if not exists movie_metadata (
    movieid    uuid primary key references movies (id) on delete cascade,
    tmdb_id    bigint not null unique,
    raw        jsonb not null,
    fetched_at timestamptz not null default now()
);

create unique index if not exists movie_metadata_tmdb_id_idx
    on movie_metadata using btree (tmdb_id);
```

- `movieid` is the primary key and FK to `movies` (one metadata row per movie).
- `tmdb_id` is `unique not null` — the idempotency key for re-imports.
- `raw` holds the complete TMDB object so later enhancements read fields out of
  jsonb (and can be promoted to typed columns via future migrations).
- `on delete cascade` keeps metadata consistent with `DeleteByIdAsync`.

## 2. Fetch — `scripts/fetch-tmdb.sh`

Dependencies: `bash`, `curl`, `jq`. Reads `TMDB_API_KEY` from the environment
(loaded from `.env`).

Arguments:
- `--list <popular|top_rated|now_playing>` (default `popular`)
- `--pages <N>` (default `5`; TMDB returns 20 results/page)
- `--out <path>` (default `Resources/tmdb-movies.ndjson`)

Steps:
1. `GET /genre/movie/list?language=en-US` once → build a genre id→name map.
2. For `page` in `1..N`: `GET /movie/<list>?page=<page>&language=en-US`.
3. For each result, emit one NDJSON line:
   ```json
   {"tmdb_id":862,"Title":"Toy Story","YearOfRelease":1995,"Genres":["Animation","Comedy","Family"],"raw":{ ...full TMDB result... }}
   ```
   - `YearOfRelease` = year parsed from `release_date`; results with an empty or
     malformed `release_date` are skipped (year is `not null` in `movies`).
   - `Genres` = `genre_ids` mapped through the lookup; unmapped ids dropped.
4. Basic politeness: sequential paging keeps well under TMDB's rate limit; no
   special throttling needed for the default page counts.

The NDJSON file is a committed/inspectable artefact and is safe to regenerate.

## 3. Load — `scripts/import-movies.sql` + a runner

Runner (documented in README / `run.sh` note, not automated on every boot):

```bash
docker compose cp Resources/tmdb-movies.ndjson db:/tmp/tmdb-movies.ndjson
docker compose exec -T db \
  psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 \
  -f - < scripts/import-movies.sql
```

`import-movies.sql`:

```sql
begin;

create temp table _import (doc jsonb) on commit drop;
\copy _import(doc) from '/tmp/tmdb-movies.ndjson'

-- Rows we have not seen before, keyed on tmdb_id.
create temp table _new on commit drop as
select
    gen_random_uuid() as id,
    (doc->>'tmdb_id')::bigint as tmdb_id,
    doc->>'Title' as title,
    (doc->>'YearOfRelease')::int as yearofrelease,
    -- Slug MUST match Movies.Application/Models/Movie.GenerateSlug:
    --   remove chars outside [0-9A-Za-z _-], lowercase, spaces -> '-', append '-year'
    replace(lower(regexp_replace(doc->>'Title', '[^0-9A-Za-z _-]', '', 'g')), ' ', '-')
        || '-' || (doc->>'YearOfRelease') as slug,
    doc->'Genres' as genres,
    doc->'raw' as raw
from _import i
where not exists (
    select 1 from movie_metadata m where m.tmdb_id = (i.doc->>'tmdb_id')::bigint
);

insert into movies (id, slug, title, yearofrelease)
select id, slug, title, yearofrelease from _new;

insert into genres (movieid, name)
select n.id, g.value
from _new n, jsonb_array_elements_text(n.genres) as g(value);

insert into movie_metadata (movieid, tmdb_id, raw)
select id, tmdb_id, raw from _new;

commit;
```

Properties:
- **Idempotent:** re-running imports only movies whose `tmdb_id` is new.
- **Atomic:** one transaction; `ON_ERROR_STOP=1` aborts cleanly on failure.
- **Ratings untouched.**

## 4. Config

- `.env`: add `TMDB_API_KEY=<real key>` (gitignored; already open in the IDE).
- `.env.example`: add `TMDB_API_KEY=your-tmdb-v3-api-key` placeholder.

## Slug parity (the known risk of Option A)

The API stores `slug` and looks movies up by it (`GetBySlugAsync`). The SQL
slug expression must stay byte-identical to `Movie.GenerateSlug`, or a by-slug
lookup could miss an imported movie. Mitigations:

1. The SQL expression above mirrors the C# exactly (regex, lowercase, space→`-`,
   `-year`). Only ASCII letters survive the regex, so `lower()` and C#
   `ToLower()` agree.
2. **Parity test:** a test that runs `Movie.GenerateSlug` and the SQL expression
   over a representative sample (including titles with punctuation, mixed case,
   digits, multiple spaces) and asserts equality. This catches drift if either
   implementation changes later.

Because dedupe is on `tmdb_id`, a slug mismatch would never cause duplicate
imports — the only exposure is a by-slug lookup, which the parity test guards.

## Testing

- **Parity test** (above) — C# slug vs SQL slug over an edge-case sample.
- **Import idempotency** — apply `import-movies.sql` twice against a test
  database with a small fixture NDJSON; assert row counts in `movies`,
  `genres`, `movie_metadata` are identical after the second run.
- **Fetch shaping** — unit-check the `jq` transform against a captured TMDB
  sample response (fixture), asserting the NDJSON fields and genre mapping.
  Kept as a fixture-driven test so it needs no live API key in CI.

## Out of scope (future enhancements this enables)

- Promoting fields from `movie_metadata.raw` (overview, poster_path,
  vote_average, runtime) into typed columns + API responses.
- New API endpoints exposing TMDB metadata.
- Automating the import as a docker-compose one-shot service (the earlier
  "Option 3"): trivial to add later — a `movies-seeder` profile service that
  runs the same script after `db` is healthy.
- Incremental refresh / updating existing movies' metadata (currently new-only).

## Files touched

| File | Change |
|---|---|
| `Movies.Application/Database/DbInitializer.cs` | + `movie_metadata` table & index |
| `scripts/fetch-tmdb.sh` | NEW — TMDB → NDJSON |
| `scripts/import-movies.sql` | NEW — NDJSON → Postgres, idempotent |
| `.env` | + `TMDB_API_KEY` |
| `.env.example` | + `TMDB_API_KEY` placeholder |
| tests (project TBD in plan) | slug parity, import idempotency, fetch shaping |
| `README` / docs | document the fetch + import commands |
