# PRD — v2 Minimal API (v1 parity)

|             |                                                                                                                      |
|-------------|----------------------------------------------------------------------------------------------------------------------|
| **Status**  | Built and verified — 2026-10-22. Scope is the v1 parity port. The browse design is **Parked** (see §9)                            |
| **Owner**   | Hammy Babar                                                                                                          |
| **Branch**  | `feature/roadmap-minimal-api-v2`                                                                                     |
| **Doc set** | **PRD (this)** → SDD `_tasks/03-v2-browse-api-spec.md` → plan `_tasks/03-v2-browse-api-plan.md`                      |
| **Note**    | `_tasks/` is gitignored, so those two docs exist only in my local checkout and are not linked. The file name still says "browse" from the first draft; the browse design now lives in §9. |

Status legend (used in all three docs): `Todo` · `In progress` · `Done` · `Blocked` · `Dropped` · `Deferred`
(`Deferred` means in the PRD on purpose, not built in this release).

## 1. Problem

v2 is one endpoint (`GET /api/v2/movies/{idOrSlug}`). It proves that Minimal APIs run beside the v1 controllers, but
it cannot be used as an API. The next step is  v1 surface be served by Minimal APIs, with the same behaviour

## 2. Goals

1. Every v1 endpoint except the admin cache evict has a v2 twin under `/api/v2` (eight of nine), with the **same request and response shapes** and the same behaviour.
2. v2 follows the reference layout: one class per endpoint, area aggregators, one shared `ApiVersionSet`.
3. Swagger shows both versions, each with its own operations and Bearer authorisation works on both.
4. v1 is not changed (NFR-1).

## 3. Non-goals (this release)

- Any difference between v1 and v2 in shapes, routes (other than the version segment), policies or caching.
- The browse design in §9: cards, detail shape, genre filter, search, ranking data, home, similar, people.
- A v2 rate-limit, CORS, compression or cache policy. v2 reuses v1's.
- A v2 admin cache evict (FR-9): only the deploy pipeline calls it, and it keeps using v1.
- A v2 SDK client, and the frontend (see `_04-react-frontend-prd.md`, which calls the v2 parity port).
- Migrations, new packages or new configuration keys.

## 4. Users and use cases

| Who                | Needs                                                                                                          |
|--------------------|----------------------------------------------------------------------------------------------------------------|
| Reviewer           | See the same API implemented twice, as controllers and as Minimal APIs, and compare the two                    |
| API client         | Call `/api/v2/...` with exactly what it sends to v1 and get exactly what v1 returns                            |
| Future v2 work     | A clean place to add the parked browse design without restructuring                                            |

## 5. Decisions (my calls — please confirm in review)

| Id   | Decision                                                                                                                                                                                                  |
|------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| D1   | **Layout follows the reference.** `Movies.Api/Endpoints/V2/` holds one static class per endpoint (`CreateMovieEndpoint`, `GetMovieEndpoint` and so on), grouped under `Movies/` and `Ratings/`. |
| D2   | **One shared `ApiVersionSet`** (versions 1.0 and 2.0) replaces the skeleton's local set. v2 endpoints declare `HasApiVersion(2.0)`. v1 controllers are untouched.                                          |
| D3   | **Same shapes.** v2 reuses the v1 request and response types in `Movies.Contracts`. No v2-only DTOs, so nothing new is mapped.                                                                           |
| D4   | **Same policies and cache.** Same `Trusted`, `Admin` and authenticated requirements, the `MovieCache` policy on the two reads, and eviction of the `movies` tag on every write.                           |
| D5   | **Same routes.** The existing `ApiEndpoints` constants carry the `v{version:apiVersion}` segment, so v2 reuses them.                                                                                       |
| D6   | **Thin handlers.** Each handler calls the same service method as its v1 controller and returns `TypedResults` where the result type is fixed, `Results` where it is not.                                  |
| D7   | **Tests mirror v1.** Each v1 endpoint test has a v2 counterpart, parameterised over the version where the two behave identically. The spec starts with a v1-versus-v2 behaviour table per endpoint, and the tests are written from it. |
| D8   | **Errors match v1 exactly.** `[ApiController]` gives v1 a `ProblemDetails` body on `404` and on model-binding `400`s, and Minimal APIs do not. v2 produces the same bodies through one shared helper and exception handling, not per handler. |
| D9   | **Route names carry the version** (`GetMovieV2`, as in the reference), so `CreatedAtRoute` and `MovieLinkBuilder` resolve the right route. `MovieLinkBuilder` is made version-aware, and v2 list items get their `self` link. |

