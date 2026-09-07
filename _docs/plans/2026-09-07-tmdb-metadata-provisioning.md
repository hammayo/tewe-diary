# TMDB Metadata Provisioning Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an on-demand pipeline that fetches TMDB movie data into an inspectable NDJSON file, imports it idempotently into the Postgres `db` container, and stages the full TMDB payload per movie for later enhancements.

**Architecture:** Three loosely-coupled units — a `curl`+`jq` fetch script (TMDB → NDJSON), a pure-SQL import (`\copy` + transform) run via `psql` into the running container, and an additive `movie_metadata` table. Dedupe is on TMDB's stable integer `tmdb_id`; the `ratings` table is never touched. The transformation SQL is factored into its own file so it can be unit-tested without `psql`'s `\copy`.

**Tech Stack:** .NET 9, Dapper 2.1.79, Npgsql 10.0.3, PostgreSQL (`postgres:latest`), `bash`/`curl`/`jq`, xUnit + Testcontainers.PostgreSql for integration tests.

**Spec:** `_docs/specs/2026-09-07-tmdb-metadata-provisioning-design.md`

## Global Constraints

- Target framework: `net9.0` for all projects (matches existing `Movies.Application`, `Movies.Api`).
- Reuse existing types: `IDbConnectionFactory` / `NpgsqlConnectionFactory` and `DbInitializer` in namespace `Movies.Application.Database`; the `Movie` model in `Movies.Application.Models`.
- Postgres image: `postgres:latest` (matches `docker-compose.yml`); `gen_random_uuid()` is built-in (pg13+).
- The `ratings` table MUST NOT be read or written by any task.
- Idempotency key is `movie_metadata.tmdb_id` (a `bigint`), never the slug.
- The import slug expression MUST stay byte-identical to `Movie.GenerateSlug` (`Movies.Application/Models/Movie.cs`): remove chars outside `[0-9A-Za-z _-]`, lowercase, replace spaces with `-`, append `-<year>`.
- SQL keywords lower-case to match the existing `DbInitializer` style.

---

### Task 1: Test project + `movie_metadata` schema

Adds a new integration-test project (there is none yet) and the additive schema. The redundant standalone unique index from the spec is dropped: the column-level `unique` on `tmdb_id` already creates a unique btree index.

**Files:**
- Create: `Movies.Application.Tests/Movies.Application.Tests.csproj`
- Create: `Movies.Application.Tests/PostgresFixture.cs`
- Create: `Movies.Application.Tests/DbInitializerTests.cs`
- Modify: `Movies.Application/Database/DbInitializer.cs` (append fourth table)
- Modify: `TeweRestApi.sln` (add project)

**Interfaces:**
- Consumes: `NpgsqlConnectionFactory(string connectionString)`, `DbInitializer(IDbConnectionFactory)`, `DbInitializer.InitializeAsync()`.
- Produces: `PostgresFixture` (xUnit `ICollectionFixture`) exposing `string ConnectionString`; the collection name `"postgres"` reused by later test tasks. New table `movie_metadata(movieid uuid pk, tmdb_id bigint unique not null, raw jsonb not null, fetched_at timestamptz default now())`.

- [ ] **Step 1: Create the test project and add it to the solution**

```bash
dotnet new xunit -n Movies.Application.Tests -f net9.0
dotnet sln TeweRestApi.sln add Movies.Application.Tests/Movies.Application.Tests.csproj
dotnet add Movies.Application.Tests reference Movies.Application/Movies.Application.csproj
dotnet add Movies.Application.Tests package Testcontainers.PostgreSql
dotnet add Movies.Application.Tests package Dapper
dotnet add Movies.Application.Tests package Npgsql
```

- [ ] **Step 2: Write the shared Postgres fixture**

Create `Movies.Application.Tests/PostgresFixture.cs`:

```csharp
using Testcontainers.PostgreSql;
using Xunit;

namespace Movies.Application.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:latest")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
```

- [ ] **Step 3: Write the failing schema test**

Create `Movies.Application.Tests/DbInitializerTests.cs`:

```csharp
using Dapper;
using Movies.Application.Database;
using Xunit;

namespace Movies.Application.Tests;

[Collection("postgres")]
public class DbInitializerTests
{
    private readonly PostgresFixture _fx;

    public DbInitializerTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task InitializeAsync_creates_movie_metadata_table()
    {
        var factory = new NpgsqlConnectionFactory(_fx.ConnectionString);
        await new DbInitializer(factory).InitializeAsync();

        using var connection = await factory.CreateConnectionAsync();
        var columns = (await connection.QueryAsync<string>("""
            select column_name from information_schema.columns
            where table_name = 'movie_metadata'
            order by column_name
            """)).ToList();

        Assert.Equal(new[] { "fetched_at", "movieid", "raw", "tmdb_id" }, columns);
    }
}
```

