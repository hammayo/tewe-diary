# TeweRestApi

A layered .NET 9 Movies REST API with a separate Identity token service. Movies
and ratings are exposed through a versioned API with JWT-protected writes; a Refit
SDK and an example consumer show how to call it.

## Project map

| Project                 | Path                           | Responsibility                                                                               |
|-------------------------|--------------------------------|----------------------------------------------------------------------------------------------|
| Movies.Contracts        | `Movies.Contracts/`            | Request/response DTOs (leaf — no project dependencies)                                       |
| Movies.Application      | `Movies.Application/`          | Core: domain models, services, validators, repositories, DB config & migrations, TMDB import |
| Movies.Api              | `Movies.Api/`                  | Web API — controllers, contract mapping, auth, Swagger, health                               |
| Identity.Api            | `Identity.Api/`                | Issues JWTs the Movies API validates                                                         |
| Movies.Api.Sdk          | `Sdk/Movies.Api.Sdk/`          | Refit client SDK (references Contracts)                                                      |
| Movies.Api.Sdk.Consumer | `Sdk/Movies.Api.Sdk.Consumer/` | Example console consumer of the SDK                                                          |
| Movies.DbTool           | `Ops.Tools/Movies.DbTool/`     | CLI: `migrate` and `import <ndjson>`                                                         |
| Tests                   | `Tests/`                       | `Movies.Api.Tests`, `Movies.Application.Tests`, `Identity.Api.Tests`, `Movies.Tests.Shared`  |

Supporting folders: `scripts/` (dev/ops shell scripts + SQL/jq helpers under
`scripts/helpers/`), `Ops.Tools/Postman/` (Postman collection + environment),
`Data/` (generated TMDB ndjson — gitignored), `_docs/` (notes).

## Architecture

The dependency rule points inward: `Movies.Contracts` is a leaf, `Movies.Application`
depends on no other project in the solution, and everything else depends on those.
DTO mapping stays in `Movies.Api/Mapping/` so the core never references the API contracts.

`Movies.Application` bundles domain + application + infrastructure in one project —
a deliberate choice at this size. The folders map 1:1 to a future split if a second
data store ever warrants it:

| Folder                       | Layer          | Concern                          |
|------------------------------|----------------|----------------------------------|
| `Models/`                    | Domain         | Entities, value objects          |
| `Services/`, `Validators/`   | Application    | Use cases, orchestration         |
| `Repositories/`, `Database/` | Infrastructure | Dapper / Npgsql / FluentMigrator |

## Running

**Full stack (API + Identity + Postgres):**
- Rider: run the `Full Stack` or `Docker Stack` configuration under `.run/`.
- Shell: `bash scripts/stack-up.sh` (builds and starts the Docker stack, then prints URLs).

**Auth flow:** request a JWT from `Identity.Api` (`POST /token`), then call the
Movies API with `Authorization: Bearer <token>` for protected writes.

## Tests

```bash
dotnet test TeweRestApi.sln
```

Integration suites (`Movies.Api.Tests`, `Movies.Application.Tests`) use a Postgres
fixture and need Docker running. The transform fixture check is a plain script:

```bash
bash scripts/tests/test_tmdb_transform.sh
```

## Regenerating TMDB data

The movie dataset is generated, not committed (`Data/` is gitignored).

```bash
bash scripts/fetch-tmdb.sh                 # writes Data/tmdb-movies.ndjson (needs a TMDB API key)
bash scripts/load-movies.sh                # loads it into the running Docker db via psql
# or, via the CLI tool:
dotnet run --project Ops.Tools/Movies.DbTool -- import Data/tmdb-movies.ndjson
```

`Ops.Tools/Movies.DbTool` also runs schema migrations: `dotnet run --project Ops.Tools/Movies.DbTool -- migrate`.
