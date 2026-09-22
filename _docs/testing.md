# Testing

```bash
dotnet test TeweRestApi.sln
```

## Strategy

- **Unit tests** (`Movies.Application.Tests`) cover domain/service logic, validators,
  slug generation, migration wiring, and the import transform.
- **Integration tests** (`Movies.Api.Tests`, `Movies.Application.Tests`) run against a
  **real PostgreSQL** via a Testcontainers-backed fixture (`Movies.Tests.Shared/PostgresFixture`) —
  the data layer is exercised, not mocked. **Docker must be running.**
- **Identity tests** (`Identity.Api.Tests`) verify token issuance end to end using a
  `WebApplicationFactory`.
- Shared helpers (`Movies.Tests.Shared`) provide the Postgres fixture, deterministic
  `Bogus` test data, and JWT helpers.

## Conventions

- **AAA** — every test has explicit `// Arrange`, `// Act`, `// Assert` sections.
- `[Theory]` over duplicated `[Fact]`s where inputs vary.
- Lower-case, behaviour-describing test names; no logic in assertions.
- Test data via `Bogus`, not hand-rolled literals.

## Fixture check (no .NET required)

The TMDB NDJSON transform has a dependency-free golden-file check:

```bash
bash scripts/tests/test_tmdb_transform.sh
bash scripts/tests/test_tmdb_details_transform.sh   # details enrichment: credit trimming + trailer selection
```

It runs `scripts/helpers/tmdb-to-ndjson.jq` over a sample TMDB response and diffs the
output against `scripts/tests/fixtures/expected.ndjson`.
