# PRD — TMDB Movie Details

|              |                                                                                                                                             |
|--------------|---------------------------------------------------------------------------------------------------------------------------------------------|
| **Status**   | Implemented — 2026-09-22 (all FR/NFR Done; see §10)                                                                                         |
| **Owner**    | Hammy Babar                                                                                                                                 |
| **Branch**   | `feature/phase-2-changes`                                                                                                                   |
| **Doc set**  | **PRD (this)** → SDD `_tasks/01-tmdb-movie-details-spec.md` → Implementation plan `_tasks/01-tmdb-movie-details-plan.md` |
| **Research** | Findings `_tasks/01-tmdb-movie-details-findings.md`                                                                        |
| **Note**     | `_tasks/` is gitignored, so those three docs exist only in my local checkout and are not linked.                           |

Status legend (used in all three docs): `Todo` · `In progress` · `Done` · `Blocked` · `Dropped`.

## 1. Problem

Tewe's API only returns a movie's title, year, genres and ratings. The TMDB page for the same film
also shows a synopsis, a tagline ("Defy the gods."), a poster, a backdrop, a trailer, the director,
the writers and the lead cast. Most of that data already reaches our database inside
`movie_metadata.raw`, but no part of the API exposes it. The tagline, credits, trailer and IMDb id
aren't fetched at all.

## 2. Goals

1. A client can render a full movie page (synopsis, tagline, runtime, poster, backdrop, trailer,
   director, writers, top cast) from **one** call: `GET /api/v1/movies/{idOrSlug}`.
2. A client can render a poster grid from the existing list call: `GET /api/v1/movies`.
3. Every TMDB-sourced movie stores its **TMDB id** and **IMDb id** for reference and future linking.
4. Nothing breaks for existing clients: changes to responses only add fields, and the `movies`
   table schema stays as it is.

## 3. Non-goals (this release)

- Details for manually created (API-created) movies. Details are TMDB-sourced and read-only.
- Filtering or searching by person (director or cast), and a `people` table or person endpoints.
- Beyond the `self` link: page navigation and action links (see D20).
- Videos other than one trailer (teasers, clips, featurettes).
- Localised text (fixed to `en-US`, matching the current fetch).
- Scheduled or automatic refresh. The import stays manually triggered.
- Editing details through the API.

## 4. Users and use cases

| User                     | Use case                                                                                          |
|--------------------------|---------------------------------------------------------------------------------------------------|
| Front-end / API consumer | Show a movie detail page like TMDB's; show poster thumbnails in lists                             |
| Operator (Hammy)         | Run the TMDB import and get enriched data without manual steps; see which movies failed to enrich |
| Future features          | Link out to IMDb; add person pages later using stored TMDB person ids                             |

## 5. Decisions (from review interviews, 2026-09-21 / 22)

