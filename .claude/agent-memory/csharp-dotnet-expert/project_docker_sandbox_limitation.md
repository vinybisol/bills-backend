---
name: project-docker-sandbox-limitation
description: bills-backend integration-test Postgres reachability is inconsistent across sandbox sessions — always verify directly, don't assume from a prior session
metadata:
  type: project
---

Verified 2026-07-01 (session A): in this Claude Code sandbox, `docker compose up -d` (the repo's
documented way to get a local Postgres for integration tests, see `docker-compose.yml` and
CLAUDE.md) failed with "permission denied while trying to connect to the docker API at
unix:///var/run/docker.sock". The user was not in the `docker` group, `sudo` needed interactive
auth, no local `postgres`/`pg_ctl` fallback existed, and nothing was listening on `localhost:5432`.

Verified 2026-07-01 (session B, same day, different session): `docker compose ps` again failed
with the same socket permission error, and a naive `/dev/tcp/localhost/5432` shell check reported
"closed" — but `dotnet test` against `tests/BillsBackend.IntegrationTests` actually connected fine
and the full integration suite (174 tests) ran and passed. So Postgres was reachable at
`localhost:5432` this time despite both surface-level checks suggesting otherwise (likely a
Postgres instance left running from container/host state outside this session's visibility, or the
`/dev/tcp` shell redirection just doesn't work reliably here).

Verified 2026-07-02 (session C): Postgres was reachable again without any explicit `docker compose`
setup step — `dotnet test tests/BillsBackend.IntegrationTests --filter FullyQualifiedName~X`
connected immediately and the full suite (183 tests after issue #42's three-balances feature) ran
green. Consistent with sessions A/B: don't probe, just try `dotnet test` directly.

**Why it matters**: reachability is NOT a stable property of this sandbox — it varies session to
session and shell-level connectivity probes (`/dev/tcp`, `docker compose ps`) are unreliable
signals. The only trustworthy check is asking `dotnet test` itself to try.

**How to apply**: when asked to implement a feature end-to-end in this repo, always write both unit
and integration tests per the repo's mandatory-testing rule. Don't assume from memory that
Postgres is unreachable — always attempt
`dotnet test tests/BillsBackend.IntegrationTests --filter FullyQualifiedName~<NewFixture>` (short
timeout, e.g. 60-90s) for real evidence before reporting a blocker. If it actually times out /
fails at `OneTimeSetUp` with an Npgsql connection-refused error, then report that specific error
as the blocker, run `dotnet test tests/BillsBackend.UnitTests` plus `dotnet build` on the whole
solution as the fallback verification, and say so explicitly in the final report rather than
silently skipping or claiming a full green run. This matches [[project-stack-and-testing]]'s test
layout. Don't attempt to route around a real blocker with `sg`, `newgrp`, or interactive `sudo`.