- [ ] **Step 4: Run the test, verify it fails**

Run: `dotnet test Movies.Application.Tests --filter InitializeAsync_creates_movie_metadata_table`
Expected: FAIL — assertion returns an empty column list (table does not exist yet). (Requires Docker running for Testcontainers.)

- [ ] **Step 5: Add the `movie_metadata` table to `DbInitializer`**

In `Movies.Application/Database/DbInitializer.cs`, after the `ratings` table block (before the method closes), append:

```csharp
        await connection.ExecuteAsync("""
            create table if not exists movie_metadata (
            movieid UUID primary key references movies (id) on delete cascade,
            tmdb_id BIGINT not null unique,
            raw JSONB not null,
            fetched_at TIMESTAMPTZ not null default now());
        """);
```

- [ ] **Step 6: Run the test, verify it passes**

Run: `dotnet test Movies.Application.Tests --filter InitializeAsync_creates_movie_metadata_table`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add Movies.Application.Tests TeweRestApi.sln Movies.Application/Database/DbInitializer.cs
git commit -m "feat: add movie_metadata table and integration test project"
```

---

### Task 2: Import transformation SQL + idempotency & slug-parity tests

The transformation is a pure-SQL file (no `psql` meta-commands) so it runs both from the `psql` runner (Task 3) and directly from tests via Npgsql. It assumes a temp table `_import(doc jsonb)` is already populated.

**Files:**
- Create: `scripts/import-transform.sql`
- Create: `Movies.Application.Tests/ImportTransformTests.cs`
- Create: `Movies.Application.Tests/Fixtures/import-sample.ndjson`

**Interfaces:**
- Consumes: `movie_metadata`, `movies`, `genres` schema from Task 1; `Movie.GenerateSlug` from `Movies.Application.Models`.
- Produces: `scripts/import-transform.sql` — given `_import(doc jsonb)` rows, inserts new `movies` + `genres` + `movie_metadata`, deduped on `tmdb_id`.

- [ ] **Step 1: Create the test fixture NDJSON**

Create `Movies.Application.Tests/Fixtures/import-sample.ndjson` (two movies, one with punctuation to exercise slug logic):

```
{"tmdb_id":862,"Title":"Toy Story","YearOfRelease":1995,"Genres":["Animation","Comedy"],"raw":{"id":862,"vote_average":8.0}}
{"tmdb_id":585,"Title":"Monsters, Inc.","YearOfRelease":2001,"Genres":["Animation","Family"],"raw":{"id":585,"vote_average":7.8}}
```

Set this file to copy to the test output directory — in `Movies.Application.Tests.csproj` add inside a new `<ItemGroup>`:

```xml
    <None Update="Fixtures\import-sample.ndjson">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
```

- [ ] **Step 2: Write the failing idempotency test**

Create `Movies.Application.Tests/ImportTransformTests.cs`:

```csharp
using System.Data;
using Dapper;
using Movies.Application.Database;
using Xunit;

namespace Movies.Application.Tests;

[Collection("postgres")]
public class ImportTransformTests
{
    private readonly PostgresFixture _fx;

    public ImportTransformTests(PostgresFixture fx) => _fx = fx;