| ID  | Decision                                                                                                                                                                                                                   |
|-----|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| D1  | Manual movies do **not** carry details                                                                                                                                                                                     |
| D2  | Enrich **all** fetched movies (about 10k detail calls per full run)                                                                                                                                                        |
| D3  | Expose `imdbId` and `runtime`                                                                                                                                                                                              |
| D4  | Images and trailer returned as **full URLs**                                                                                                                                                                               |
| D5  | Include director(s), writers and top cast                                                                                                                                                                                  |
| D6  | `movies` table unchanged; new data lives in **new tables**                                                                                                                                                                 |
| D7  | Store `tmdb_id` (existing) and `imdb_id` (new)                                                                                                                                                                             |
| D8  | Include **one trailer** per movie                                                                                                                                                                                          |
| D9  | Details are **embedded** in `GET /movies/{idOrSlug}` as a nullable `details` object (no separate endpoint)                                                                                                                 |
| D10 | `posterUrl` added to `MovieResponse` and returned by the **list and single** GET                                                                                                                                           |
| D11 | Import stays **manual** (`workflow_dispatch`); the default fetch becomes the full discover run                                                                                                                             |
| D12 | Top **10** cast                                                                                                                                                                                                            |
| D13 | Writers = core jobs only: Screenplay, Writer, Story, Novel, Author                                                                                                                                                         |
| D14 | No person search this release                                                                                                                                                                                              |
| D15 | A failed details call for a movie: **skip that movie, carry on, report failed ids**                                                                                                                                        |
| D16 | *(post-implementation, 2026-09-22)* Long-running ops steps show **progress** (fetch, psql load, DbTool import)                                                                                                             |
| D17 | *(post-implementation)* **Every** TMDB movie already in the db must get details: enrich **by id from the db**, not by re-running discover                                                                                  |
| D19 | *(post-implementation)* A **clean start** needs one command: wipe all data (including manual movies and ratings) with a typed confirmation, optionally the db volume too, optionally followed by a fresh fetch + load |
| D20 | *(post-implementation)* **HATEOAS**: list items carry a HAL `links` array — for now the `self` link only, absolute, generated from the route. The single-movie response carries none. Page (`next`/`prev`) and action links (`update`/`delete`/`rate`) are deliberately deferred |
| D21 | *(post-implementation)* One TMDB fetch can't hold the catalogue (TMDB caps discover at 500 pages ≈ 10k). So **each fetch writes its own file** (`Data/tmdb-<what-was-fetched>.ndjson`) and the loaders take **many files at once**, deduping on `tmdb_id` |
| D22 | *(post-implementation)* The fetch is called by **preset** (`classics`, `year 1994`, `ids 680`), not by TMDB flags, and its output file is named after the preset. Flags stay for anything a preset can't express |
| D18 | *(post-implementation)* `stack-up.sh` output is short but informative: counts with a details and credits breakdown, the last import time, the scripts in fetch-then-load order, and a next step only when action is needed |

## 6. Functional requirements

