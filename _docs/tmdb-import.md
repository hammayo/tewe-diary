# TMDB Import Runbook

Populate `movies`, `genres`, and `movie_metadata` from TMDB. The `ratings`
table is never touched. Imports are idempotent (deduped on `tmdb_id`), so
re-running only adds movies not already imported.

## 1. Configure

Add your TMDB credentials to `.env` (gitignored):

    TMDB_API_KEY=<your key>

Get credentials at https://www.themoviedb.org/settings/api. Either a **v3 API key**
(32-char hex) or a **v4 read-access token** (a JWT) works — `fetch-tmdb.sh` detects
the shape and uses the correct auth scheme automatically.

## 2. Fetch (on demand)

`fetch-tmdb.sh` has three mutually-exclusive modes. All modes write NDJSON to
`Resources/tmdb-movies.ndjson` (override with `--out PATH`) and paginate with
`--pages N` (20 movies per page; trending is fixed at 10).

### List mode (default)

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

## 3. Import into the running db container

The db runs in Docker and no host `psql` client is required. Copy the NDJSON,
the transform, and the runner into the container, then run the runner with psql
inside the container:

    # load POSTGRES_* from .env into the shell
    set -a; . ./.env; set +a

    CID=$(docker compose ps -q db)
    docker cp Resources/tmdb-movies.ndjson "$CID":/tmp/tmdb-movies.ndjson
    docker cp scripts/import-transform.sql "$CID":/tmp/import-transform.sql
    docker cp scripts/import-movies.sql    "$CID":/tmp/import-movies.sql

    docker compose exec -T db \
      psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 \
      -f /tmp/import-movies.sql

The schema (including `movie_metadata`) is created automatically the first time
`Movies.Api` starts (`DbInitializer`); ensure the API has run at least once, or
the tables already exist, before importing.

Re-running fetch + import refreshes with newly-seen movies only; movies already
present (same `tmdb_id`) are skipped. The `ratings` table is never modified.

## Behaviour notes

Within a single import batch, if the same TMDB id appears more than once (which
happens when TMDB paged lists return the same movie on two pages), only the first
occurrence is kept; duplicates are silently dropped before insertion.

A movie whose title + year produces a slug that already exists in `movies`, or
that collides with another movie in the same batch, is **skipped** rather than
imported. Slugs are never disambiguated with a suffix because `movies.slug` must
remain byte-identical to the slug generated by the API's `Movie.GenerateSlug`
method; adding a suffix would break by-slug lookups in the API.
