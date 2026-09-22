# TMDB Import Runbook

Populate `movies`, `genres`, `movie_metadata` (incl. `imdb_id`), `movie_details`
(overview, tagline, runtime, poster/backdrop, trailer) and `movie_credits`
(director(s), core writers, top-10 cast) from TMDB. The `ratings` table is never
touched. Imports are idempotent (deduped on `tmdb_id`).

## Pipeline overview

The dataset is generated, not committed (`Data/` is gitignored). The **same SQL
transform** is the single source of truth — embedded in `Movies.Application` for the
CLI path and shared with the psql ops path (see [design-decisions.md](design-decisions.md#4-the-import-transform-is-embedded-shared-with-the-ops-path)).

```
TMDB API ──fetch-tmdb.sh (list/discover/trending, then enrich: one
          /movie/{id}?append_to_response=credits,videos per movie)──▶ Data/tmdb-<fetch>.ndjson
                                              │
                    ┌─────────────────────────┴─────────────────────────┐
                    ▼                                                   ▼
         load-movies.sh (psql COPY into                       Movies.DbTool import
         staging → helpers/import-transform.sql)              (embedded transform)
                    └─────────────────────────┬─────────────────────────┘
                                              ▼
                                     movies + genres + movie_metadata +
                                     movie_details + movie_credits
                                     (upsert by id, slug-collision-safe)
```

### Enrichment (details, credits, trailer)

After the chosen mode writes its list, `fetch-tmdb.sh` always enriches every movie:

- **One call per movie:** `helpers/fetch-tmdb-detail.sh` runs 8 in parallel through `xargs -P 8`,
  with `curl --retry 5` on 429/5xx. A full run is about 10k calls and takes a few minutes.
- **Trimmed before storage:** `helpers/tmdb-details-to-ndjson.jq` keeps the top 10 cast, the
  director(s), and writers with job Screenplay, Writer, Story, Novel or Author. That's about
  3.5 KB per movie against about 86 KB untrimmed.
- **One trailer is chosen:**
  1. YouTube or Vimeo only
  2. `Trailer`, falling back to `Teaser`
  3. official first
  4. a video named exactly "Official Trailer" first
  5. newest first
- **Paging stops early:** TMDB keeps serving empty pages past the last result, so the fetch stops as
  soon as a page comes back empty (`No more results after page N; stopping early.`) instead of running
  to `--pages`. The classics fetch runs out at ~page 200 of 500.
- **Failures don't stop the run:** a movie whose details call fails (a 404, or retries used up)
  is skipped. The script ends with `Skipped N movie(s) … <ids>` and still exits 0.

Test the jq step with `bash scripts/tests/test_tmdb_details_transform.sh`.

```bash
bash scripts/fetch-tmdb.sh                 # 1. fetch movies + details into Data/tmdb-<what-was-fetched>.ndjson (needs a TMDB API key)
bash scripts/load-movies.sh [files...]     # 2. load every Data/*.ndjson (or just the files you name) into the running db,
                                           #    showing step progress, the import summary and details coverage
bash scripts/enrich-movies.sh [--all]      # shortcut for movies already in the db: fetch details for those missing them,
                                           #    then load (--all: refresh every one)
bash scripts/reset-data.sh [--volume] [--reload]   # start fresh: delete every movie and rating (asks first)
# or, via the CLI tool:
dotnet run --project Ops.Tools/Movies.DbTool -- import Data/*.ndjson
# schema migrations (same CLI):
dotnet run --project Ops.Tools/Movies.DbTool -- migrate
```

## 1. Configure

Add your TMDB credentials to `.env` (gitignored):

    TMDB_API_KEY=<your key>

Get credentials at https://www.themoviedb.org/settings/api. Either a **v3 API key**
(32-char hex) or a **v4 read-access token** (a JWT) works — `fetch-tmdb.sh` detects
the shape and uses the correct auth scheme automatically.

## 2. Fetch (on demand)

### Presets (the easy way)

A preset is one word, needs no TMDB vocabulary, and names its own output file:

```
    scripts/fetch-tmdb.sh                     newest releases (the default)      -> tmdb-newest.ndjson
    scripts/fetch-tmdb.sh newest [pages]      newest releases, N pages of 20     -> tmdb-newest.ndjson
    scripts/fetch-tmdb.sh classics [minVotes] highest rated, 1000+ votes         -> tmdb-classics.ndjson
    scripts/fetch-tmdb.sh popular             TMDB's popular list                -> tmdb-popular.ndjson
    scripts/fetch-tmdb.sh top-rated           TMDB's top-rated list              -> tmdb-top-rated.ndjson
    scripts/fetch-tmdb.sh now-playing         in cinemas now                     -> tmdb-now-playing.ndjson
    scripts/fetch-tmdb.sh trending [day|week] trending now (top 10)              -> tmdb-trending-week.ndjson
    scripts/fetch-tmdb.sh year 1994 [genre]   one year, optionally genres        -> tmdb-year-1994.ndjson
    scripts/fetch-tmdb.sh genre "Horror,Sci"  one or more genres                 -> tmdb-genre-horror-sci.ndjson
    scripts/fetch-tmdb.sh language hi         one original language              -> tmdb-language-hi.ndjson
    scripts/fetch-tmdb.sh ids 680 550         exact TMDB ids (or: ids ids.txt)   -> tmdb-ids-680-550.ndjson
```

`--help` lists them, `--dry-run` prints what a command would fetch and where without calling TMDB, and
any flag below still works after a preset (`classics --pages 50 --min-rating 7`).

### Flags (everything a preset can't say)

`fetch-tmdb.sh` has four mutually exclusive modes. They all write NDJSON to
`Data/tmdb-<what-was-fetched>.ndjson` (override with `--out PATH`). A bare run is discover mode, newest first,
up to 500 pages. List and discover paginate with `--pages N` (20 movies per page); trending is fixed
at 10. Every mode then enriches each movie with its details (see
[Enrichment](#enrichment-details-credits-trailer)).

### List mode

    scripts/fetch-tmdb.sh --list popular --pages 5

Lists: `popular`, `top_rated`, `now_playing`.

`--list` uses TMDB's curated endpoints, which **cannot be filtered**. To filter by
year/genre/language, use discover mode instead: `--sort popularity.desc` ≈ popular,
`--sort vote_average.desc` ≈ top_rated (e.g. `--original-language en --sort popularity.desc`).

### Discover mode (filtered)

Triggered by any of `--year`, `--genre`, `--original-language`, `--sort`,
`--min-rating`, `--min-votes` (uses TMDB `/discover/movie`):

    # Latest, well-rated movies of a year
    scripts/fetch-tmdb.sh --year 2026 --pages 10

    # Filter by genre name(s) — comma-separated, matched to TMDB genre ids (OR)
    scripts/fetch-tmdb.sh --year 2025 --genre "Mystery,Thriller,Action,Science Fiction,Comedy" --pages 5

    # Hindi-language movies of a year (ISO 639-1 code: hi)
    scripts/fetch-tmdb.sh --year 2026 --original-language hi --pages 5

    # Hindi action films, highest-rated first, stricter quality floor
    scripts/fetch-tmdb.sh --year 2024 --original-language hi --genre "Action" \
      --sort vote_average.desc --min-rating 7 --min-votes 100 --pages 5

Options and defaults:

| Flag                     | TMDB parameter                                                                       | Default                     |
|--------------------------|--------------------------------------------------------------------------------------|-----------------------------|
| `--year YYYY`            | `primary_release_year`                                                               | (none)                      |
| `--genre "A,B"`          | `with_genres` (names → ids, OR)                                                      | (none)                      |
| `--original-language xx` | `with_original_language` (ISO 639-1, e.g. `hi`, `en`, `ja`)                          | (none)                      |
| `--sort field`           | `sort_by` (e.g. `primary_release_date.desc`, `vote_average.desc`, `popularity.desc`) | `primary_release_date.desc` |
| `--min-rating X`         | `vote_average.gte`                                                                   | `6.5`                       |
| `--min-votes N`          | `vote_count.gte`                                                                     | `50`                        |

Note the tension between "latest" and "highly rated": very new releases have few
votes, so a high `--min-votes` will drop them. Lower `--min-votes 0` to prioritise
recency, or raise `--min-rating`/`--min-votes` to prioritise quality. An unknown
`--genre` name aborts with the list of valid genres.

### Trending mode (top 10)

    scripts/fetch-tmdb.sh --trending week    # or: --trending day (defaults to week)

Returns the current top 10 trending movies. Discover/list filters cannot be
combined with `--trending`.

### Ids mode (details only)

    scripts/fetch-tmdb.sh --ids-file ids.txt --out /tmp/enriched.ndjson

Skips the list call and fetches details for exactly the TMDB ids listed one per line in `ids.txt`.
It can't be combined with the other modes. `scripts/enrich-movies.sh` uses it (see §4).

### One file per fetch

Each run writes `Data/tmdb-<what-was-fetched>.ndjson`, so a new pull never overwrites an earlier one:

| Command                                               | File                                                                                  |
|-------------------------------------------------------|---------------------------------------------------------------------------------------|
| `fetch-tmdb.sh` or `fetch-tmdb.sh newest`             | `tmdb-newest.ndjson`                                                                  |
| `fetch-tmdb.sh classics`                              | `tmdb-classics.ndjson`                                                                |
| `fetch-tmdb.sh classics 5000`                         | `tmdb-classics-v5000.ndjson`                                                          |
| `fetch-tmdb.sh year 1994 Action`                      | `tmdb-year-1994-action.ndjson`                                                        |
| `fetch-tmdb.sh popular` / `top-rated` / `now-playing` | `tmdb-popular.ndjson` …                                                               |
| `fetch-tmdb.sh trending week`                         | `tmdb-trending-week.ndjson`                                                           |
| `fetch-tmdb.sh ids 680 550`                           | `tmdb-ids-680-550.ndjson`                                                             |
| raw flags, no preset                                  | named after the mode and filters, e.g. `tmdb-discover-vote_average.desc-v1000.ndjson` |

`--out PATH` overrides it. Re-running the same fetch refreshes its own file.

**Why it matters:** TMDB caps discover at 500 pages (10,000 movies), so one fetch can never hold the
whole catalogue. A bare run gives the newest releases only — it reached back to 2004 in practice, so
older films (Pulp Fiction, 1994) simply aren't in it. Build the collection you want from several
fetches and load them together:

    scripts/fetch-tmdb.sh             # newest releases
    scripts/fetch-tmdb.sh classics    # highest rated (1000+ votes)
    scripts/fetch-tmdb.sh year 1994   # one year
    scripts/fetch-tmdb.sh ids 680 550 # exact films
    scripts/load-movies.sh            # load them all at once

## 3. Import into the running db container

The simplest way is the loader script, which loads **every** `Data/*.ndjson` unless you name files.
No host `psql` client is needed:

    scripts/load-movies.sh                      # every Data/*.ndjson
    scripts/load-movies.sh Data/tmdb-list-popular.ndjson /path/to/other.ndjson

**No duplicates.** All the files are staged together in one transaction: a movie appearing in several
fetches is imported once (the transform keeps one row per `tmdb_id`), and movies already in the db are
refreshed in place, keeping their ids and ratings. The loader reports unique movies as well as lines.

It shows its progress, and on failure prints psql's error and exits 1 (nothing is committed):

      tmdb-discover-primary_release_date.desc.ndjson              9800 movies (9800 with details)
      tmdb-discover-vote_average.desc-v5000.ndjson                  40 movies (40 with details)
    [1/3] Copying 2 file(s), 9840 lines (9830 unique movies) + loaders into the db container...
    [2/3] Importing in one transaction (stage, upsert movies/genres/metadata, details, credits)...
      running · 9s elapsed
      Import: 0 inserted, 9781 refreshed, 0 skipped (slug collision)
    [3/3] TMDB movies with details: 10009 / 10009
    Done in 21s.

The import is one transaction, so there's no per-row count; it shows the elapsed time instead. The
.NET path (`dotnet run --project Ops.Tools/Movies.DbTool -- import <file>`) stages lines one at a time,
so it does show a count: `n/total (%) · elapsed · ETA`, updated every 250 lines.

What the script does, if you ever need to do it by hand: copy the NDJSON, the transform and the
runner into the container, then run the runner with psql inside it.

    # load POSTGRES_* from .env into the shell
    set -a; . ./.env; set +a

    CID=$(docker compose ps -q db)
    docker cp Data/<fetch>.ndjson "$CID":/tmp/tmdb-movies.ndjson
    docker cp scripts/helpers/import-transform.sql "$CID":/tmp/import-transform.sql
    docker cp scripts/helpers/import-movies.sql    "$CID":/tmp/import-movies.sql

    docker compose exec -T db \
      psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 \
      -f /tmp/import-movies.sql

The schema is owned by the FluentMigrator migrations. `Movies.Api` applies them on boot locally;
otherwise run `dotnet run --project Ops.Tools/Movies.DbTool -- migrate` before importing.

Re-running fetch + import inserts newly seen movies and **refreshes** existing ones in place, matched
by `tmdb_id`, so ids and ratings are kept. The `ratings` table is never modified.

## 4. Keep every movie's details complete

A fetch only enriches the movies it lists, and discover's result set changes daily. So movies
imported earlier, for example by the list-only fetch that ran before details existed, can be left
without details. `enrich-movies.sh` fixes that by working from the TMDB ids already in the db:

    scripts/enrich-movies.sh          # only TMDB movies still missing details
    scripts/enrich-movies.sh --all    # refresh details for every TMDB movie

It reads the ids from the db, fetches them (`fetch-tmdb.sh --ids-file`, with progress), then loads the
result (`load-movies.sh`). If nothing is missing it prints "All TMDB movies already have details" and
exits. A full local run (9,781 movies) took about 3 minutes.

TMDB itself doesn't have every field for every film. After a full enrichment locally: posters
10,006 / 10,009, runtime 9,986, director 9,982, trailers 8,472, taglines 5,812. An empty tagline or
trailer usually means TMDB has none.

## 5. Progress and status output

| Command                      | Shows                                                                                                  |
|------------------------------|--------------------------------------------------------------------------------------------------------|
| `fetch-tmdb.sh` (enrichment) | `done/total (%) · failed · elapsed · ETA`, then `Enriched n/m` and any skipped ids                     |
| `load-movies.sh`             | the steps above, the import summary, and `TMDB movies with details: x / y`                             |
| `Movies.DbTool import`       | the staged count with ETA, the transform step and summary, and the same coverage line                  |
| `stack-up.sh` | Movies; Details with %, posters, trailers and taglines; Credits by type; last import time; the scripts in order (fetch, then load, or the `enrich-movies.sh` shortcut); a next step only when one is needed; it also lists `reset-data.sh` |

In a terminal, the progress line redraws in place every second. In CI logs such as `import.yml`, it
prints a new line every 30s (scripts) or every 10% (DbTool).

`stack-up.sh` example:

    Movies data:
      Movies    10034   10009 from TMDB · 25 manual
      Details   10009   of 10009 TMDB movies (100%) · posters 10006 · trailers 8472 · taglines 5812
      Credits   122694  11146 directors · 19241 writers · 92307 cast
      Imported  2026-09-22 13:29 UTC

      Scripts (in order, all safe to re-run):
        1. scripts/fetch-tmdb.sh [options]     fetch movies + details -> Data/tmdb-<what-was-fetched>.ndjson
        2. scripts/load-movies.sh [files...]   load every Data/*.ndjson (or just the files you name)
        Or, for movies already in the db (fetches, then loads):
           scripts/enrich-movies.sh [--all]    fill in missing details (--all: refresh every TMDB movie)
        Start over (deletes every movie and rating, asks first):
           scripts/reset-data.sh [--volume] [--reload]   wipe the data (--volume: the db volume too;
                                                         --reload: then fetch + load fresh TMDB data)

## Behaviour notes

**Enriched data is never downgraded.** A line fetched from the details endpoint (its `raw` has
`credits`) always refreshes `movie_details`, `movie_credits`, `imdb_id` and `raw`. A list-only line,
for example from an older NDJSON file, only seeds `movie_details` (overview and poster) for a movie
that has none yet. It never blanks an existing tagline, trailer, credits, `imdb_id` or an enriched `raw`.
Bad field values (a non-numeric runtime, an unsupported trailer site, a credit with no id) are stored
as `null` or skipped, never failing the import transaction.

**GitHub Actions:** `import.yml` is triggered manually. Its default `fetch_args` is empty, which
runs the full discover fetch (about 10k movies, enriched). Pass e.g. `--list popular --pages 5`
for a quick run.

**API image URLs** are built from `Tmdb:Images` in `appsettings.json`:
`BaseUrl` `https://image.tmdb.org/t/p/`, `PosterSize` `w500`, `BackdropSize` `w1280` and
`ProfileSize` `w185`. The same values are the code defaults.

Within a single import batch, if the same TMDB id appears more than once (which
happens when TMDB paged lists return the same movie on two pages), only the first
occurrence is kept; duplicates are silently dropped before insertion.

A movie whose title + year produces a slug that already exists in `movies`, or
that collides with another movie in the same batch, is **skipped** rather than
imported. Slugs are never disambiguated with a suffix because `movies.slug` must
remain byte-identical to the slug generated by the API's `Movie.GenerateSlug`
method; adding a suffix would break by-slug lookups in the API.

## 6. Start fresh (wipe everything)

`scripts/reset-data.sh` clears the local data for a clean start: every movie (TMDB **and** manually
created), its genres, details, credits and metadata, and **all ratings**. It prints what will go,
asks you to type `wipe`, and has no undo. The schema stays with the migrations.

    scripts/reset-data.sh                    # empty the data tables (container and schema kept)
    scripts/reset-data.sh --volume           # also delete the Postgres volume, then restart the stack
    scripts/reset-data.sh --reload           # after wiping: fetch fresh TMDB data and load it
    scripts/reset-data.sh --volume --reload  # the full clean start
    scripts/reset-data.sh --yes ...          # skip the prompt (scripting)

`--volume` is the only option that also clears anything left by migrations or added by hand outside
the data tables. Without it, one `truncate movies cascade` empties every dependent table, which is
what the test fixture does between tests.

**One stack script at a time.** `stack-up.sh` and `reset-data.sh` take a lock, so starting the stack
(Rider's Docker Stack config, or `stack-up.sh`) while a reset is recreating it stops with a message
instead of Docker's "container name is already in use". A lock left by a dead process is cleared
automatically.

Check afterwards with `scripts/stack-up.sh`: **manual** should be 0, and `Imported` should show today.
Your `.env` and its TMDB key are never touched.