| ID    | Requirement                                          | Acceptance criteria                                                                                                                                                                                                                                                                                                                                                                        | Status |
|-------|------------------------------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|--------|
| FR-1  | Persist external ids                                 | Every TMDB movie row has `tmdb_id` (unique, existing) and `imdb_id` (nullable) after import                                                                                                                                                                                                                                                                                                | Done   |
| FR-2  | Overview, tagline, runtime                           | `details.overview`, `details.tagline`, `details.runtimeMinutes`. Empty TMDB strings and runtime 0 return `null`                                                                                                                                                                                                                                                                            | Done   |
| FR-3  | Backdrop image                                       | `details.backdropUrl` is a full HTTPS URL at the configured size, or `null` if TMDB has none                                                                                                                                                                                                                                                                                               | Done   |
| FR-4  | Poster image in list and single GET                  | `posterUrl` on every `MovieResponse` from `GET /movies` and `GET /movies/{idOrSlug}`. `null` for manual movies and movies without a poster                                                                                                                                                                                                                                                 | Done   |
| FR-5  | Director(s)                                          | `details.directors[]` = crew with job `Director`, in TMDB order                                                                                                                                                                                                                                                                                                                            | Done   |
| FR-6  | Writers                                              | `details.writers[]` = crew with job in {Screenplay, Writer, Story, Novel, Author}, with `role` = the job                                                                                                                                                                                                                                                                                   | Done   |
| FR-7  | Top cast                                             | `details.cast[]` = the first 10 by TMDB billing order, each with `role` = character and `profileUrl`                                                                                                                                                                                                                                                                                       | Done   |
| FR-8  | Trailer                                              | `details.trailer` = `{site, key, name, url, embedUrl}` chosen by the selection rule (SDD §2.3), or `null` if none                                                                                                                                                                                                                                                                          | Done   |
| FR-9  | Details embedded in single GET                       | `GET /movies/{idOrSlug}` returns `details` for TMDB movies and `details: null` for manual movies. The list returns `details: null` (use the single GET for details)                                                                                                                                                                                                                        | Done   |
| FR-10 | Enriched fetch                                       | `scripts/fetch-tmdb.sh` enriches every fetched movie with one details call (`credits,videos` appended). Failures are skipped and reported by id, and the script exits 0                                                                                                                                                                                                                    | Done   |
| FR-11 | Safe import                                          | Re-import is idempotent. Ratings and manual movies are never touched. A list-only NDJSON never wipes existing details or credits                                                                                                                                                                                                                                                           | Done   |
| FR-12 | Ops default                                          | `import.yml` default `fetch_args` runs the full discover fetch (bare `fetch-tmdb.sh`); still manual trigger                                                                                                                                                                                                                                                                                | Done   |
| FR-13 | Attribution                                          | The TMDB attribution notice appears in `README.md` and the Swagger description                                                                                                                                                                                                                                                                                                             | Done   |
| FR-14 | Progress output *(added post-implementation)*        | `fetch-tmdb.sh` enrichment shows done/total, %, failures, elapsed and ETA. `load-movies.sh` shows its steps with elapsed time and the import summary. The DbTool import shows a staged count every 250 lines, the transform step and its summary. All three print every second on a terminal and every 30s or 10% in CI logs                                                               | Done   |
| FR-15 | Enrich existing movies *(added post-implementation)* | `scripts/enrich-movies.sh [--all]` reads the TMDB ids missing details from the db and fetches them (`fetch-tmdb.sh --ids-file`), then loads them. Re-running it when nothing is missing is a no-op                                                                                                                                                                                         | Done   |
| FR-16 | Data summary *(added post-implementation)*           | `stack-up.sh` prints:<br>• Movies (TMDB / manual)<br>• Details: n of the TMDB total, with %, posters, trailers and taglines<br>• Credits by type (directors / writers / cast)<br>• the last import time (UTC)<br>• the scripts in usage order: 1 `fetch-tmdb.sh`, 2 `load-movies.sh`, or the `enrich-movies.sh` shortcut<br>• a next step only when the db is empty or details are missing <br>• `reset-data.sh` listed as the destructive "start over" option | Done |
| FR-17 | Clean start *(added post-implementation)* | `scripts/reset-data.sh [--volume] [--reload] [--yes]` deletes every movie (TMDB and manual), genres, details, credits, metadata and ratings after a typed `wipe` confirmation; `--volume` also drops the Postgres volume and restarts the stack; `--reload` then fetches the newest releases and loads **every** `Data/*.ndjson`, so earlier fetches (classics, per-year, by-id) come back too; the NDJSON files are never deleted ; `reset-data.sh` and `stack-up.sh` take a lock so two concurrent stack starts fail fast with a message instead of Docker's "container name is already in use" | Done |
| FR-18 | HAL self links *(added post-implementation)* | Each **list item** carries `links: [{ href, rel: "self", type: "GET" }]`, with an absolute href generated from the route; following it returns the same movie. `GET /movies/{idOrSlug}` carries **no** links (the caller already has that URL), and the property is omitted rather than sent as null | Done |
| FR-19 | Multiple fetches, one library *(added post-implementation)* | `fetch-tmdb.sh` names its output after the fetch (list/trending/ids/discover with year, language, genre, rating and vote floors), so pulls don't overwrite each other; `--out` still overrides. `load-movies.sh` with no arguments loads every `Data/*.ndjson`, or just the files named; `Movies.DbTool import` takes several paths and `import.yml` passes `Data/*.ndjson`. Files are staged together in one transaction, so a movie in several files is imported once and existing movies are refreshed in place | Done |
| FR-20 | Friendly fetch presets *(added post-implementation)* | `fetch-tmdb.sh [newest\|classics\|popular\|top-rated\|now-playing\|trending\|year\|genre\|language\|ids] [value]` needs no TMDB vocabulary; `--help` lists them; `--dry-run` shows what would be fetched and where without calling TMDB; the output file is named after the preset (`tmdb-classics.ndjson`); flags still work and can follow a preset; bad input exits 2 with usage | Done |
| FR-21 | Stop paging when results run out *(added post-implementation)* | The list and discover loops stop as soon as TMDB returns an empty page, reporting `No more results after page N`, instead of running to `--pages` | Done |

## 7. Non-functional requirements

