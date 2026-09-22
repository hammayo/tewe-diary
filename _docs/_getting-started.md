# Getting started

Clone ─→ configure ─→ run ─→ call it, in a few minutes.

## Prerequisites

- **.NET 9 SDK**
- **Docker** (Desktop or engine) — the stack and the integration tests use it
- **`jq`** — used by the TMDB fetch/transform scripts
- A **TMDB API key** (only needed to regenerate data) — https://www.themoviedb.org/settings/api

## Configure

Copy the tracked example and fill in real values (`.env` is gitignored):

```bash
cp .env.example .env
```

`.env.example` ships **working local-dev defaults** — after `cp` the stack runs as-is.
The only value you must supply is `TMDB_API_KEY`, and only if you want to regenerate
data. This table is the reference for what each variable means (the example file itself
is kept comment-free):

| Variable                              | Used by                    | Notes                                                                                                                                                            |
|---------------------------------------|----------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `IDENTITY_API_URL`, `MOVIES_API_URL`  | SDK consumer, tooling      | Base URLs for the two services                                                                                                                                   |
| `POSTGRES_HOST/PORT/DB/USER/PASSWORD` | docker-compose, Movies.Api | Database connection parts                                                                                                                                        |
| `Database__SslMode`                   | Movies.Api                 | `Disable` locally (Docker has no TLS); `Require` against Azure Flexible Server                                                                                   |
| `Database__MigrateOnStartup`          | Movies.Api                 | `true` locally; `false` in Azure (the deploy pipeline migrates instead)                                                                                          |
| `Database__ConnectionString`          | Movies.Api                 | Optional. A full connection string **overrides** the `POSTGRES_*` parts above. Left blank locally                                                                |
| `KeyVault__Uri`                       | Movies.Api                 | **Azure-only** — secrets via managed identity. Leave unset locally                                                                                               |
| `JWT_TOKEN_SECRET`                    | **both APIs**              | **Must be identical** in Identity.Api and Movies.Api — the shared signing secret. HS256 needs ≥ 32 chars                                                         |
| `JWT_ISSUER`, `JWT_AUDIENCE`          | both APIs                  | Must match on both sides or tokens are rejected                                                                                                                  |
| `API_KEY`                             | Movies.Api                 | Sent as the `x-api-key` header for admin/ops endpoints                                                                                                           |
| `ASPNETCORE_DEV_CERT_PASSWORD`        | stack                      | Dev HTTPS cert password. `scripts/stack-up.sh` generates `.certs/aspnet-dev.pfx` with it if missing; any non-empty value works locally                           |
| `TMDB_API_KEY`                        | `scripts/fetch-tmdb.sh`    | Only for data regeneration. Accepts a **v3 API key** (32-char hex) or a **v4 read-access token** (a JWT); the script detects the shape and picks the auth scheme |

## Run

**Full stack (Movies API + Identity + Postgres):**
- Rider: run the `Full Stack` or `Docker Stack` configuration under `.run/`.
- Shell: `bash scripts/stack-up.sh` builds and starts the stack detached, then prints the URLs and a
  **Movies data** summary:
  - movie counts (TMDB and manual)
  - TMDB details coverage, with how many movies have posters, trailers and taglines
  - credits by type (directors, writers, cast)
  - when TMDB data was last imported
  - the data scripts in the order you use them: `fetch-tmdb.sh`, then `load-movies.sh`, or the
    `enrich-movies.sh` shortcut for movies already in the db
  - a next step, only when one is needed, plus `reset-data.sh` for a clean start
  See the [TMDB import runbook](tmdb-import.md#5-progress-and-status-output).

## URLs & ports

| Service      | HTTP                         | HTTPS                  | Notes                                       |
|--------------|------------------------------|------------------------|---------------------------------------------|
| Movies.Api   | http://localhost:5001        | https://localhost:7001 | Swagger UI at `/swagger` (Development only) |
| Identity.Api | http://localhost:5003        | https://localhost:7003 | `POST /token`                               |
| Postgres     | `localhost:${POSTGRES_PORT}` | —                      | default `5432`                              |

Health check: `GET http://localhost:5001/_health`.

## Quickstart — get a token and create a movie

`Identity.Api` mints a JWT; `CustomClaims` become claims on the token, so you can grant
yourself the `trusted_member` (create/update) or `admin` (delete) role for testing.

```bash
# 1) Mint a token with the "trusted_member" claim
TOKEN=$(curl -sk https://localhost:5003/token \
  -H 'Content-Type: application/json' \
  -d '{
        "userId": "d8566de3-b1a6-4a9b-b842-8e3887a82e41",
        "email": "dev@test.local",
        "customClaims": { "trusted_member": true }
      }' | tr -d '"')

# 2) Create a movie (Trusted policy required)
curl -sk https://localhost:7001/api/v1/movies \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{ "title": "Blade Runner", "yearOfRelease": 1982, "genres": ["Science Fiction"] }'

# 3) Read it back (anonymous, cached)
curl -sk "https://localhost:7001/api/v1/movies?title=blade&page=1&pageSize=10"
```

See [api.md](api.md) for the full endpoint surface, auth tiers, and error contract.
To populate real data, see the [TMDB import runbook](tmdb-import.md).