## 6. Functional requirements

| Id     | Requirement                      | Acceptance                                                                                                                                                                       | Status |
|--------|----------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|--------|
| FR-1   | Shared version set and layout    | One `ApiVersionSet` (1.0 and 2.0) and aggregators `MapMovieEndpoints` and `MapRatingEndpoints` behind one `MapApiEndpoints`. The skeleton is split into its own class | Done   |
| FR-2   | Create movie                     | `POST /api/v2/movies` needs `Trusted`. Returns `201` with `Location` and the movie, `400` with the validation body on bad input, `401` and `403` as in v1                         | Done   |
| FR-3   | Get movie                        | `GET /api/v2/movies/{idOrSlug}` is anonymous and cached. A GUID looks up by id, anything else by slug. `404` when missing. `userRating` is filled for a signed-in caller          | Done   |
| FR-4   | List movies                      | `GET /api/v2/movies` with the v1 filters, sorting and paging. Anonymous and cached. Items carry the same `self` link, absolute and pointing at the v2 URL (the skeleton passes no link builder today). A malformed query (`?page=abc`) gets v1's `400` body | Done   |
| FR-5   | Update movie                     | `PUT /api/v2/movies/{id}` needs `Trusted`. `200` with the movie, `404` when missing, `400` on bad input                                                                          | Done   |
| FR-6   | Delete movie                     | `DELETE /api/v2/movies/{id}` needs `Admin`. `200` or `404` as in v1                                                                                                              | Done   |
| FR-7   | Rate and un-rate                 | `PUT` and `DELETE /api/v2/movies/{id}/ratings` need a JWT. `404` for an unknown movie, `400` for a rating outside 1 to 5                                                         | Done   |
| FR-8   | My ratings                       | `GET /api/v2/ratings/me` needs a JWT and returns the caller's ratings                                                                                                            | Done   |
| FR-9   | Admin cache evict                | **Dropped.** `POST /api/v2/admin/cache/evict` is not built. It is a machine call for the deploy pipeline, which already uses the v1 endpoint, so v2 does not need a twin | Dropped |
| FR-10  | Swagger shows both versions      | The Swagger UI lists a v1 and a v2 document. v2 lists its eight operations, v1 still lists its nine, and the Bearer scheme applies to both                                         | Done   |
| FR-11  | Docs and tooling                 | `_docs/api.md` lists the v2 endpoints, says they match v1 and drops "not designed yet". `_docs/architecture.md`, decision 15 and `Movies.Api.http` describe and exercise the final layout. `_docs/prds/_04-react-frontend-prd.md` gets the cache finding from NFR-10 | Done   |

## 7. Non-functional requirements

| Id      | Requirement                                                                                                                                                                                       | Status |
|---------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|--------|
| NFR-1   | **v1 is untouched.** No v1 route, contract, query, policy or controller changes, and the v1 tests pass unchanged on every step                                                                    | Done   |
| NFR-2   | **Identical contract.** For the same request, v1 and v2 return the same status code, headers that matter (`Location`, content type) and body shape, including every error: the `ValidationFailureResponse` `400`, the `ProblemDetails` `404`, the binding `400` for a malformed body or query, and the `401` and `403`. The only allowed difference is the version in links and `Location` | Done   |
| NFR-3   | **Cache correctness.** Every v2 write evicts the `movies` tag. A v2 write is visible to the next v1 or v2 read, and the reverse. v1 and v2 reads are cached as separate entries (the path differs) and both vary by the same query keys | Done   |
| NFR-4   | **Authorisation** is policy-based, with the same policies and the same JWT secret. No v2 endpoint is more open than its v1 twin                       | Done   |
| NFR-5   | **No new moving parts.** No migrations, packages, services or `appsettings.json` keys                                                                                                              | Done   |
| NFR-6   | **Tests:** strict AAA, Testcontainers Postgres, Bogus data. Happy path, `404`, `400` and auth (`401`, `403`) for every endpoint. A Swagger test checks that both documents exist and v2 has eight operations | Done   |
| NFR-7   | **Swagger:** operations carry names, tags and `Produces` metadata, so the UI and the OpenAPI document match the controllers' quality. Operation ids are unique across versions                    | Done   |
| NFR-8   | **Repo rules:** dependencies point inward, DTOs stay in `Movies.Contracts`, mapping stays in `Movies.Api/Mapping/`, nothing is hardcoded in C#                                                    | Done   |
| NFR-9   | **Error parity is tested, not assumed.** A parameterised test sends the same bad request (unknown id, invalid body, malformed JSON, `?page=abc`, missing or wrong role) to v1 and v2 and asserts the same status and body shape | Done   |
| NFR-10  | **Signed-in reads are not served from the anonymous cache.** The default output-cache policy skips requests with an `Authorization` header. A v2 test proves a signed-in `GET` returns the caller's `userRating`, and the result is recorded for the frontend PRD's first spike | Done   |