| ID    | Requirement                                                                                                                                                  | Status |
|-------|--------------------------------------------------------------------------------------------------------------------------------------------------------------|--------|
| NFR-1 | **Backward compatible:** only new, nullable fields on existing contracts. No renamed or removed fields, and the same routes                                  | Done   |
| NFR-2 | **Additive migration** (expand-only): safe to run before the deploy, while the previous image is still serving                                               | Done   |
| NFR-3 | **Storage:** credits and videos are trimmed before storage (target ≤ 10 KB `raw` per movie; the untrimmed payload is about 86 KB)                            | Done   |
| NFR-4 | **TMDB rate limits:** at most 8 parallel requests, with retries on 429/5xx                                                                                   | Done   |
| NFR-5 | **No new NuGet packages** and no new runtime dependencies                                                                                                    | Done   |
| NFR-6 | **Configurable image host and sizes** (appsettings), with sensible defaults when not configured                                                              | Done   |
| NFR-7 | **Tests:** AAA layout, lower-case names, Theory over duplication, Bogus for generated data. Every FR has at least one test                                   | Done   |
| NFR-8 | **SOLID:** new responsibilities go in new types (details repository, URL builder), and existing types are only extended                                      | Done   |
| NFR-9 | **Trust boundary:** the import SQL tolerates missing or malformed fields in NDJSON without failing the transaction (e.g. an unknown trailer site is ignored) | Done   |

## 8. Risks and known limitations

| #  | Risk / limitation                                                                                        | Mitigation / stance                                                                                                                                                                           |
|----|----------------------------------------------------------------------------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| R1 | The existing ~10k DB movies have no details until the next **full** enriched import                      | **Resolved by FR-15:** `scripts/enrich-movies.sh` enriches exactly the db's TMDB ids (a discover re-run would miss some, because its result set changes daily). Done locally: 10,009 / 10,009 |
| R2 | `POST`/`PUT` responses echo the request, so `posterUrl`/`details` are `null` there even for a TMDB movie | Accepted; this matches today's echo behaviour. Clients re-GET for the full view                                                                                                               |
| R3 | A `PUT` on a TMDB movie is overwritten by the next import (existing behaviour)                           | Unchanged; documented                                                                                                                                                                         |
| R4 | TMDB trailers can later be removed from YouTube, leaving a dead link                                     | Accepted; the next import refreshes. No link checking                                                                                                                                         |
| R5 | A full fetch is about 10k details calls plus 500 list pages, a few minutes in CI                         | Accepted (D2); still manual (D11)                                                                                                                                                             |
| R6 | TMDB can have duplicate entries sharing one IMDb id                                                      | `imdb_id` index is **non-unique** (a unique index would fail the whole import)                                                                                                                |
| R7 | The output cache (1 min) can serve pre-import data                                                       | `import.yml` already evicts the `movies` tag after import                                                                                                                                     |
| R9 | A single fetch looks like "all of TMDB" but isn't: a bare discover run only reached back to 2004, so older films (e.g. Pulp Fiction) were absent — the gap that prompted FR-19 | Build the library from several fetches (newest, highest-rated, per year, by id) and load them together; the runbook shows the recipe |
| R8 | Consumers expect `details` in the list and read `details: null` as missing data (asked during review)    | By design (D9): the list carries only `posterUrl`; details come from `GET /movies/{idOrSlug}`. Now stated in `_docs/api.md` under the list parameters                                         |

## 9. Success metrics

- After a full import, ≥ 95% of TMDB movies have `details.overview` and `posterUrl`. Directors and
  trailers are best-effort, as TMDB coverage varies.
- Every existing API and SDK test passes unchanged, which confirms backward compatibility.
- The enriched fetch reports a failure count, and ≤ 1% of ids fail.

