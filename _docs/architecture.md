# Architecture

A layered .NET solution. Dependencies point **inward**; the core references nothing
outward and DTOs never leak into the domain — mapping happens at the API edge
(`Movies.Api/Mapping/`).

```
        ┌───────────────────────────────── depends on ─────────────────────────────┐
        │                                                                          ▼
  ┌───────────┐     ┌────────────────┐     ┌────────────────────┐         ┌─────────────────────┐
  │ Movies.Api│────▶│Movies.Contracts│◀────│ Movies.Api.Sdk     │         │  Movies.Application │
  │ (web edge)│     │ (DTOs, leaf)   │     │ (Refit client)     │         │  (core, no deps)    │
  └─────┬─────┘     └────────────────┘     └────────────────────┘         └────────┬────────────┘
        │                                                                          │
        └──────────────────────── depends on ──────────────────────────────────────┘
                                                                                   ▲
   Movies.DbTool (CLI) ───────────────── depends on ───────────────────────────────┘
```

- `Movies.Contracts` is a leaf (no project references) — safe for both the API and the SDK to share.
- `Movies.Application` (the core) references no other project in the solution.
- `Movies.Api` depends on the core + contracts; `Movies.Api.Sdk` depends only on contracts.
- The dependency rule is verifiable: `grep` the `.csproj` files — nothing points outward from the core.

## Layering inside `Movies.Application`

The core intentionally bundles domain + application + infrastructure in one project —
right-sized for this scope. The folders map 1:1 to a project split if a second data
store ever justifies it (see [design-decisions.md](design-decisions.md)):

| Folder                       | Layer          | Concern                                            |
|------------------------------|----------------|----------------------------------------------------|
| `Models/`                    | Domain         | Entities, value objects                            |
| `Services/`, `Validators/`   | Application    | Use cases, orchestration, `FluentValidation` rules |
| `Repositories/`, `Database/` | Infrastructure | Dapper / Npgsql / FluentMigrator                   |

### Schema

| Table                         | Owner               | Notes                                                                                                |
|-------------------------------|---------------------|------------------------------------------------------------------------------------------------------|
| `movies`, `genres`, `ratings` | API (CRUD) + import | Unchanged by the TMDB details work                                                                   |
| `movie_metadata`              | import              | `tmdb_id` (unique) and `imdb_id` (non-unique index), plus the enriched TMDB `raw`                    |
| `movie_details`               | import (M0002)      | 1:1 with TMDB movies: overview, tagline, runtime, poster/backdrop paths, trailer site/key/name       |
| `movie_credits`               | import (M0002)      | Director(s), writers and top-10 cast, in order. `tmdb_person_id` is kept for a future `people` table |

TMDB details are read-only from the API's point of view.
- `MovieDetailsRepository` reads them, and `MovieService` attaches them to single-movie reads.
- `MovieRepository` joins `movie_details` for the poster on every read.
- At the edge, `Movies.Api/Mapping/TmdbUrlBuilder` turns the stored paths and keys into image and
  trailer URLs, using the `Tmdb:Images` options.

## Request pipeline (Movies.Api)

```
HTTP ─→ routing (URL-segment version) ─→ authN (JWT) ─→ authZ (policy) ─→
  controller ─→ ContractMapping (DTO→domain) ─→ Service (+ FluentValidation) ─→
  Repository (Dapper) ─→ PostgreSQL
                     ▲
   ValidationMappingMiddleware turns validation failures into 400 payloads;
   GlobalExceptionHandler turns unhandled errors into a consistent problem response.
   OutputCache serves cached reads; writes evict the movie cache tag.
```

## Project map

| Project                 | Path                           | Responsibility                                                                               |
|-------------------------|--------------------------------|----------------------------------------------------------------------------------------------|
| Movies.Contracts        | `Movies.Contracts/`            | Request/response DTOs (leaf — no project dependencies)                                       |
| Movies.Application      | `Movies.Application/`          | Core: domain models, services, validators, repositories, DB config & migrations, TMDB import |
| Movies.Api              | `Movies.Api/`                  | Web API — controllers, contract mapping, auth, caching, Swagger, health                      |
| Identity.Api            | `Identity.Api/`                | Issues the JWTs the Movies API validates                                                     |
| Movies.Api.Sdk          | `Sdk/Movies.Api.Sdk/`          | Refit client SDK (references Contracts)                                                      |
| Movies.Api.Sdk.Consumer | `Sdk/Movies.Api.Sdk.Consumer/` | Example console consumer of the SDK                                                          |
| Movies.DbTool           | `Ops.Tools/Movies.DbTool/`     | CLI: `migrate` and `import <ndjson>`                                                         |
| Tests                   | `Tests/`                       | `Movies.Api.Tests`, `Movies.Application.Tests`, `Identity.Api.Tests`, `Movies.Tests.Shared`  |

Supporting folders: `scripts/` (dev/ops shell scripts + SQL/jq helpers under
`scripts/helpers/`), `Ops.Tools/Postman/` (Postman collection + environment),
`Data/` (generated TMDB ndjson — gitignored), `_docs/` (this documentation),
`_task/` (working specs/plans — gitignored).

The solution's virtual folders (`Sdk`, `Tests`, `Ops.Tools`, `IAM`) mirror this
on-disk layout.