## 8. Risks and known limitations

- **Duplicate route names.** v1 and v2 would register the same endpoint names (for example `GetMovie`), which breaks
  `CreatedAtRoute` and `LinkGenerator`. The names need a version suffix, as the reference does (`GetMovieV2`). The spec
  settles how `MovieLinkBuilder` finds the right route per version.
- **Swashbuckle with two kinds of endpoint.** Controllers and Minimal APIs are described by different explorers.
  The skeleton already shows up, but eight operations may expose duplicate operation ids or missing tags.
  I will check the real UI, not only the JSON.
- **Error bodies differ by default.** `[ApiController]` turns `NotFound()` and binding failures into `ProblemDetails`.
  `Results.NotFound()` returns an empty body, and a bad JSON body or `?page=abc` raises `BadHttpRequestException`.
  D8 closes this with a shared helper and handling. `ValidationMappingMiddleware` already runs before both
  `MapControllers` and the endpoints, so the validation `400` should carry over, but NFR-9 proves it.
- **Swagger depends on a real check.** Resolved in T7. Checking the running UI found that its version list was built
  before the v2 endpoints were mapped, so it offered only v1 (the documents were fine). `UseSwaggerUI` now runs after
  the endpoints are mapped. v2 operations also needed tags matching v1's.
- **Links and `Location`.** `MovieLinkBuilder` resolves routes by name, and the skeleton passes it no builder, so a v2
  list would silently lose its `self` links (D9, FR-4).
- **Two cache namespaces, one tag.** A write must evict both versions' entries (NFR-3). The shared `movies` tag does
  this, and the test confirms it.
- **Same weaknesses as v1.** v2 inherits v1's behaviour on purpose, including the 60-second cache and list query
  cost. Fixing either belongs to the parked browse design (§9), not to this release.
- **Duplicated behaviour.** Two surfaces calling the same services will drift unless the shared tests keep them tied.
  That is the reason for D7.
- **The v2 skeleton's existing tests** assert only the read endpoint. They must pass unchanged after the split.

## 9. Parked: the browse design

I designed a different v2 earlier and parked it. It is the likely next step once parity is in place, and the reason
the version set is shared. It is not built, and none of it is in scope above.

**Why it was parked.** The first phase of the React frontend (`_04-react-frontend-prd.md`) needs only what v1 and the
v2 parity port serve. The browse design is for when the UI hits that limit. Phase 2 of the frontend (top rated, popular,
genre and watch-list rows) is the first consumer, and its PRD §8 maps each gap to the B-ids below.

**The design, kept as written.**

| Id      | Idea                                                                                                                                                                         |
|---------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| B1      | Two shapes: a *card* (id, slug, title, year, genres, `posterUrl`, `backdropUrl`, `rating`, `ratingCount`) and a *detail*. Cards carry no per-user data                       |
| B2      | Catalogue reads are anonymous and cached. Per-user data appears only in the detail's `myRating` and in rating responses. A request with `Authorization` is never cached     |
| B3      | `GET /api/v2/movies?genre=&year=&q=&sort=&page=&pageSize=` returns cards and `hasNextPage` (limit plus one, no total count). `pageSize` capped at 25                          |
| B4      | `GET /api/v2/search?q=` returns up to 8 cards by title (`ilike`, wildcards escaped, `pg_trgm` index where allowed), rate-limited to 30 a minute per IP                       |
| B5      | `GET /api/v2/genres` with movie counts                                                                                                                                       |
| B6      | A v2-only cache policy varying by every query parameter, plus compression, `Cache-Control: public, max-age=60` and a CORS policy with origins from configuration             |
| B7      | Additive indexes in one expand-only migration: `genres(name, movieid)`, `genres(movieid)`, `ratings(movieid)`, a `pg_trgm` index on `movies.title`                          |
| B8      | Card `rating` is the community average (one decimal) or `null`, with `ratingCount`. A TMDB-score fallback comes with the ranking data                                        |
| B9      | Ranking columns on `movie_details` (popularity, vote average and count, release date), then ranking sorts, a home screen, similar movies, people and `GET /me/ratings`      |
| B10     | Targets: cached reads p95 under 200 ms and uncached browse under 400 ms on the seeded 11k database, a 25-card page under 15 KB, and a load script under `scripts/`          |
| B11     | A watch list that follows the signed-in user: a `watchlist(userid, movieid, addedat)` table in an expand-only migration, `GET`, `PUT` and `DELETE` under `/api/v2/me/watchlist`, any valid JWT, never cached, and the list returns cards (B1) |

