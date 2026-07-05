---
name: project-stack-and-testing
description: bills-backend stack, current multi-project layout (Domain/Application/Data/Api), dependency-direction rule, and how to run unit vs integration tests
metadata:
  type: project
---

Stack: .NET 10 Minimal API, EF Core + Npgsql (Neon Postgres), Firebase JWT auth, NUnit for tests.

**Layout is a multi-project solution** (corrected 2026-07-05 — a prior version of this memory
described a single `src/BillsBackend.Api/` project with `Endpoints/Contracts/Domain/Data/Identity`
folders; that was split into separate projects during a July 2026 layered-architecture refactor).
Current structure under `src/`:
- `Domain/Entities/` — encapsulated entities (`AppUser`, `Bill`, `Category`, `BillEntry`,
  `IncomeEntry`, `Income`, `Person`), `Domain/Abstractions/Filters/ICurrentOwner`, enums.
- `Application/Abstractions/Services/` — service ports (`IUserProvisioningService`).
- `Application/Abstractions/Repositories/` — narrow repository ports consumed by Application
  services (`IAppUserRepository`, `ICategoryRepository`), added 2026-07-05. Keep these narrow
  (a handful of methods each) — do NOT add an interface that mirrors the whole `DbContext`.
- `Application/Services/` — `UserProvisioningService` (orchestration only, no EF Core).
- `Data/Contexts/AppDbContext.cs` — EF config, global per-owner query filters.
- `Data/Repositories/` — EF-backed implementations of the `Application` repository ports
  (`AppUserRepository`, `CategoryRepository`).
- `Api/Endpoints/`, `Api/Contracts/` — one file per feature's route group / DTOs.
- `Api/Migrations/` — EF Core migrations.

**Dependency direction (enforced via .csproj ProjectReferences): `Application` must never
reference `Data`.** `Data` references `Application` (it implements Application's repository
ports) and `Domain`. `Api` references both `Application` and `Data`. If a future Application
service needs persistence, add a narrow port under `Application/Abstractions/Repositories/` and
implement it in `Data/Repositories/` — don't reach for `AppDbContext` from `Application`.
Consequence: `Application.csproj` needs its own `Microsoft.Extensions.Logging.Abstractions`
package reference (previously came transitively through the `Data` reference).

Domain entities are encapsulated classes with `private set` properties, `Create`/`Provision`
factories, and behavior methods (`MarkPaid`, `Unfreeze`, `UpdateAmounts`, etc.) — good reference
for "don't build anemic models" pattern. `Category.Create` requires `ownerId > 0`
(`ArgumentOutOfRangeException.ThrowIfNegativeOrZero`) — relevant when mocking an
`IAppUserRepository` for seeding tests, see [[project-nunit-nsubstitute-gotchas]].

Enrichment pattern for reference-table lookups (bill -> category, bill -> person, etc.): entries
are fetched via the normal DbSet (global `owner_id` query filter applies), but the bill/category/
person templates they reference are fetched with `.IgnoreQueryFilters()` + manual
`.Where(x => x.OwnerId == appUser.Id)`, because the template may have been soft-deactivated since
the entry was created. See `GET /api/entries` and `GET /api/dashboard/month` for the pattern.

**Testing**: unit tests in `tests/BillsBackend.UnitTests` (NUnit, no DB, run in ~200ms, ~198 tests
as of 2026-07-05). `Microsoft.EntityFrameworkCore.InMemory` was removed from this project — no
more spinning up a real (if in-memory) `AppDbContext`; services that need persistence are tested
by mocking their narrow repository ports with **NSubstitute** (added 2026-07-05, first mocking lib
in this repo). Most tests still reconstruct the same LINQ/domain logic the handler runs, over real
domain objects built via `Create` factories, and assert on the result — no mocking needed there.
Integration tests in `tests/BillsBackend.IntegrationTests` use `IntegrationTestBase`
(WebApplicationFactory + real Postgres via Respawn, reset once per fixture) with a unique Firebase
uid per test method for isolation (`Uid(suffix)` helper), plain HTTP calls through
`Client.SendAsync`, and local `private sealed record` DTOs per test file for response
deserialization.

**Running tests**: `dotnet test tests/BillsBackend.UnitTests` is fast and self-contained. The full
`dotnet test` (or anything under `tests/BillsBackend.IntegrationTests`) needs a real Postgres —
see [[project-docker-sandbox-limitation]] for why that often can't run inside this sandbox.