**Actuals (local, 2026-09-22, after a full `reset-data.sh --volume --reload`):** a clean db holds
9,873 movies, all from TMDB and **0 manual**, all with details. Fetch: 10,000 listed, enriched in 5m41s
with 0 failures (9,885 lines; 115 skipped for having no release date). Load: 20s, `9873 inserted,
0 refreshed, 0 skipped` (12 duplicate ids collapsed while staging). TMDB's own coverage: posters 9,870,
trailers 8,351, taglines 5,730. Credits 120,987 (10,995 directors, 18,961 writers, 91,031 cast).

Earlier actuals (after `enrich-movies.sh` on the pre-existing db): 10,009 / 10,009 TMDB movies have details
(0 failed, 0 skipped, about 3 minutes). Coverage depends on what TMDB has: posters 10,006, runtime 9,986,
director 9,982, trailers 8,472, taglines 5,812. There are 122,694 credits, and `raw` averages about 2.4 KB.

## 10. Traceability and status (master tracker)

The plan's task statuses roll up here. Update this table whenever a plan task changes status.

| Req   | SDD section  | Plan task(s)   | Status |
|-------|--------------|----------------|--------|
| FR-1  | §3, §4       | T2, T3         | Done   |
| FR-2  | §3, §4, §6   | T3, T4, T6     | Done   |
| FR-3  | §4, §6       | T3, T5, T6     | Done   |
| FR-4  | §5, §6       | T4, T6         | Done   |
| FR-5  | §2, §4, §6   | T1, T3, T6     | Done   |
| FR-6  | §2, §4, §6   | T1, T3, T6     | Done   |
| FR-7  | §2, §4, §6   | T1, T3, T6     | Done   |
| FR-8  | §2.3, §4, §6 | T1, T3, T5, T6 | Done   |
| FR-9  | §5, §6       | T4, T6         | Done   |
| FR-10 | §2           | T1             | Done   |
| FR-11 | §4           | T3             | Done   |
| FR-12 | §8           | T7             | Done   |
| FR-13 | §6           | T7             | Done   |
| FR-14 | §10.1        | T9             | Done   |
| FR-15 | §2.1, §10.2  | T9             | Done   |
| FR-16 | §10.3        | T9             | Done   |
| FR-17 | §10.5a | T9 | Done |
| FR-18 | §11 | T10 | Done |
| FR-19 | §12 | T11 | Done |
| FR-20 | §13 | T12 | Done |
| FR-21 | §13 | T12 | Done |
| NFR-1 | §6           | T6             | Done   |
| NFR-2 | §3           | T2             | Done   |
| NFR-3 | §2           | T1             | Done   |
| NFR-4 | §2           | T1             | Done   |
| NFR-5 | all          | all            | Done   |
| NFR-6 | §6           | T5             | Done   |
| NFR-7 | §7           | all            | Done   |
| NFR-8 | §5, §6       | T4, T5         | Done   |
| NFR-9 | §4           | T3             | Done   |

## 11. Change log

