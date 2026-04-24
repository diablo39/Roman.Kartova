# Defer Wolverine PostgreSQL persistence until an outbox is actually needed

**Date:** 2026-04-24
**Status:** Approved
**Scope:** Slice 2 (auth + multi-tenancy) preparation for integration-test stabilization
**Related ADRs:** ADR-0080 (Wolverine outbox, mandatory when needed), ADR-0085 (migrator owns DDL), ADR-0090 (tenant-scope role separation)

## Context

`src/Kartova.Api/Program.cs` currently registers Wolverine with
`PersistMessagesWithPostgresql(kartovaConnection, schemaName: "wolverine")`. At
Slice 2, **no production code publishes or consumes a Wolverine message** — a
codebase grep for `IMessageBus | PublishAsync | SendAsync | InvokeAsync`
returns zero hits outside an architecture test. Wolverine is effectively
acting as a pure in-process CQRS mediator, which is its default mode and
needs no database schema.

Despite having no producers, the registration still causes Wolverine to want
a `wolverine.*` schema in Postgres. That schema would be created lazily at
API startup under the `kartova_app` connection — which conflicts with:

- **ADR-0085:** DDL is the migrator's job, never the API's.
- **ADR-0090:** `kartova_app` is the tenant-scoped, RLS-enforced app role; it
  is not the owner of shared-infra tables.

The existing comment in `src/Kartova.Migrator/Program.cs:22-26` already
acknowledges this gap ("wolverine tables are created lazily by the API").
Integration-test flakiness around role ownership surfaced the debt.

## Decision

Remove `PersistMessagesWithPostgresql` from the API registration entirely.
Wolverine continues to run as an in-process CQRS mediator (its default); no
`wolverine.*` schema exists at any ownership level. ADR-0080 is **not**
revisited — Wolverine remains the mandatory outbox mechanism for the moment
a slice first publishes a domain event. At that point, the owning slice
picks between three schema-ownership approaches (preserved here for future
reference so the choice is informed, not rediscovered):

- **Option A — Host Wolverine inside `Kartova.Migrator` and use JasperFx
  `IStatefulResource` / `IMessageStore.Admin.MigrateAsync` to apply its
  schema under the `migrator` role.** Recommended when the option needs to
  be exercised. Pros: single DDL writer; `migrator` owns `wolverine.*`, so
  the existing default-privileges grant to `kartova_app` and
  `kartova_bypass_rls` applies automatically; survives Wolverine version
  bumps. Cons: migrator takes a Wolverine dependency; API-side auto-create
  must be explicitly disabled.
- **Option B — Generate Wolverine DDL once and embed as raw SQL in an EF
  migration.** Pros: a single migration mechanism. Cons: hand-maintained
  across every Wolverine upgrade that changes the schema; mixes generated
  and raw-SQL migrations in one history.
- **Option C — Call Wolverine's `NpgsqlMessageStore` directly from the
  migrator process, without hosting a full Wolverine runtime.** Pros:
  lightest footprint. Cons: depends on Wolverine internals that are not part
  of the stable public surface.

Volume-ceiling concern (outbox at scale) was raised and dismissed: Kartova
is an entity-bounded service-catalog workload with a realistic sustained
rate on the order of hundreds of events per second even at enterprise scale
— several orders of magnitude below the transactional-outbox ceiling on a
modest Postgres. If volume ever becomes the constraint, the escape hatch is
Debezium/CDC over the WAL, not a redesign of the write path. Direct-publish
after commit is not acceptable because MiFID II / GDPR audit requirements
(E-01.F-05) rule out the lost-message window.

## Changes

Three edits:

1. **`src/Kartova.Api/Program.cs`, lines 61-70** — remove
   `opts.PersistMessagesWithPostgresql(kartovaConnection, schemaName: "wolverine")`.
   Keep `builder.Host.UseWolverine(...)` and the
   `foreach (var module in modules) { module.ConfigureWolverine(opts); }`
   block (modules still register in-process handlers).
   Remove the unused `using Wolverine.Postgresql;` import.
   Replace the surrounding comment with: *"Wolverine — in-process CQRS
   mediator only. Postgres persistence (outbox) deferred until a slice
   publishes domain events; see ADR-0080 and
   `docs/superpowers/specs/2026-04-24-defer-wolverine-persistence-design.md`."*

2. **`src/Kartova.Migrator/Program.cs`, lines 22-26** — rewrite the stale
   comment. New text states: `Kartova.Migrator` is the sole DDL owner per
   ADR-0085; when a future slice enables Wolverine persistence, the
   `wolverine.*` schema will be added here under the `migrator` role, and
   API-side auto-create will be disabled at the same time.

3. **Architecture test — new fitness function** in
   `tests/Kartova.ArchitectureTests/`. Fails the build if a call to
   `PersistMessagesWithPostgresql` (or `PersistMessagesWithSqlServer`, for
   completeness) appears anywhere outside the `Kartova.Migrator` assembly.
   Assertion strategy: use NetArchTest to assert that types in
   non-migrator assemblies do not reference the relevant Wolverine type,
   or — if NetArchTest cannot reach method-call granularity — a Roslyn /
   source-text scan over `src/**/*.cs` excluding `Kartova.Migrator`.
   Guard-rail exists so "someone adds it back without reading the spec"
   shows up as a red CI build, not a convention lapse in review.

**Not changed:**

- `src/Modules/Organization/Kartova.Organization.IntegrationTests/KartovaApiFixture.cs`
  — the `InitRolesAndSchemaAsync` SQL (creating `migrator`, `kartova_app`,
  `kartova_bypass_rls`) stays as-is. It is already correct for the day
  Wolverine persistence returns.
- `BypassConnectionString` / `MigratorConnectionString` plumbing — stays.
- Any other module, DbContext, or test.

## Definition of Done

Per `CLAUDE.md`'s five-gate rule. "Complete" requires all of the following
green with command + output cited:

1. **Build:** `dotnet build Kartova.slnx` with `TreatWarningsAsErrors=true`
   — 0 warnings, 0 errors.
2. **Architecture tests:** new fitness function green; the full
   `Kartova.ArchitectureTests` suite green.
3. **Unit tests:** full unit suite green.
4. **Integration tests:** `Kartova.Organization.IntegrationTests` suite runs
   end-to-end against Testcontainers Postgres and passes (or surfaces a
   separate, pre-existing failure unrelated to Wolverine persistence —
   which is the hand-off point to the next debugging slice).
5. **Docker-compose smoke:** one `docker compose up`, one happy-path
   `GET /api/v1/organizations/me` with a valid JWT (200), one negative-path
   (401 or 403) to confirm auth still rejects. Output captured in the
   plan's task log.

Until all five are green and cited, the honest status is *"implementation
staged, verification pending"* — not *"complete"*.

## Explicitly out of scope

- **Integration-test debugging.** Any test failures that persist after this
  change (e.g., in `TenantIsolationTests` or `AdminBypassTests`) are the
  subject of a separate follow-up slice. Removing Wolverine persistence
  narrows the surface so the real cause is easier to isolate; it does not
  claim to fix those failures.
- **The outbox ownership decision.** Options A / B / C are preserved in the
  "Decision" section for the future slice that first publishes a domain
  event. They are not chosen now.
- **ADR changes.** No new ADR; ADR-0080 and ADR-0085 stand unchanged. If
  the eventual outbox-enabling slice picks Option A, that slice may
  propose an ADR addendum documenting migrator-hosted Wolverine schema
  apply.
