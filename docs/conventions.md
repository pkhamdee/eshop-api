# Conventions for eshop-api

What the code does not tell you at a glance. Moved here from pkhamdee/modernapp's `docs/agents/conventions.md` when
eshop-api got its own repository (ADR 0009); the system-wide conventions (local environment, deploy, human-only
steps) are still there.

## Local environment

- **Docker**: point each command at Rancher Desktop's socket, e.g. `DOCKER_HOST=unix:///Users/pongsak/.rd/docker.sock`.
- **Testcontainers** on Rancher Desktop needs both `DOCKER_HOST` (above) and
  `TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/var/run/docker.sock` (the socket path inside the VM); without the
  override Ryuk fails to start (`../modernapp/docs/setup.md`, "Developing").
- **.NET 10** is at `~/.dotnet`, not on `PATH`: set `DOTNET_ROOT=$HOME/.dotnet` and prepend
  `$HOME/.dotnet:$HOME/.dotnet/tools` to `PATH`. If the shell guard rejects `export` or variables as command
  names, a tiny wrapper script with literal paths (`exec /Users/pongsak/.dotnet/dotnet "$@"`) works.

## The solution

Layout:

- `Eshop.Domain` holds the rules and no infrastructure: one folder per area. Reuse the domain rules
  (`Catalog/InstallmentPlan`, `Applications/Eligibility`, `Customers/SmsCode`) instead of re-implementing them.
- `Eshop.Data`: `EshopDbContext`, one `Configurations/<X>Configuration.cs` per entity, snake_case names via
  EFCore.NamingConventions, `Seed/DemoData.cs`.
- `Eshop.Api`: one folder per feature with `<X>Endpoints.cs` exposing `Map<X>Endpoints(this IEndpointRouteBuilder)`,
  wired on the `/api` group in `Program.cs`. Health probes `/healthz/live|ready` sit outside `/api` so the Gateway
  never exposes them.
- `Eshop.SyncMaster` (eshop-sync-master) lives in the same solution and references `Eshop.Data`; it never migrates.
- Package versions only in `Directory.Packages.props` (`<PackageReference>` without `Version`). Warnings are errors,
  including NuGet audit (`NU1903`): a vulnerable transitive package is fixed by pinning a `PackageVersion` plus a
  direct reference (see `System.Security.Cryptography.Pkcs`). Each project has a committed `packages.lock.json`
  (the dependency-scan gate reads it; CI restores with `--locked-mode`): run `dotnet restore` after any package
  change and commit the lock files.
- Gates (`../modernapp/scripts/gates/*.sh`, `../modernapp/docs/setup.md` "Running the gates locally"): a Semgrep
  ERROR that is a false positive goes in this repository's `sast-accepted.txt` with a reason; `nosemgrep` comments are ignored. The ZAP gate
  (`dast.sh`) scans every operation in the OpenAPI description, which eshop-api serves only with
  `OpenApi:Enabled` (set in the temporary copy alone); a High alert to accept goes in `../modernapp/scripts/gates/dast-rules.conf`
  under a `# reason:` line. Every response carries `X-Content-Type-Options: nosniff` (Program.cs). It scans again
  signed in as a Customer and as Staff (`dast-session.mjs`) and fails if either got no 2xx from its role's paths
  (`CUSTOMER_ONLY` / `STAFF_ONLY` in `../modernapp/scripts/gates/dast.sh`): a new Customer-only area outside those patterns needs adding there.

Adding an endpoint:

1. Test first in `tests/Eshop.Api.Tests/<Area>/<X>Tests.cs`: `public sealed class XTests(EshopApiFactory api)`,
   `api.CreateClient()` (or a signed-in client, below), `TestContext.Current.CancellationToken`. Assert on HTTP
   responses only; read JSON into private records.
2. Add `Map<X>Endpoints` (a `MapGroup`, private static handlers, `internal sealed record <X>Response` DTOs) and wire
   it in `Program.cs`. Routes and JSON use GLOSSARY names; enums serialise kebab-case.
3. Refusals go through `Refusal.cs` (`Refusal.BadRequest/Conflict/Of`, `Refusal.SmsCode`, extra fields as
   `("key", value)` tuples), giving ProblemDetails with an `error` code. Missing: `TypedResults.NotFound()`.
4. Roles: `group.RequireAuthorization(Roles.Customer|Roles.Staff)` (no token 401, wrong role 403). Current user:
   `user.RequiredCustomerId()`, `user.RequiredStaffUsername()`.
5. Staff endpoints go under `BackOffice/` as a `Map<X>Administration(this RouteGroupBuilder)` called from
   `BackOfficeEndpoints`, so they inherit the Staff policy; add each to the `TheoryData` in
   `BackOffice/BackOfficeAccessTests.cs`. Validation: `BackOfficeEndpoints.Errors()` +
   `errors.Require(ok, field, thaiMessage)` → `TypedResults.ValidationProblem`.

Rules the code follows:

- **Time** comes only from the injected `TimeProvider` (singleton in `Program.cs`). Timeouts too:
  `new CancellationTokenSource(delay, timeProvider)` so the fake clock fires them. Bangkok dates/times via
  `Eshop.Domain.Bangkok`.