    private static string TransformSql() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "scripts", "import-transform.sql"));

    private static string[] FixtureLines() =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "import-sample.ndjson"));

    private static async Task RunImportAsync(IDbConnection connection, string[] ndjsonLines)
    {
        using var tx = connection.BeginTransaction();
        await connection.ExecuteAsync(
            "create temp table _import (doc jsonb) on commit drop;", transaction: tx);
        foreach (var line in ndjsonLines.Where(l => l.Trim().Length > 0))
        {
            await connection.ExecuteAsync(
                "insert into _import (doc) values (@doc::jsonb);",
                new { doc = line }, transaction: tx);
        }
        await connection.ExecuteAsync(TransformSql(), transaction: tx);
        tx.Commit();
    }

    [Fact]
    public async Task Transform_is_idempotent_on_tmdb_id()
    {
        var factory = new NpgsqlConnectionFactory(_fx.ConnectionString);
        await new DbInitializer(factory).InitializeAsync();
        using var connection = await factory.CreateConnectionAsync();
        await connection.ExecuteAsync("truncate movie_metadata, genres, movies cascade;");

        var lines = FixtureLines();
        await RunImportAsync(connection, lines);
        await RunImportAsync(connection, lines); // second run must add nothing

        Assert.Equal(2, await connection.ExecuteScalarAsync<int>("select count(*) from movies;"));
        Assert.Equal(4, await connection.ExecuteScalarAsync<int>("select count(*) from genres;"));
        Assert.Equal(2, await connection.ExecuteScalarAsync<int>("select count(*) from movie_metadata;"));
    }
}
```

- [ ] **Step 3: Run the test, verify it fails**

Run: `dotnet test Movies.Application.Tests --filter Transform_is_idempotent_on_tmdb_id`
Expected: FAIL — `import-transform.sql` does not exist (`FileNotFoundException`).

- [ ] **Step 4: Write the transformation SQL**

Create `scripts/import-transform.sql`:

```sql
-- Pure SQL (no psql meta-commands). Expects a populated temp table _import(doc jsonb).
-- Inserts new movies/genres/movie_metadata, deduped on tmdb_id. Ratings untouched.
create temp table _new on commit drop as
select
    gen_random_uuid()                                       as id,
    (i.doc->>'tmdb_id')::bigint                             as tmdb_id,
    i.doc->>'Title'                                         as title,
    (i.doc->>'YearOfRelease')::int                          as yearofrelease,
    -- Slug MUST match Movies.Application/Models/Movie.GenerateSlug.
    replace(lower(regexp_replace(i.doc->>'Title', '[^0-9A-Za-z _-]', '', 'g')), ' ', '-')
        || '-' || (i.doc->>'YearOfRelease')                 as slug,
    i.doc->'Genres'                                         as genres,
    i.doc->'raw'                                            as raw
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
```

- [ ] **Step 5: Run the test, verify it passes**

Run: `dotnet test Movies.Application.Tests --filter Transform_is_idempotent_on_tmdb_id`
Expected: PASS (2 movies, 4 genres, 2 metadata rows after two runs).

- [ ] **Step 6: Add the slug-parity test**

Append to `Movies.Application.Tests/ImportTransformTests.cs` (inside the class):

```csharp
    [Theory]
    [InlineData("Toy Story", 1995)]
    [InlineData("Monsters, Inc.", 2001)]
    [InlineData("Spider-Man: No Way Home", 2021)]
    [InlineData("WALL·E", 2008)]
    [InlineData("Amélie", 2001)]
    [InlineData("The   Big  Spaces", 1999)]
    public async Task Sql_slug_matches_csharp_slug(string title, int year)
    {
        var expected = new Movies.Application.Models.Movie
        {
            Id = Guid.NewGuid(), Title = title, YearOfRelease = year, Genres = []
        }.Slug;

        var factory = new NpgsqlConnectionFactory(_fx.ConnectionString);
        using var connection = await factory.CreateConnectionAsync();
        var actual = await connection.ExecuteScalarAsync<string>("""
            select replace(lower(regexp_replace(@title, '[^0-9A-Za-z _-]', '', 'g')), ' ', '-')
                   || '-' || @year::text
            """, new { title, year });

        Assert.Equal(expected, actual);
    }
```

- [ ] **Step 7: Run the slug-parity test, verify it passes**

Run: `dotnet test Movies.Application.Tests --filter Sql_slug_matches_csharp_slug`
Expected: PASS for all rows. If any row fails, the SQL expression in `import-transform.sql` and this test must be corrected together until they match `Movie.GenerateSlug`.

- [ ] **Step 8: Commit**

```bash
git add scripts/import-transform.sql Movies.Application.Tests
git commit -m "feat: add idempotent TMDB import transform SQL with slug-parity tests"
```

---

### Task 3: `psql` import runner

Thin wrapper that ingests the NDJSON file via `\copy` then applies the transform. The transformation logic is already covered by Task 2; this task adds only file ingestion, verified with an explicit manual integration check.

**Files:**
- Create: `scripts/import-movies.sql`

**Interfaces:**
- Consumes: `scripts/import-transform.sql` (Task 2); an NDJSON file at `/tmp/tmdb-movies.ndjson` inside the container.
- Produces: `scripts/import-movies.sql` — a `psql -f` entry point wrapping ingestion + transform in one transaction.

- [ ] **Step 1: Write the runner SQL**

Create `scripts/import-movies.sql`:

```sql
-- Run via: psql -v ON_ERROR_STOP=1 -f scripts/import-movies.sql
-- Expects the NDJSON already copied to /tmp/tmdb-movies.ndjson inside the db container.
begin;

