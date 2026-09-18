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

## Authorization model

Three tiers, enforced with ASP.NET Core policies, plus an API-key filter for machine/ops access:

```
 anonymous ──▶ GET  /api/v1/movies                        (list, cached)
               GET  /api/v1/movies/{idOrSlug}             (read, cached)

 any JWT ─────▶ PUT/DELETE /api/v1/movies/{id}/ratings    (rate / un-rate)
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
