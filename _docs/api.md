# API reference (v1)

Base path is versioned via URL segment: `/api/v{version}` (v1 today). The API
advertises supported versions in the `api-supported-versions` response header
(`ReportApiVersions = true`), and defaults to `v1` when unspecified.

Interactive docs: Swagger UI at `/swagger` (Development only), one document per API version.

## Endpoints

| Method   | Route                         | Auth                  | Notes                                   |
|----------|-------------------------------|-----------------------|-----------------------------------------|
| `POST`   | `/api/v1/movies`              | Trusted               | Create; `201 Created` + `Location`      |
| `GET`    | `/api/v1/movies/{idOrSlug}`   | Anonymous             | Read by GUID **or** slug; output-cached |
| `GET`    | `/api/v1/movies`              | Anonymous             | Paged + filterable list; output-cached  |
| `PUT`    | `/api/v1/movies/{id}`         | Trusted               | Update                                  |
| `DELETE` | `/api/v1/movies/{id}`         | Admin                 | Delete                                  |
| `PUT`    | `/api/v1/movies/{id}/ratings` | Authenticated         | Rate a movie                            |
| `DELETE` | `/api/v1/movies/{id}/ratings` | Authenticated         | Remove own rating                       |
| `GET`    | `/api/v1/ratings/me`          | Authenticated         | Caller's ratings                        |
| `POST`   | `/api/v1/admin/cache/evict`   | API key (`x-api-key`) | Evict the movie cache tag               |
| `POST`   | `/token` *(Identity.Api)*     | —                     | Issue a JWT                             |
| `GET`    | `/_health`                    | —                     | Liveness/readiness (DB check)           |

Writes evict the movie output-cache tag, so cached reads stay correct after a mutation.

### TMDB fields on movie responses

`MovieResponse` has two nullable TMDB fields, both added without breaking existing clients:

| Call                                   | `posterUrl`                  | `details`                   |
|----------------------------------------|------------------------------|-----------------------------|
| `GET /movies/{idOrSlug}`, TMDB movie   | URL or `null`                | object                      |
| `GET /movies/{idOrSlug}`, manual movie | `null`                       | `null`                      |
| `GET /movies` (list)                   | URL or `null`                | `null` (use the single GET) |
| `POST` / `PUT` responses               | `null` (echo of the request) | `null`                      |

```json
{
  "id": "…", "title": "The Odyssey", "slug": "the-odyssey-2026", "yearOfRelease": 2026,
  "genres": ["Adventure", "Action", "Fantasy"], "rating": null, "userRating": null,
  "posterUrl": "https://image.tmdb.org/t/p/w500/5rhTDKUhPYvpdQIijFIs5VoWsON.jpg",
  "details": {
    "tmdbId": 1368337, "imdbId": "tt33764258",
    "overview": "Odysseus, the legendary King of Ithaca, embarks on …",
    "tagline": "Defy the gods.", "runtimeMinutes": 173,
    "backdropUrl": "https://image.tmdb.org/t/p/w1280/RMXG8myu1aGlNUsRjtxzmpdMK0.jpg",
    "trailer": {
      "site": "YouTube", "key": "Mzw2ttJD2qQ", "name": "Official Trailer",
      "url": "https://www.youtube.com/watch?v=Mzw2ttJD2qQ",
      "embedUrl": "https://www.youtube.com/embed/Mzw2ttJD2qQ"
    },
    "directors": [{ "tmdbPersonId": 525, "name": "Christopher Nolan", "role": null, "profileUrl": "…" }],
    "writers":   [{ "tmdbPersonId": 525, "name": "Christopher Nolan", "role": "Writer", "profileUrl": "…" }],
    "cast":      [{ "tmdbPersonId": 1892, "name": "Matt Damon", "role": "Odysseus", "profileUrl": "…" }]
  }
}
```

- `cast` holds up to 10 members in billing order, and `role` is the character name.
- For writers, `role` is the job (Screenplay, Writer, Story, Novel or Author). Directors have `role: null`.
- Image sizes come from `Tmdb:Images` in `appsettings.json`.
- This product uses the TMDB API but is not endorsed or certified by TMDB.

## Authorization model

Three tiers, enforced with ASP.NET Core policies, plus an API-key filter for machine/ops access:

```
 anonymous ──▶ GET  /api/v1/movies                        (list, cached)
               GET  /api/v1/movies/{idOrSlug}             (read, cached)

 any JWT ────▶ PUT/DELETE /api/v1/movies/{id}/ratings     (rate / un-rate)
               GET         /api/v1/ratings/me             (my ratings)

 "Trusted" ──▶ POST /api/v1/movies                        (create)
               PUT  /api/v1/movies/{id}                   (update)

 "Admin" ────▶ DELETE /api/v1/movies/{id}                 (delete)

 API key ────▶ POST /api/v1/admin/cache/evict             (invalidate cache tag)
```

Claims (`admin`, `trusted_member`) are carried in the JWT. The `Trusted` policy is
satisfied by **either** an admin or a trusted-member claim. Grant claims when minting
the token via `CustomClaims` (see [_getting-started.md](_getting-started.md)).

## List query parameters

`GET /api/v1/movies` accepts:

| Param      | Type   | Default  | Meaning                                                       |
|------------|--------|----------|---------------------------------------------------------------|
| `title`    | string | —        | Case-insensitive partial match                                |
| `year`     | int    | —        | Filter by year of release                                     |
| `sortBy`   | string | —        | Sort field; prefix `-` for descending (e.g. `-yearOfRelease`) |
| `page`     | int    | `1`      | Page number                                                   |
| `pageSize` | int    | `10`     | Items per page                                                |

Responses are paged (`PagedResponse`): items plus `page`, `pageSize`, and `total`.

Each item carries a HAL `links` array with its own URL, so a client can follow it instead of building
the path:

```json
"links": [ { "href": "https://localhost:7001/api/v1/movies/the-odyssey-2026", "rel": "self", "type": "GET" } ]
```

`href` is absolute and generated from the route (so the version segment is always right); `type` is the
HTTP method to use on it. The single-movie response has **no** `links`: the caller already has that URL.
`links` is omitted entirely when there is no link to give.

Each list item includes `posterUrl` but always has `details: null`. That's by design, so a page of
movies doesn't need an extra details query per movie. To get a movie's overview, tagline, trailer and
credits, request it by id or slug: `GET /api/v1/movies/{idOrSlug}` (see
[TMDB fields on movie responses](#tmdb-fields-on-movie-responses)).

## Error contract

Validation failures return `400 Bad Request` with a consistent body
(`ValidationFailureResponse`), produced by `ValidationMappingMiddleware`:

```json
{
  "errors": [
    { "propertyName": "Title", "message": "'Title' must not be empty." }
  ]
}
```

Unhandled exceptions are turned into a consistent problem response by
`GlobalExceptionHandler` (no stack traces leak to clients). Standard status codes
apply otherwise: `401` (no/invalid token), `403` (policy not satisfied), `404` (not found).
