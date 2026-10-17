# Tewe Diary

A production-shaped .NET 9 REST API for movies and ratings. It is a portfolio piece: the engineering decisions are the point, not feature count. `mission-critical` copies this repo's conventions, so changes here set the pattern for both.

## Where to look

- `_docs/architecture.md`: project map, dependency rule, request pipeline, schema
- `_docs/design-decisions.md`: why, and when to revisit
- `_docs/api.md`: endpoints, authorisation tiers, error contract
- `_docs/testing.md`: test strategy and conventions
- `_docs/_getting-started.md`: every `.env` and `appsettings.json` setting, and how to run
- `_docs/prds/`: PRDs, tracked. `_tasks/`: specs, plans and findings, gitignored

## Rules that must hold in code

- Dependencies point inward. `Movies.Application` references no other project, and `Movies.Contracts` is a leaf. DTOs never reach the domain; mapping happens at the API edge (`backend/src/Movies.Api/Mapping/`)
- Dapper over Postgres. The schema is owned by FluentMigrator migrations, never by import scripts
- Secrets and per-environment values go in `.env` (gitignored, tracked `.env.example`). Non-secret tuning goes in `appsettings.json`. Nothing is hardcoded in C#
- Authorisation is policy-based (`Admin`, `Trusted`), with the JWT signing secret shared with Identity
- Writes evict the output-cache tag for movies
- Central package management (`backend/Directory.Packages.props`)

## This repo's stack is not a violation

Controllers with URL-segment versioning, FluentValidation, Refit SDK, output caching and Swashbuckle are deliberate (see the design decisions). Skills that mention other styles do not apply to them.

## Claude skills (`hb-extensions` plugin)

- `csharp-conventions` applies.
- `csharp-domain-modelling` applies to **new** domain code only. Existing `Models/` classes with `{ get; set; }` and the one enum are legacy: leave them unless a task is about them, and say so when you pass one.
- `financial-ledger` does not apply: there is no ledger, outbox or broker here.

## Development workflow

- **PRDs are tracked** in `_docs/prds/` as `_<nn>-<feature-name>-prd.md`
- **Working docs live in `_tasks/`** (gitignored): `<nn>-<feature-name>-spec.md`, `-plan.md`, `-tasks-progress.md`. Superseded drafts go in `_tasks/x-archieve/`
- Tracked docs never link into `_tasks/`: it is gitignored, so the link breaks on a fresh clone. Name those files as plain code paths instead
- Check `_docs/prds/` and `_tasks/` before starting work
- **Never commit.** Stage the unit's files with `git add` and give a one-line draft commit message

## Conventions

- **Strict AAA tests**: `// Arrange`, `// Act`, `// Assert` in every test; `[Theory]` where inputs vary
- **Testcontainers** for Postgres (`Movies.Tests.Shared/PostgresFixture`): the data layer is exercised, not mocked. Docker must be running
- **Bogus** for test data
- Write docs in plain first person, UK English, without filler. State each fact in one doc and link to it from the others

## Build & test

```bash
cp .env.example .env
bash scripts/stack-up.sh
dotnet test backend/TeweDiary.sln
```

CI and deployment: `_docs/ci-cd.md`. SonarCloud analysis: `.github/workflows/sonarcloud.yml` (setup in `_docs/_getting-started.md`).
