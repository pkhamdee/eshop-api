# eshop-api

eShop's back end: one .NET 10 solution building two images, **eshop-api** (`src/Eshop.Api`, the HTTP API) and
**eshop-sync-master** (`src/Eshop.SyncMaster`, the Enterprise System sync), sharing `src/Eshop.Domain` and
`src/Eshop.Data`. One of eShop's service repositories (ADR 0009). The system lives in `pkhamdee/modernapp`, cloned
beside this one as `../modernapp` (its `scripts/clone-all.sh` clones this one): the glossary, ADRs, the one issue
tracker, deploy manifests, the smoke test, the gates and releases.

## Commands (.NET 10; Docker for the tests)

- `dotnet test` (Testcontainers: PostgreSQL; SQL Server amd64 under Rosetta for the sync master's Seam-2 tests),
  `dotnet format --verify-no-changes` (lint, as CI runs it). Set the Docker and Testcontainers variables first
  (`docs/conventions.md`, "Local environment").
- Migrations: `dotnet tool restore && dotnet ef migrations add <Name> --project src/Eshop.Data`.
- Running locally (PostgreSQL, SQL Server, thirdparty-mock): `../modernapp/docs/setup.md`, "Developing".
- The whole system around it, built from your clones: `../modernapp/scripts/gates/temporary-copy.sh build`, then
  `up`.

## Where things are

- Domain terms: `../modernapp/GLOSSARY.md`; decisions: `../modernapp/docs/adr/`. Skills that mention `GLOSSARY.md`
  or `docs/adr/` mean those.
- Conventions for this code (domain rules, one folder per feature, packages, adding an endpoint, tests):
  `docs/conventions.md`.
- How a merge here reaches dev, QA and prod: `../modernapp/docs/git-workflow.md`, "Service repositories".

## Rules

- Issues are in `pkhamdee/modernapp`: reference them as `pkhamdee/modernapp#N` in commits and PRs.
- Every merge must work with the other services' `main` (the temporary copy checks it): a change across services
  ships expand-then-contract, never as two PRs that only work together.
- Before a step touching secrets, repo settings or a PR merge, read "Human-only steps" in
  `../modernapp/docs/agents/conventions.md`.