- **First writer wins** (no-tie Bids, Lot edits, payment vs forfeit): one conditional `ExecuteUpdateAsync`
  (`WHERE ... AND <precondition>`) and act on rows affected = 0. A read-then-write loses races; the concurrency
  tests catch it.
- **Contract is owned by the Enterprise System** (ADR-0001): private setters; only sync-master changes status via
  the internal `RefreshFromEnterprise`. Handover data lives beside it (ADR-0004). Active Contracts in queries:
  `db.Contracts.Where(Contract.IsActive)`.
- **Offer status**: always read `StatusAt(now)`; the stored status is updated lazily. Same for `Auction`/`Lot`.
- Contract numbers come from `contract_number_counters` via `INSERT ... ON CONFLICT ... RETURNING` with
  `SqlQuery<int>(...).ToListAsync()`; composing it with `SingleAsync` makes EF wrap the INSERT in a subquery.
- Contract, outbox row and Handover are written in one explicit transaction (`OfferEndpoints.Accept`).

Test harness (Seam 1, `EshopApiFactory`):

- One `[assembly: AssemblyFixture]`: one Postgres container and one host for every test, started in
  `InitializeAsync` to avoid double seeding. All tests share one database: create unique data per test
  (`TestData.NewMobileNumber()`, `NewIdNumber()`, `NewPhoto()`), filter catalog-wide assertions to your own rows,
  never rely on row counts, and create Products through the back-office API rather than the DB.
- Signed-in clients: `api.CreateCustomerClientAsync()`, `api.CreateStaffClientAsync()`. Area helpers are extension
  methods beside the tests (`Applications/Applying.cs`, `Offers/Offering.cs`, `Handovers/HandingOver.cs`,
  `Auctions/Bidding.cs`, `Contracts/EnterpriseSync.cs` for simulating the sync pull). Look there before writing
  setup code.
- Fakes (`tests/.../Fakes/`, swapped with `services.Replace` in `ConfigureWebHost`): `api.Clock`
  (`FakeTimeProvider`, starts 2026-01-05T03:00Z), `api.Sms`, `api.Images`, `api.IdVerification`, `api.CreditCheck`.
  Scripted fakes throw for an unscripted ID number.
- **Moving the clock**: put the test in `[Collection(nameof(ClockMoving))]` (defined in `tests/.../ClockMoving.cs`
  with `DisableParallelization = true`) and only move forward. Keep the explicit name: a collection definition
  without it was seen to run in parallel silently. After advancing past a token lifetime (Customer 7 days, Staff
  12 h) sign in again. Tests that change shared settings (Flat Rate, Term) use
  `[Collection(nameof(ProductCategorySettings))]` and restore in `finally`.
- Filter tests with `dotnet test <proj> --filter "FullyQualifiedName~X"` (vstest runner).

Third-party ports: interface + real adapter in `Eshop.Api/ThirdParty/` (typed `HttpClient`), registered in
`AddThirdParties` against `ThirdParty:BaseUrl`; an in-memory fake in `tests/.../Fakes/` exposed as a property on
`EshopApiFactory`. The image service port is `Images/IImageStore`; store only the image ID in Postgres.

Migrations: `dotnet ef migrations add <Name> --project src/Eshop.Data` (`../modernapp/docs/setup.md`). Only eshop-api applies
them, at startup (`Database:MigrateOnStartup`). Demo data is seeded by `UseSeeding`/`UseAsyncSeeding` into empty
tables only (no `HasData`, so identity sequences stay right). On a model-snapshot conflict after a rebase, or after
a bad `migrations remove`: check out `origin/main`'s `EshopDbContextModelSnapshot.cs`, delete your migration files
and run `migrations add` again.

eshop-sync-master:

- Seam-2 tests (`tests/Eshop.SyncMaster.Tests`, `SyncMasterFactory`) run Postgres and an amd64 SQL Server; CI keeps
  them on x86 (`ubuntu-latest`). Locally SQL Server runs under Rosetta (~20 s start).
- A new pulled Enterprise table follows `Sync/ContractPull`: `row_ver` watermark per table in `sync_watermarks`,
  bounded by `MIN_ACTIVE_ROWVERSION()`, saved in the same transaction under `pg_advisory_xact_lock`. Without the
  lock, overlapping pullers (rolling update, parallel tests) hit duplicate keys.
- Enterprise tables have triggers, so `UPDATE ... OUTPUT` without `INTO` fails; use rows affected. Schema changes go
  in `tests/Eshop.SyncMaster.Tests/enterprise-schema.sql` (idempotent), the source of truth: the tests run it, and
  pkhamdee/modernapp deploys a copy (`deploy/thirdparty/mssql/`) that its CI keeps identical. Change it here first.
- SqlClient needs ICU: SyncMaster sets `InvariantGlobalization=false` and uses the `-chiseled-extra` image.
  eshop-api stays invariant. Npgsql connection strings include `GSS Encryption Mode=Disable` (libgssapi noise on
  chiseled images).

