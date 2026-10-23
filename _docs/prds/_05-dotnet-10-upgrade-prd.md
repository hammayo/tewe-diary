# PRD — Upgrade to .NET 10

|              |                                                                                                         |
|--------------|---------------------------------------------------------------------------------------------------------|
| **Status**   | Draft — 2026-10-23 (nothing built; every requirement is `Todo`, see §10)                                |
| **Owner**    | Hammy Babar                                                                                             |
| **Branch**   | `feature/roadmap-dotnet-10-upgrade`                                                                     |
| **Deadline** | Before 10 November 2026, when .NET 9 reaches end of support                                             |
| **Doc set**  | **PRD (this)** → SDD `_tasks/05-dotnet-10-upgrade-spec.md` → plan `_tasks/05-dotnet-10-upgrade-plan.md` |
| **Note**     | `_tasks/` is gitignored, so those two docs exist only in my local checkout and are not linked.          |

Status legend (used in all three docs): `Todo` · `In progress` · `Done` · `Blocked` · `Dropped` · `Deferred`.

## 1. Problem

.NET 9 reaches end of support on **10 November 2026** ([Microsoft's announcement](https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/)).
After that date the runtime and the `mcr.microsoft.com/dotnet` images get no security fixes. This repo exists to show a
production-shaped service, and a service on an unsupported runtime is not one. .NET 10 is the current LTS, supported
until November 2028.

## 2. Goals

1. The solution targets `net10.0` and builds, tests and ships as containers on .NET 10.
2. **No behaviour changes.** v1 and v2 answer every request exactly as they do today (NFR-1).
3. The upgrade is small and reversible: framework, SDK pin, images and package versions, then docs.
4. The change lands before 10 November 2026 (NFR-5).

## 3. Non-goals

- New .NET 10 or C# 14 features, and refactors made on the way.
- Moving Swagger to the built-in OpenAPI generator, or to Swashbuckle 10 (it needs Microsoft.OpenApi 2.x, whose types
  differ from the ones the repo uses). Both are follow-ups (§9).
- A routine bump of every outdated package. Only packages the framework change needs move.
- Changes to the React app, the database schema or any API contract.

## 4. Who it affects

| Who                    | Needs                                                                                     |
|------------------------|-------------------------------------------------------------------------------------------|
| Reviewer of the repo   | A supported runtime, and docs and badges that name the real target                        |
| Me, running the stack  | `bash scripts/stack-up.sh` and `dotnet test` work on a machine with only the .NET 10 SDK  |
| The deploy pipeline    | The same workflows build, test, migrate and deploy images that carry the .NET 10 runtime  |

## 5. Decisions (my calls — please confirm in review)

| Id   | Decision                                                                                                                                                                                                                                                                                                                                                                                                                       |
|------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| D1   | **Remove the unused package first, then bump the toolchain and the Microsoft packages together, then fix what the new SDK reports, then docs.** The old `9.0.20` packages cannot restore on `net10.0` (`Npgsql` 10 needs `Microsoft.Extensions.*` 10), so the framework and those packages are one step.                                                                                                                       |
| D2   | **Keep Swashbuckle 9.0.6 and remove the unused `Microsoft.AspNetCore.OpenApi`.** Nothing calls `AddOpenApi` or `MapOpenApi`, and the package would pull Microsoft.OpenApi 2.x into a build that Swashbuckle 9 keeps on 1.x.                                                                                                                                                                                                    |
| D3   | **Microsoft packages move to one version**, the newest 10.0 patch when the change is made (`10.0.13` on 2026-10-23).                                                                                                                                                                                                                                                                                                           |
| D4   | **"No behaviour change" is tested, not assumed.** The existing tests, the v1/v2 parity and cache suites and the Swagger tests pass unchanged. An assertion changes only with a written reason.                                                                                                                                                                                                                                 |
| D5   | **The live stack is part of done.** Tests use `WebApplicationFactory`, not the Docker image, so the rebuilt images are run and exercised by hand.                                                                                                                                                                                                                                                                              |
| D6   | **`Asp.Versioning` stays on 8.1.1 unless the spike or the package bump shows a problem.** The matching 10.2.1 is the fallback.                                                                                                                                                                                                                                                                                                 |
| D7   | **Other packages wait.** `Refit`, `NSubstitute` and the patch and minor updates are left for a routine dependency update.                                                                                                                                                                                                                                                                                                      |
| D8   | **A vulnerable transitive package is pinned, not suppressed.** The .NET 10 SDK audits transitive packages and warns about `System.Security.Cryptography.Pkcs` 9.0.0 (pulled in by `FluentMigrator.Runner` through `Microsoft.Data.SqlClient`). `CentralPackageTransitivePinningEnabled` plus one `PackageVersion` entry in `Directory.Packages.props` fixes it. Revisit when FluentMigrator updates its SQL Server dependency. |

## 6. Functional requirements

| Id     | Requirement                | Acceptance                                                                                                                                                                                                   | Status |
|--------|----------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|--------|
| FR-1   | Toolchain                  | `Directory.Build.props` says `net10.0`, `global.json` pins SDK `10.0.100` with `latestFeature`, and both Dockerfiles use `ARG DOTNET_VERSION=10.0`. They change together, and `DotNetVersionTests` passes    | Done   |
| FR-2   | Microsoft packages         | The nine `9.0.20` Microsoft packages in `Directory.Packages.props` are on one 10.0 patch version (D3), in the same step as FR-1 (D1)                                                                         | Done   |
| FR-3   | Remove an unused package   | `Microsoft.AspNetCore.OpenApi` is gone from `Movies.Api`, `Identity.Api` and `Directory.Packages.props`, and the Swagger tests still pass                                                                    | Done   |
| FR-4   | Third-party packages       | Only a package the framework change needs is moved (D6, D7). `Swashbuckle.AspNetCore` stays on 9.0.6 and design decisions say why and when to revisit                                                        | Todo   |
| FR-5   | Images and stack           | `bash scripts/stack-up.sh` builds both images on `10.0`. Both APIs report healthy, Swagger offers 1.0 and 2.0, a migration run succeeds, and the same request to v1 and v2 returns matching bodies           | Done   |
| FR-6   | CI and SonarCloud          | The workflows resolve the .NET 10 SDK from `global.json`, the SonarCloud scanner supports it, and the PR's checks are green                                                                                  | Todo   |
| FR-7   | Docs                       | No file names .NET 9 as the current target (history excepted): `README.md` (badge and text), `CLAUDE.md`, `_docs/_getting-started.md` and any other doc found by search. The README roadmap item is removed  | Todo   |
| FR-8   | Obsolete API               | `ForwardedHeadersOptions.KnownNetworks` (obsolete in ASP.NET Core 10, `ASPDEPR005`) becomes `KnownIPNetworks`, with the same behaviour: the allow-list is still cleared                                      | Done   |
| FR-9   | Vulnerability warnings     | The build has no `NU1902` or `NU1903` warning. `System.Security.Cryptography.Pkcs` is pinned centrally (D8) and `dotnet list package --vulnerable --include-transitive` is empty                             | Done   |

## 7. Non-functional requirements

| Id      | Requirement                                                                                                                                                                                                         | Status |
|---------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|--------|
| NFR-1   | **No behaviour change.** `dotnet test backend/TeweDiary.sln` passes with the same tests as on .NET 9 (3 Identity, 58 Application, 137 Api). Contracts, error bodies and links are identical                         | Todo   |
| NFR-2   | **No new moving parts.** No migration, no `appsettings.json` key, no v1 controller change, no React change, no new package reference (a transitive version pin is not one)                                          | Todo   |
| NFR-3   | **Warnings do not rise.** The baseline is one warning (`CS8625` in `BadRequestExceptionHandlerTests`). The count on .NET 10 is no higher, and none is suppressed to get there                                       | Todo   |
| NFR-4   | **No vulnerable packages.** The baseline lists `System.Security.Cryptography.Pkcs` 9.0.0 and two old packages in `Identity.Api`. After the upgrade `dotnet list package --vulnerable --include-transitive` is empty | Todo   |
| NFR-5   | **On time.** The change is merged before 10 November 2026                                                                                                                                                           | Todo   |
| NFR-6   | **Repo rules hold.** The target framework is only in `backend/Directory.Build.props`, package versions only in `backend/Directory.Packages.props`, and the dependency rule is untouched                             | Todo   |
| NFR-7   | **Reversible.** The change is one revert. The database, the contracts and the React app are untouched, so nothing needs migrating back                                                                              | Todo   |

## 8. Risks and known limitations

| Risk                                                                                                  | Likelihood     | Mitigation                                                                                                         |
|-------------------------------------------------------------------------------------------------------|----------------|--------------------------------------------------------------------------------------------------------------------|
| Swashbuckle 9 and a transitive Microsoft.OpenApi version disagree on .NET 10                          | Low (checked)  | The spike resolved Microsoft.OpenApi 1.6.30 and built cleanly with `Microsoft.AspNetCore.OpenApi` removed (D2)     |
| `Asp.Versioning` 8.1.1 misbehaves on .NET 10 (versioned Minimal API, a Swagger document per version)  | Low (checked)  | The spike passed all 198 tests on 8.1.1. D6 stays the fallback                                                     |
| A framework change alters an edge case (binding, JSON, output cache, auth)                            | Low            | Parity and error-contract tests, the live stack check, and a read of Microsoft's .NET 10 breaking-changes list     |
| The SonarCloud scanner or coverage step does not support the new SDK                                  | Medium         | Check the scanner version up front; the PR's SonarCloud run is the proof                                           |
| The `10.0` base images differ (user, paths, culture)                                                  | Low (checked)  | Ubuntu 24.04 (not Debian), same non-root `app` user, `curl` installs with `apt`. Still run the stack               |
| New analyser warnings from the 10 SDK                                                                 | Low            | Compare warning counts before and after; fix real ones                                                             |
| Azure                                                                                                 | None           | The apps run as containers, so the image carries the runtime and the App Service stack setting does not apply      |

## 9. Follow-ups (not this release)

- Swashbuckle 10 with Microsoft.OpenApi 2.x.
- A routine patch and minor update of the remaining packages, and the `Refit` and `NSubstitute` majors.
- Anything .NET 10 adds that this repo would benefit from.

## 10. Success metrics

- `dotnet build` is green with `net10.0` and `dotnet test` shows 3, 58 and 137 passing.
- `bash scripts/stack-up.sh` builds on `10.0`, both APIs are healthy and the v1/v2 comparison matches.
- The PR's checks are green, SonarCloud included.
- A search for ".NET 9" in the current-state docs finds nothing stale.

## 11. Traceability and status (master tracker)

The plan's task statuses roll up here. Update this table whenever a plan task changes status.

| Req     | SDD section  | Plan task(s)  | Status  |
|---------|--------------|---------------|---------|
| FR-1    | 5            | T3            | Done    |
| FR-2    | 5            | T3            | Done    |
| FR-3    | 5            | T2            | Done    |
| FR-4    | 6            | T5            | Todo    |
| FR-5    | 7            | T6            | Done    |
| FR-6    | 5            | T7            | Todo    |
| FR-7    | 5            | T8            | Todo    |
| FR-8    | 5            | T4            | Done    |
| FR-9    | 6            | T4            | Done    |
| NFR-1   | 7            | T1, T9        | Todo    |
| NFR-2   | 3            | T9            | Todo    |
| NFR-3   | 7            | T1, T9        | Todo    |
| NFR-4   | 7            | T1, T9        | Todo    |
| NFR-5   | —            | T9            | Todo    |
| NFR-6   | 3            | T3, T4        | Todo    |
| NFR-7   | 8            | T9            | Todo    |

## 12. Change log

| Date       | Change                                                                                                                              |
|------------|-------------------------------------------------------------------------------------------------------------------------------------|
| 2026-10-23 | First draft: decisions D1 to D7, FR-1 to FR-7, NFR-1 to NFR-7                                                                       |
| 2026-10-23 | After the spike: D1 reordered, D8 (pin Pkcs), FR-8 (`KnownIPNetworks`), FR-9 (no vulnerability warnings), NFR-3 and NFR-4 baselines |