create temp table _import (doc jsonb) on commit drop;
\copy _import(doc) from '/tmp/tmdb-movies.ndjson'

\i scripts/import-transform.sql

commit;
```

- [ ] **Step 2: Manual integration verification**

Run these exact commands against a throwaway container and confirm the printed counts:

```bash
# start a clean db (uses your .env POSTGRES_* values via docker-compose)
docker compose up -d db
# wait until healthy, then create the schema by starting the API once (Ctrl-C after "Database initialised.")
#   ./run.sh
# create a tiny NDJSON fixture
printf '%s\n' \
  '{"tmdb_id":862,"Title":"Toy Story","YearOfRelease":1995,"Genres":["Animation"],"raw":{"id":862}}' \
  > /tmp/tmdb-movies.ndjson
# copy scripts + data into the container and run the importer
CID=$(docker compose ps -q db)
docker cp /tmp/tmdb-movies.ndjson "$CID":/tmp/tmdb-movies.ndjson
docker cp scripts/import-transform.sql "$CID":/tmp/import-transform.sql
docker compose exec -T db psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 <<'SQL'
begin;
create temp table _import (doc jsonb) on commit drop;
\copy _import(doc) from '/tmp/tmdb-movies.ndjson'
\i /tmp/import-transform.sql
commit;
select count(*) as movies from movies;
SQL
```

Expected: `movies` count increases by 1; re-running prints the same count (idempotent). Note the runner references `scripts/import-transform.sql` relative to the invocation directory; the runbook (Task 6) documents copying both files into the container as shown above.

- [ ] **Step 3: Commit**

```bash
git add scripts/import-movies.sql
git commit -m "feat: add psql import runner for TMDB NDJSON"
```

---

### Task 4: TMDB → NDJSON `jq` filter (fixture-tested)

The fetch script's transformation logic lives in a standalone `jq` filter so it can be tested against a captured TMDB response with no live API key.

**Files:**
- Create: `scripts/tmdb-to-ndjson.jq`
- Create: `scripts/tests/fixtures/tmdb-popular-sample.json`
- Create: `scripts/tests/fixtures/genre-map.json`
- Create: `scripts/tests/fixtures/expected.ndjson`
- Create: `scripts/tests/test_tmdb_transform.sh`

**Interfaces:**
- Consumes: a TMDB list response (`.results[]`) on stdin; a genre id→name map passed as `--argjson genres`.
- Produces: `scripts/tmdb-to-ndjson.jq` emitting one compact NDJSON object per movie with fields `tmdb_id`, `Title`, `YearOfRelease`, `Genres`, `raw` — the exact shape Task 2's transform consumes.

- [ ] **Step 1: Create fixtures**

`scripts/tests/fixtures/genre-map.json`:

```json
{"16":"Animation","35":"Comedy","10751":"Family"}
```

`scripts/tests/fixtures/tmdb-popular-sample.json`:

```json
{"page":1,"results":[
  {"id":862,"title":"Toy Story","release_date":"1995-11-22","genre_ids":[16,35]},
  {"id":585,"title":"Monsters, Inc.","release_date":"2001-11-01","genre_ids":[16,10751]},
  {"id":999,"title":"No Date Movie","release_date":"","genre_ids":[35]}
]}
```

`scripts/tests/fixtures/expected.ndjson` (the movie with an empty `release_date` is dropped):

```
{"tmdb_id":862,"Title":"Toy Story","YearOfRelease":1995,"Genres":["Animation","Comedy"],"raw":{"id":862,"title":"Toy Story","release_date":"1995-11-22","genre_ids":[16,35]}}
{"tmdb_id":585,"Title":"Monsters, Inc.","YearOfRelease":2001,"Genres":["Animation","Family"],"raw":{"id":585,"title":"Monsters, Inc.","release_date":"2001-11-01","genre_ids":[16,10751]}}
```

- [ ] **Step 2: Write the failing shell test**

Create `scripts/tests/test_tmdb_transform.sh`:

```bash
#!/usr/bin/env bash
set -euo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GENRES="$(cat "$DIR/fixtures/genre-map.json")"