| Date       | Change                                                                                                                                                                                                                                                                                                                                                                                  |
|------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| 2026-09-21 | Research and first design (findings doc, design draft)                                                                                                                                                                                                                                                                                                                                  |
| 2026-09-21 | Trailer added (D8)                                                                                                                                                                                                                                                                                                                                                                      |
| 2026-09-22 | PRD created; interview decisions D9–D15 recorded; SDD and plan rewritten against this PRD                                                                                                                                                                                                                                                                                               |
| 2026-09-22 | T1 + T2 done: enriched fetch (17/17 jq checks, live smoke 10/10), M0002 migration (full suite 82/82)                                                                                                                                                                                                                                                                                    |
| 2026-09-22 | T3 + T4 done: import transform (18/18), details repository + poster join + service (full suite 93/93)                                                                                                                                                                                                                                                                                   |
| 2026-09-22 | T5 + T6 done: URL builder (10/10), contracts/mapping/controller + endpoint tests (full suite 106/106; existing API tests unchanged)                                                                                                                                                                                                                                                     |
| 2026-09-22 | T7 + T8 done: import.yml default, attribution (README + Swagger), docs. Final: 106/106 .NET tests + 2 script suites; local E2E on The Odyssey passed; ~2.5 KB raw per enriched movie                                                                                                                                                                                                    |
| 2026-09-22 | Post-implementation (T9): FR-14 progress output, FR-15 `enrich-movies.sh` + `fetch-tmdb.sh --ids-file`, FR-16 short `stack-up.sh` summary; shared `scripts/helpers/{progress,db}.sh`; `load-movies.sh [file]`. Whole local db enriched (10,009 / 10,009). Tests 107/107 + 2 script suites                                                                                               |
| 2026-09-22 | Docs brought in line: `api.md` (list `details: null` is by design, use the single GET), `_getting-started.md` (stack-up summary), `tmdb-import.md` (four fetch modes incl. `--ids-file`; §3 `load-movies.sh [file]` + progress; new §4 enrich-movies, §5 progress/status output; stale `Resources/`, `DbInitializer`, "list is default" and "re-import skips" corrected). Added risk R8 |
| 2026-09-22 | Request collections updated (SDD §10.6): `Movies.Api.http` gains `@tmdbSlug` and a "TMDB movie with details" request; Postman gets a `tmdbSlug` variable, the broken "Get movie" URL (`/movies/`) fixed, tests for `posterUrl`/`details` (single) and `details: null` (list), and example responses captured from the live API. Postman tests not yet run (no Newman)                   |
| 2026-09-22 | `stack-up.sh` summary (FR-16) refined: details breakdown (%, posters, trailers, taglines), credits by type, last import time; scripts listed in order: fetch, then load, then the `enrich-movies.sh` shortcut                                                                                                                                                                           |
| 2026-09-23 | Final verification: build clean (no warnings), **109/109** .NET tests (Identity 3, Application 55, Api 51), both jq script suites pass, all 10 shell scripts syntax-check, fetch presets resolve correctly. Every plan step ticked; all 12 tasks and all FR/NFR rows Done |
| 2026-09-22 | Docs reorganised: `_task/<date>-<feature>/{_PRD-<feature>,spec-<feature>,plan-<feature>,_FINDINGS}.md`, superseded plans/specs moved to `_task/x-archieve/` |
| 2026-10-08 | Moved to the repo-wide layout: this PRD is tracked in `_docs/prds/`; the spec, plan and findings are in the gitignored `_tasks/` as `01-tmdb-movie-details-{spec,plan,findings}.md` |
| 2026-09-22 | `reset-data.sh --reload` clarified: it fetches `newest` and loads every `Data/*.ndjson` (it already did; the wording and confirmation line now say so) |
| 2026-09-22 | FR-20 (D22) + FR-21: fetch presets with `--help`/`--dry-run`, files named after the preset (existing files renamed), and early stop on empty pages (a `--pages 500` year fetch now ends in 3s at page 3) |
| 2026-09-22 | FR-19 (D21): one NDJSON file per fetch + multi-file loading (dedupe on `tmdb_id`). Legacy `Data/tmdb-movies.ndjson` renamed. Pulp Fiction (1994) and 40 classics loaded alongside the newest releases: 9,908 movies, oldest now 1954 |
| 2026-09-22 | FR-18 (D20): HAL `self` links on list items (`MovieLinkBuilder`); the single GET keeps none, documented in `api.md`, `architecture.md` and design decision 14. Tests 109/109; verified live |
| 2026-09-22 | Full clean start run end to end (`reset-data.sh --volume --reload`): 9,873 movies, 0 manual, 100% with details; fetch 5m41s / 0 failures, load 20s. Verified live on the API |
| 2026-09-22 | Stack lock (`scripts/helpers/lock.sh`) added to `reset-data.sh` and `stack-up.sh` after a concurrent Rider stack start collided with `reset-data.sh --volume` |
| 2026-09-22 | FR-17 (D19): `scripts/reset-data.sh` added, listed in the stack-up summary, documented in runbook §6 and `_getting-started.md` |
| 2026-09-22 | `_docs` aligned with the refined summary: the runbook's command list is in fetch-then-load order, its §5 table and example are updated, and `_getting-started.md` lists each part of the summary                                                                                                                                                                                        |
