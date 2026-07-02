---
name: project-stack-and-testing
description: bills-backend stack, current Minimal API file layout (Endpoints/Contracts/Domain split), and how to run unit vs integration tests
metadata:
  type: project
---

Stack: .NET 10 Minimal API, EF Core + Npgsql (Neon Postgres), Firebase JWT auth, NUnit for tests.

**Layout is feature-organized, NOT single-file** (corrected 2026-07-02 — a prior version of this
memory claimed everything lived in one giant `Program.cs`; that was refactored away, see PR #43
"organizar minimal APIs em route groups por feature"). Current structure under
`src/BillsBackend.Api/`:
- `Endpoints/` — one file per feature's route group (`EntryEndpoints.cs`, `DashboardEndpoints.cs`,
  `BillEndpoints.cs`, `IncomeEndpoints.cs`, `CategoryEndpoints.cs`, `PersonEndpoints.cs`,
  `ProjectionEndpoints.cs`, `ReceivablesEndpoints.cs`, `UserEndpoints.cs`), each exposing a
  `Map<Feature>Endpoints(this RouteGroupBuilder group)` extension method.
- `Contracts/` — one file per feature holding the request/response DTOs (`EntryContracts.cs`,
  `DashboardContracts.cs`, etc.), matching the `Endpoints/` split 1:1.
- `Domain/` — entities (`Bill`, `Category`, `BillEntry`, `IncomeEntry`, `Income`, `Person`) plus
  `EntryCalculations.cs` (pure functions).
- `Data/` — `AppDbContext` and EF config.
- `Identity/` — Firebase JWT / `IUserProvisioningService` / `ICurrentOwner`.
- `Migrations/` — EF Core migrations.

`Program.cs` itself is now just composition root (DI registration, `MapGroup` wiring, JSON config).
Every authenticated endpoint still repeats the same `firebaseUid`/`provisioning.GetOrCreateAsync`/
`currentOwner.Id` boilerplate inline at the top of its handler — this is intentional per the repo's
own CLAUDE.md, do not refactor into a shared helper.

Domain entities (`Bill`, `Category`, `BillEntry`, `IncomeEntry`, `Income`, `Person`) are encapsulated
classes with `private set` properties, `Create` factories, and behavior methods (`MarkPaid`,
`Unfreeze`, `UpdateAmounts`, etc.) — good reference for "don't build anemic models" pattern.
`EntryCalculations` (src/BillsBackend.Api/Domain/EntryCalculations.cs) holds the shared pure
functions: `EffectiveAmount`, `MyShare`, `Receivable`.

Enrichment pattern for reference-table lookups (bill -> category, bill -> person, etc.): entries
are fetched via the normal DbSet (global `owner_id` query filter applies), but the bill/category/
person templates they reference are fetched with `.IgnoreQueryFilters()` + manual
`.Where(x => x.OwnerId == appUser.Id)`, because the template may have been soft-deactivated since
the entry was created. See `GET /api/entries` and the newer `GET /api/dashboard/month` for the
pattern.

**Testing**: unit tests in `tests/BillsBackend.UnitTests` (NUnit, no DB, run in ~1s, ~148+ tests as
of 2026-07-01). They don't test the Minimal API handlers directly (no extraction into testable
classes) — they reconstruct the same LINQ the handler runs, over real domain objects built via
`Create` factories, and assert on the result. Integration tests in
`tests/BillsBackend.IntegrationTests` use `IntegrationTestBase` (WebApplicationFactory + real
Postgres via Respawn, reset once per fixture) with a unique Firebase uid per test method for
isolation (`Uid(suffix)` helper), plain HTTP calls through `Client.SendAsync`, and local
`private sealed record` DTOs per test file for response deserialization.

**Running tests**: `dotnet test tests/BillsBackend.UnitTests` is fast and self-contained. The full
`dotnet test` (or anything under `tests/BillsBackend.IntegrationTests`) needs a real Postgres —
see [[project-docker-sandbox-limitation]] for why that often can't run inside this sandbox.