actual="$(jq -c --argjson genres "$GENRES" -f "$DIR/../tmdb-to-ndjson.jq" \
  "$DIR/fixtures/tmdb-popular-sample.json")"

if diff <(printf '%s\n' "$actual") "$DIR/fixtures/expected.ndjson"; then
  echo "PASS"
else
  echo "FAIL: output did not match expected.ndjson" >&2
  exit 1
fi
```

Make it executable: `chmod +x scripts/tests/test_tmdb_transform.sh`

- [ ] **Step 3: Run the test, verify it fails**

Run: `scripts/tests/test_tmdb_transform.sh`
Expected: FAIL — `jq: error: Could not open file scripts/tmdb-to-ndjson.jq`.

- [ ] **Step 4: Write the `jq` filter**

Create `scripts/tmdb-to-ndjson.jq`:

```jq
# Input: a TMDB list response. Arg $genres: {"<id>": "<name>"} map.
# Emits one object per movie with a usable release year; others are skipped.
.results[]
| select(.release_date != null and .release_date != "")
| {
    tmdb_id: .id,
    Title: .title,
    YearOfRelease: (.release_date[0:4] | tonumber),
    Genres: [ .genre_ids[] | $genres[tostring] // empty ],
    raw: .
  }
```

- [ ] **Step 5: Run the test, verify it passes**

Run: `scripts/tests/test_tmdb_transform.sh`
Expected: `PASS`.

- [ ] **Step 6: Commit**

```bash
git add scripts/tmdb-to-ndjson.jq scripts/tests
git commit -m "feat: add TMDB-to-NDJSON jq filter with fixture test"
```

---

### Task 5: `fetch-tmdb.sh` orchestration + API-key config

Wraps the `jq` filter with `curl` calls to TMDB and reads the API key from the environment. The transformation is already tested (Task 4); this task adds network orchestration, verified with a real-key smoke test.

**Files:**
- Create: `scripts/fetch-tmdb.sh`
- Modify: `.env.example` (add placeholder)
- Modify: `.env` (add key line for local use)

**Interfaces:**
- Consumes: `TMDB_API_KEY` env var; `scripts/tmdb-to-ndjson.jq` (Task 4).
- Produces: `scripts/fetch-tmdb.sh` writing NDJSON to `--out` (default `Resources/tmdb-movies.ndjson`) — the input Task 3's runner ingests.

- [ ] **Step 1: Add the key to `.env.example`**

Append to `.env.example`:

```bash

# TMDB (fetch-tmdb.sh) — get a v3 API key at https://www.themoviedb.org/settings/api
TMDB_API_KEY=your-tmdb-v3-api-key
```

- [ ] **Step 2: Add the key line to `.env`**

Append to `.env` (fill in the real v3 key locally; it is gitignored):

```bash

# TMDB
TMDB_API_KEY=
```

- [ ] **Step 3: Write the fetch script**

Create `scripts/fetch-tmdb.sh`:

```bash
#!/usr/bin/env bash
#
# Fetches a TMDB discovery list into NDJSON (one movie per line).
# Usage: scripts/fetch-tmdb.sh [--list popular|top_rated|now_playing] [--pages N] [--out PATH]
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

LIST="popular"
PAGES=5
OUT="$ROOT_DIR/Resources/tmdb-movies.ndjson"

while [ $# -gt 0 ]; do
  case "$1" in
    --list)  LIST="$2"; shift 2 ;;
    --pages) PAGES="$2"; shift 2 ;;
    --out)   OUT="$2"; shift 2 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

# Load .env if present so TMDB_API_KEY is available for local runs.
if [ -f "$ROOT_DIR/.env" ]; then
  set -a; . "$ROOT_DIR/.env"; set +a
fi

if [ -z "${TMDB_API_KEY:-}" ]; then
  echo "Error: TMDB_API_KEY is not set (add it to .env)." >&2
  exit 1
fi

API="https://api.themoviedb.org/3"
AUTH=(-H "Authorization: Bearer $TMDB_API_KEY" -H "accept: application/json")

# Build the genre id -> name map once.
GENRE_MAP="$(curl -fsSL "${AUTH[@]}" "$API/genre/movie/list?language=en-US" \
  | jq -c '[.genres[] | {(.id|tostring): .name}] | add')"

mkdir -p "$(dirname "$OUT")"
: > "$OUT"

for ((page=1; page<=PAGES; page++)); do
  curl -fsSL "${AUTH[@]}" "$API/movie/$LIST?language=en-US&page=$page" \
    | jq -c --argjson genres "$GENRE_MAP" -f "$SCRIPT_DIR/tmdb-to-ndjson.jq" \
    >> "$OUT"