When this is picked up it becomes a new PRD, written against the parity layout.

## 10. Success metrics

- Every v1 endpoint except the admin cache evict has a v2 twin, and the shared tests pass for both versions.
- The Swagger UI shows v1 and v2, and I can call each v2 operation from it with a Bearer token.
- A write through v2 shows up in a v1 read at once, and the reverse.
- The v1 suite passes unchanged (NFR-1).

## 11. Traceability and status (master tracker)

The plan's task statuses roll up here. Update this table whenever a plan task changes status. NFR-1, NFR-5 and NFR-8
are checked in T9, the final verification.

| Req     | SDD section  | Plan task(s)  | Status    |
|---------|--------------|---------------|-----------|
| FR-1    | §3.1, §3.2   | T1            | Done      |
| FR-2    | §2, §3.2     | T4            | Done      |
| FR-3    | §2, §3.3     | T3            | Done      |
| FR-4    | §2, §3.5     | T3            | Done      |
| FR-5    | §2           | T4            | Done      |
| FR-6    | §2           | T4            | Done      |
| FR-7    | §2           | T5            | Done      |
| FR-8    | §2           | T5            | Done      |
| FR-9    | —            | T6            | Dropped   |
| FR-10   | §3.6         | T7            | Done      |
| FR-11   | §5           | T8            | Done      |
| NFR-1   | §3.5         | every task    | Done      |
| NFR-2   | §3.4         | T2 to T6      | Done      |
| NFR-3   | §2           | T3, T4        | Done      |
| NFR-4   | §2, §3.4     | T4 to T6      | Done      |
| NFR-5   | —            | T9            | Done      |
| NFR-6   | §4           | T2 to T7      | Done      |
| NFR-7   | §3.6         | T7            | Done      |
| NFR-8   | §3.1         | T9            | Done      |
| NFR-9   | §4           | T2 to T6      | Done      |
| NFR-10  | §4           | T3, T7        | Done      |

## 12. Change log

| Date        | Change                                                                                                                                    |
|-------------|-------------------------------------------------------------------------------------------------------------------------------------------|
| 2026-10-17  | First draft: browse API, decisions D1 to D9, FR-1 to FR-14, NFR-1 to NFR-14                                                               |
| 2026-10-17  | Rating scale corrected to 1 to 5 (matches `RatingService`)                                                                                |
| 2026-10-17  | Trimmed to the slim scope; the frontend is built on v1 first, so the PRD was parked                                                       |
| 2026-10-22  | Rescoped to a v1 parity port in the style of the course's `7.MinimalApi` reference. The browse design moved to §9. FR and NFR rewritten   |
| 2026-10-22  | Gap review: errors match v1 exactly (D8), versioned route names and links (D9), error and cache parity tests (NFR-9, NFR-10), wider docs scope |
| 2026-10-22  | FR-9 and NFR-4 corrected: the admin evict is guarded by the `x-api-key` header, not the `Admin` policy                                    |
| 2026-10-22  | Built (T1 to T7): eight v2 endpoints, error and cache parity tests, Swagger lists both versions. Swagger UI version list bug found by a live check and fixed. T8 docs done |
| 2026-10-22  | FR-9 dropped: the admin cache evict stays v1-only. v2 has eight endpoints; the API-key endpoint filter was removed |
| 2026-10-22  | Browse design §9: added B11 (watch list) and pointed at the frontend PRD's gap table                                                  |