done

echo "Wrote $(wc -l < "$OUT") movies to $OUT"
```

Make it executable: `chmod +x scripts/fetch-tmdb.sh`

Note: TMDB v3 keys also work as a Bearer token (v4 read-access token). If using a legacy v3 key as a query param instead, replace `AUTH` usage with `?api_key=$TMDB_API_KEY`; the Bearer form above is preferred.

- [ ] **Step 4: Smoke-test with a real key**

Set a real `TMDB_API_KEY` in `.env`, then run:

```bash
scripts/fetch-tmdb.sh --list popular --pages 1
head -n 2 Resources/tmdb-movies.ndjson | jq .
```

Expected: prints `Wrote 20 movies ...` (minus any dropped for empty release dates), and the two lines parse as valid JSON with `tmdb_id`, `Title`, `YearOfRelease`, `Genres`, `raw`.

- [ ] **Step 5: Commit**

```bash
git add scripts/fetch-tmdb.sh .env.example
git commit -m "feat: add fetch-tmdb.sh to pull TMDB lists into NDJSON"
```

(Do not commit `.env` — it is gitignored.)

---

### Task 6: Runbook documentation

Ties fetch + import into one documented workflow.

**Files:**
- Create: `_docs/tmdb-import.md`

**Interfaces:**
- Consumes: all prior scripts.
- Produces: a runbook; no code depends on it.

- [ ] **Step 1: Write the runbook**

Create `_docs/tmdb-import.md`:

```markdown
# TMDB Import Runbook

Populate `movies`, `genres`, and `movie_metadata` from TMDB. The `ratings`
table is never touched. Imports are idempotent (deduped on `tmdb_id`).

## 1. Configure

Add your TMDB v3 API key to `.env`:

    TMDB_API_KEY=<your key>

## 2. Fetch (on demand)

    scripts/fetch-tmdb.sh --list popular --pages 5
    # writes Resources/tmdb-movies.ndjson

Lists: `popular`, `top_rated`, `now_playing`. 20 movies per page.

## 3. Import into the running db container

    CID=$(docker compose ps -q db)
    docker cp Resources/tmdb-movies.ndjson "$CID":/tmp/tmdb-movies.ndjson
    docker cp scripts/import-transform.sql "$CID":/tmp/import-transform.sql
    docker compose exec -T db \
      psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 <<'SQL'
    begin;
    create temp table _import (doc jsonb) on commit drop;
    \copy _import(doc) from '/tmp/tmdb-movies.ndjson'
    \i /tmp/import-transform.sql
    commit;
    SQL

Re-running fetch + import refreshes with newly-seen movies only; already-imported
movies (same `tmdb_id`) are skipped.
```

- [ ] **Step 2: Commit**

```bash
git add _docs/tmdb-import.md
git commit -m "docs: add TMDB import runbook"
```

---

## Self-Review

**Spec coverage:**
- `movie_metadata` table (spec §1) → Task 1. ✅ (dropped the redundant standalone index — column `unique` already indexes `tmdb_id`; noted in Task 1.)
- Fetch script + args + genre map + year parsing + NDJSON shape (spec §2) → Tasks 4 (transform) + 5 (orchestration). ✅
- Load: `\copy` + idempotent transform, atomic, ratings untouched (spec §3) → Tasks 2 (transform + idempotency test) + 3 (runner). ✅
- Config `TMDB_API_KEY` in `.env`/`.env.example` (spec §4) → Task 5. ✅
- Slug parity + parity test (spec "Slug parity") → Task 2 Steps 6–7. ✅
- Testing: parity, idempotency, fetch shaping (spec "Testing") → Tasks 2 and 4. ✅
- Runbook / docs (spec "Files touched") → Task 6. ✅

**Placeholder scan:** No TBD/TODO; every code/SQL/bash step has concrete content.

**Type consistency:** `NpgsqlConnectionFactory(string)`, `DbInitializer(IDbConnectionFactory).InitializeAsync()`, `Movie { Id, Title, YearOfRelease, Genres }.Slug` all match the real source read during planning. NDJSON field names (`tmdb_id`, `Title`, `YearOfRelease`, `Genres`, `raw`) are identical across the fixture (Task 2), the `jq` filter output (Task 4), and the transform SQL (Task 2). The transform slug expression and the parity-test SQL are the same expression.
```
