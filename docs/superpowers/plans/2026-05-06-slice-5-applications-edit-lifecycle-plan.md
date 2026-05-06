# Slice 5 — Applications Edit + Lifecycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Land the project's first edit endpoint and first lifecycle-transition endpoints — `PUT /applications/{id}`, `POST /{id}/deprecate`, `POST /{id}/decommission` — end-to-end through the Catalog module on the existing `Application` aggregate, with optimistic concurrency (Postgres `xmin` + `If-Match`/`ETag` + 412), ADR-0073 lifecycle invariants (Active→Deprecated→Decommissioned, sunset-date strict), and the matching SPA edit modal + state-aware lifecycle menu. Co-ships **ADR-0096 — REST verb policy** (PUT for full replacement, POST for actions, no PATCH) bundled in the same PR.

**Architecture:** Adds `Lifecycle` enum, `SunsetDate?`, `Version` (xmin shadow) to `Application`; new domain methods `EditMetadata`/`Deprecate`/`Decommission` enforce ADR-0073 invariants and throw a new `InvalidLifecycleTransitionException`. Three new commands + handlers + endpoints follow slice-3's direct-dispatch pattern (ADR-0093). Three new `IExceptionHandler`s (`LifecycleConflict`, `ConcurrencyConflict`, `PreconditionRequired`) project domain/EF exceptions to RFC 7807. A new `IfMatchEndpointFilter` parses the request header. SPA gains `EditApplicationDialog`, `LifecycleMenu`, two confirmation dialogs, a `LifecycleBadge`, and three TanStack Query mutation hooks. Audit logging, notifications, admin override on backward transitions, and successor reference are honestly deferred — captured as backlog with concrete triggers in the spec §13.

**Tech Stack:** .NET 10, ASP.NET Core 10 minimal API, EF Core 10 + Npgsql 10 (Postgres `xmin` system column as concurrency token), Wolverine (discovery only — direct dispatch per ADR-0093), NetArchTest 1.3, Testcontainers 4, xUnit, FluentAssertions, `Microsoft.Extensions.TimeProvider.Testing` (FakeTimeProvider), KeyCloak 26.1 unchanged, React 19 + Vite 6 + TS strict, react-hook-form + zod, TanStack Query, Untitled UI (react-aria-components + Tailwind v4), Vitest, Playwright MCP for manual verification.

**Spec:** `docs/superpowers/specs/2026-05-06-slice-5-applications-edit-lifecycle-design.md` (commit `c8b92d9`)
**Co-shipped ADR:** `ADR-0096` — REST verb policy (PUT for replacement, POST for actions, no PATCH). Authored in Task 2 of this plan.
**Closes:** E-02.F-01.S-03 (edit metadata) + E-02.F-01.S-04 (lifecycle status transitions). Ticks the stale checklist rows for E-02.F-01.S-06, E-02.F-01.S-07, E-01.F-01.S-04 in Task 22.

---

## Pre-flight

Before starting Task 1, verify the branch state.

- [ ] **Working tree clean.** Run:

```bash
git status --short
```

Expected: empty (or only unrelated cache files). If you see staged or modified spec/plan files, commit or stash them.

- [ ] **On `master`.** Run:

```bash
git rev-parse --abbrev-ref HEAD
```

Expected: `master`. The slice-5 spec was committed to master directly (`c8b92d9`); the plan commits to master next, then we cut the feature branch.

- [ ] **Spec on master.** Run:

```bash
git log --oneline c8b92d9 -1
```

Expected: `c8b92d9 docs(spec): slice 5 — Applications edit + lifecycle transitions`. If `c8b92d9` doesn't resolve, **stop** — the spec hasn't merged.

- [ ] **Build green from start.** Run:

```bash
cmd //c "dotnet build Kartova.slnx -c Debug --nologo -v minimal"
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. If not, **stop** — fix on master before starting this PR.

- [ ] **Unit + arch tests green.** Run:

```bash
cmd //c "dotnet test Kartova.slnx --no-build --filter \"FullyQualifiedName!~IntegrationTests\" --nologo -v minimal"
```

Expected: all unit + architecture tests pass. Integration tests require Docker; defer to Task 12+.

- [ ] **Frontend baseline green.** Run:

```bash
cd web && npm run typecheck && npm run lint && npm run test --run && cd ..
```

Expected: TypeScript clean, ESLint clean, Vitest passes. If a test fails, **stop** — fix on master.

- [ ] **Cut the feature branch.** Run:

```bash
git checkout -b feat/slice-5-applications-edit-lifecycle
```

- [ ] **Commit the plan to the new branch.** Run:

```bash
git add docs/superpowers/plans/2026-05-06-slice-5-applications-edit-lifecycle-plan.md
git commit -m "$(cat <<'EOF'
docs(plan): slice 5 — Applications edit + lifecycle implementation plan

22-task plan for E-02.F-01.S-03 (edit metadata) + E-02.F-01.S-04
(lifecycle transitions) end-to-end. Co-ships ADR-0096 (REST verb policy
— PUT/POST only, no PATCH) in the same PR. Pins optimistic-concurrency
contract via Postgres xmin + If-Match + 412.

Spec reference: c8b92d9 (docs/superpowers/specs/2026-05-06-slice-5-applications-edit-lifecycle-design.md).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 1: ADR-0096 — REST verb policy

**Goal:** Author and index the architecture decision that this slice instantiates. Doc-only — no code change.

**Files:**
- Create: `docs/architecture/decisions/ADR-0096-rest-verb-policy.md`
- Modify: `docs/architecture/decisions/README.md` (add ADR-0096 to keyword index)

- [ ] **Step 1: Create the ADR.**

```markdown
# ADR-0096: REST Verb Policy — PUT for Full Replacement, POST for Actions, No PATCH

**Status:** Accepted
**Date:** 2026-05-06
**Deciders:** Roman Głogowski (solo developer)
**Category:** API & Integration Architecture
**Related:** ADR-0029 (REST as primary API style), ADR-0091 (RFC 7807 error responses), ADR-0092 (REST URL convention), ADR-0073 (entity lifecycle states), ADR-0095 (cursor pagination contract).

## Context

The Catalog `Application` aggregate is the first entity to acquire an edit endpoint and lifecycle-transition endpoints (slice 5). The HTTP verb landscape gives us three options for "change something on this resource": `PUT`, `PATCH`, and named-action `POST`. ADR-0029 picked REST as the API style without freezing verb usage. The first edit slice forces the question — and whatever pattern lands here will be copy-pasted across roughly twenty edit endpoints over the rest of the catalog (Component, Service, API, Infrastructure, Broker, Environment, Deployment).

`PATCH` is attractive in theory: sparse update, send only what changes. In practice three things degrade it:

1. **Semantics drift.** RFC 7396 (JSON Merge Patch), RFC 6902 (JSON Patch), and ad-hoc "send the diff" all coexist as production patterns. Within one team it's fine; across the API surface — including future CLI / agent / webhook consumers — the format choice multiplies bug surface.
2. **Missing-vs-null ambiguity.** `{ "displayName": null }` reads as "clear the field" in JSON Merge Patch and "no change" in absent-key semantics. Codegen typings have to model this distinction (often as `T | null | undefined`) and downstream client code has to handle it. The fields where this matters (nullable foreign keys, optional metadata) are exactly the ones where bugs hide longest.
3. **Codegen support is uneven.** OpenAPI generators handle `PUT` body schemas trivially; `PATCH` schemas vary by tool and require explicit signaling that a property's absence is meaningful.

A CQRS-shaped service has a clean alternative: **named action endpoints**. "Change one thing" becomes "invoke the command that changes that thing" — `POST /applications/{id}/deprecate`, `POST /applications/{id}/restore`, `POST /applications/{id}/transfer-ownership`. Each maps 1:1 to a domain method, gets its own per-route authorization policy, and reads in OpenAPI like the ubiquitous language. Sparse updates are expressed as commands, not as wire-format gymnastics.

That leaves `PUT` for full-resource replacement on stable, small DTOs (e.g., the `EditApplicationRequest { displayName, description }` two-field shape).

## Decision

1. **`PUT /resources/{id}`** is used for idempotent full-resource replacement when the editable surface of the resource is small and stable. The request body carries every editable field; missing fields are an error, not "no change." Concurrency is enforced via `If-Match` / `ETag` (slice 5 §6 of the spec).

2. **`POST /resources/{id}/<action>`** is used for domain commands — anything that maps to a named domain method (`deprecate`, `decommission`, `restore`, `transfer-ownership`, `regenerate-token`). Each action endpoint takes a precise request DTO (often empty), has its own authorization policy, and emits its own OpenAPI operation (`deprecateApplication`, `decommissionApplication`).

3. **`PATCH` is forbidden.** Code review and an architecture test reject any new `PATCH` endpoint.

4. **Bulk operations** (E-01.F-06.S-03 — separate epic) get their own collection-action endpoints (`POST /applications/bulk/deprecate`); they are not modeled as `PATCH` on the collection.

## Rationale

- One verb (`PUT`) for "replace this whole resource." Idempotent. Body is the resource. No semantic ambiguity.
- One verb (`POST`) for "invoke this domain command." Each command is its own endpoint, its own authorization rule, its own OpenAPI op. Reads like the ubiquitous language.
- Zero verbs (`PATCH`) where semantics drift. The "I only want to change displayName" case is solved by a small two-field `PUT` body or by a future named-action endpoint if the field has command-like semantics (e.g., `POST /applications/{id}/rename` if and when slug rename is supported).
- Per-route authorization is declarative — each route is one `[Authorize(Policy = ...)]` attribute. With a discriminated `PATCH` body or a single fat `POST /lifecycle { to: ... }`, authorization becomes "if body field is X, require admin" — imperative, easy to drift, hard to assert via arch test.

## Alternatives Considered

- **`PATCH` with JSON Merge Patch (RFC 7396).** Rejected for the missing-vs-null ambiguity and codegen drift outlined above.
- **Single `POST /resources/{id}/lifecycle` with `{ to: <state>, ... }` body.** Rejected because it forces a fat handler with a `switch (cmd.To)` + per-state validation branches. Per-action endpoints scale the same number of routes but keep handlers thin and per-route authorization declarative.
- **Verb-per-state in the URL but `PATCH`-style sparse body** (e.g., `PATCH /applications/{id}` with `{ lifecycle: "deprecated", sunsetDate: "..." }`). Rejected for collapsing two distinct authorization concerns (metadata edit vs lifecycle transition) into one route.

## Consequences

**Positive:**
- Clients (TypeScript SDK, future CLI, future agents) get one operation per command — names read like the domain.
- Per-route authorization stays declarative; future RBAC retrofit (E-01.F-04.S-03) is one attribute per admin endpoint, not a refactor of branching code.
- Audit logging (when E-01.F-03.S-03 lands) keys off the route name — the route IS the action.
- OpenAPI surface self-documents. No "what does `PATCH` do here" reading required.

**Negative / Trade-offs:**
- More routes per resource. A resource with three lifecycle transitions has three routes, not one.
- "Change one nullable field" cases that don't map to a named command (genuine sparse update) cannot be expressed at all under this policy. If such a case appears, the resolution is to either expand the `PUT` body (fields stay required, send the unchanged ones explicitly) or introduce a named action. Neither has been needed yet; this ADR is amended (not violated) if and when one does.

**Neutral:**
- The architecture test that pins absence of `PATCH` (Task 3 of slice 5) costs nothing to maintain.

## Implementation notes

- Slice 5 introduces three new endpoints under this policy: `PUT /api/v1/catalog/applications/{id}`, `POST /api/v1/catalog/applications/{id}/deprecate`, `POST /api/v1/catalog/applications/{id}/decommission`. Each is the reference exemplar.
- The architecture test `RestVerbPolicyRules.No_endpoint_uses_PATCH_verb` (Task 3) walks the `EndpointDataSource` after `WebApplicationFactory` boot and asserts no endpoint metadata declares a `PATCH` HTTP method.

## References

- ADR-0029 (REST as primary API style)
- ADR-0091 (RFC 7807 error responses)
- ADR-0092 (REST URL convention — module slug as URL segment)
- Slice 5 spec — `docs/superpowers/specs/2026-05-06-slice-5-applications-edit-lifecycle-design.md`
```

- [ ] **Step 2: Add ADR-0096 to the README index.**

Read `docs/architecture/decisions/README.md` and locate the table where ADR-0095 is indexed. Add a row for ADR-0096 in numeric order (just after ADR-0095). Use the same column format used by neighboring rows (typically `[ADR-NNNN](path) — Topic — keyword tags`).

If you can't find the exact format by reading the file, run:

```bash
grep -n "ADR-0095" docs/architecture/decisions/README.md
```

…and mirror the existing line's structure.

- [ ] **Step 3: Verify the ADR renders cleanly.**

```bash
cmd //c "dotnet build Kartova.slnx -c Debug --nologo -v minimal"
```

Expected: still green (no code change). The verify step is to confirm you didn't accidentally edit a `.cs` file.

- [ ] **Step 4: Commit.**

```bash
git add docs/architecture/decisions/ADR-0096-rest-verb-policy.md docs/architecture/decisions/README.md
git commit -m "$(cat <<'EOF'
docs(adr): ADR-0096 — REST verb policy (PUT/POST only, no PATCH)

First edit slice (slice 5) instantiates the policy. PATCH is rejected for
semantics drift, missing-vs-null ambiguity, and uneven codegen support;
named action endpoints (POST /resource/{id}/<action>) are the sparse-update
alternative for command-shaped state changes.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: `ProblemTypes` additions + `RestVerbPolicyRules` arch test

**Goal:** Add the three new RFC 7807 type slugs (concurrency-conflict, precondition-required, lifecycle-conflict) and pin ADR-0096 with an arch test that asserts no endpoint declares the PATCH verb. The arch test is GREEN immediately because no PATCH route exists today; it goes RED if anyone ever adds one.

**Files:**
- Modify: `src/Kartova.SharedKernel.AspNetCore/ProblemTypes.cs`
- Create: `tests/Kartova.ArchitectureTests/RestVerbPolicyRules.cs`

- [ ] **Step 1: Add new ProblemTypes constants.**

Open `src/Kartova.SharedKernel.AspNetCore/ProblemTypes.cs`. Append the three new slugs after the existing pagination block:

```csharp
    // Optimistic concurrency / preconditions — slice 5 (ADR-0096 + spec §7).
    public const string ConcurrencyConflict    = Base + "concurrency-conflict";
    public const string PreconditionRequired   = Base + "precondition-required";

    // Lifecycle transitions — ADR-0073, slice 5.
    public const string LifecycleConflict      = Base + "lifecycle-conflict";
```

The full updated file should preserve the existing constants (`InvalidToken`, `ResourceNotFound`, `ValidationFailed`, etc.) and add only the three above.

- [ ] **Step 2: Verify the additions compile.**

```bash
cmd //c "dotnet build src/Kartova.SharedKernel.AspNetCore --nologo -v minimal"
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Write the failing arch test.**

```csharp
// tests/Kartova.ArchitectureTests/RestVerbPolicyRules.cs
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Kartova.ArchitectureTests;

/// <summary>
/// Pins ADR-0096 — the API does not use the PATCH verb. PUT for full-resource
/// replacement, POST for named action endpoints. Sparse-update demand is met
/// by named actions, not PATCH semantics.
///
/// The test boots a <see cref="WebApplicationFactory{TEntryPoint}"/> against
/// the real Kartova.Api program, walks <see cref="EndpointDataSource"/>, and
/// asserts no endpoint metadata declares "PATCH" as an accepted HTTP method.
/// </summary>
public class RestVerbPolicyRules : IClassFixture<WebApplicationFactory<Kartova.Api.Program>>
{
    private readonly WebApplicationFactory<Kartova.Api.Program> _factory;

    public RestVerbPolicyRules(WebApplicationFactory<Kartova.Api.Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public void No_endpoint_uses_PATCH_verb()
    {
        // Boot the API host so EndpointDataSource is populated.
        using var scope = _factory.Services.CreateScope();
        var sources = scope.ServiceProvider.GetServices<EndpointDataSource>();

        var patchEndpoints = sources
            .SelectMany(s => s.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.IHttpMethodMetadata>()
                ?.HttpMethods.Contains("PATCH", StringComparer.OrdinalIgnoreCase) == true)
            .Select(e => e.RoutePattern.RawText)
            .ToList();

        patchEndpoints.Should().BeEmpty(
            because: "ADR-0096 forbids PATCH endpoints. Use PUT for full replacement or POST /<action> for named commands.");
    }
}
```

If the arch test project doesn't yet reference `Microsoft.AspNetCore.Mvc.Testing`, add it:

```bash
cmd //c "dotnet add tests/Kartova.ArchitectureTests package Microsoft.AspNetCore.Mvc.Testing"
```

If `Kartova.Api.Program` is not exposed (no `public partial class Program`), see how `EndpointRouteRules` (slice-3 §13.9 RESOLVED) does it — it uses the same fixture pattern. Mirror that test's setup.

- [ ] **Step 4: Run the test.**

```bash
cmd //c "dotnet test tests/Kartova.ArchitectureTests --filter \"FullyQualifiedName~RestVerbPolicyRules\" --nologo -v minimal"
```

Expected: PASS (no PATCH routes exist on master). If the test fails to compile, fix references; if it fails because PATCH was inadvertently introduced, **stop** — that contradicts master state.

- [ ] **Step 5: Commit.**

```bash
git add src/Kartova.SharedKernel.AspNetCore/ProblemTypes.cs tests/Kartova.ArchitectureTests/RestVerbPolicyRules.cs tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj
git commit -m "$(cat <<'EOF'
feat(api): add ProblemTypes + RestVerbPolicyRules arch test (ADR-0096)

- ProblemTypes: concurrency-conflict, precondition-required, lifecycle-conflict slugs
- RestVerbPolicyRules.No_endpoint_uses_PATCH_verb pins ADR-0096

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: `xmin` mapping spike — Testcontainer roundtrip proof

**Goal:** Validate the riskiest mapping in the slice before wiring endpoints. Postgres `xmin` is a system column, not a regular column, and EF Core's `IsRowVersion()` integration needs `HasColumnType("xid")` + an explicit `ValueGeneratedOnAddOrUpdate`. A small Testcontainer test proves the roundtrip and the `DbUpdateConcurrencyException` path before we lock the EF entity config in Task 7. If the spike fails, fall back to an explicit `version BIGINT` column with a trigger or app-side increment (spec §10 risks table).

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/XminConcurrencyTokenSpikeTests.cs`

- [ ] **Step 1: Write the spike test.**

```csharp
// src/Modules/Catalog/Kartova.Catalog.IntegrationTests/XminConcurrencyTokenSpikeTests.cs
using FluentAssertions;
using Kartova.Catalog.Domain;
using Kartova.Catalog.Infrastructure;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Testcontainers.PostgreSql;

namespace Kartova.Catalog.IntegrationTests;

/// <summary>
/// Spike — proves that EF Core can map the Postgres xmin system column as a
/// concurrency token (uint), and that a stale OriginalValue raises
/// DbUpdateConcurrencyException on SaveChangesAsync. If this passes, slice 5
/// adopts the same mapping in EfApplicationConfiguration (Task 7). If it fails,
/// fall back to an explicit `version BIGINT` column (spec §10 risks).
/// </summary>
public class XminConcurrencyTokenSpikeTests : IAsyncLifetime
{
    private PostgreSqlContainer _pg = null!;

    public async ValueTask InitializeAsync()
    {
        _pg = new PostgreSqlBuilder()
            .WithImage("postgres:18")
            .Build();
        await _pg.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _pg.DisposeAsync();
    }

    [Fact]
    public async Task Xmin_advances_on_update_and_raises_on_stale_OriginalValue()
    {
        var cs = _pg.GetConnectionString();

        // First DbContext: create the table and insert one row.
        var optionsA = new DbContextOptionsBuilder<SpikeDbContext>().UseNpgsql(cs).Options;
        await using (var db = new SpikeDbContext(optionsA))
        {
            await db.Database.EnsureCreatedAsync();
            db.Widgets.Add(new SpikeWidget { Id = Guid.NewGuid(), Name = "alpha" });
            await db.SaveChangesAsync();
        }

        // Second DbContext A: load + capture the version.
        await using var dbA = new SpikeDbContext(optionsA);
        var rowA = await dbA.Widgets.FirstAsync();
        var versionAtLoad = rowA.Version;

        // Third DbContext B (separate scope, simulates another client): load + update.
        var optionsB = new DbContextOptionsBuilder<SpikeDbContext>().UseNpgsql(cs).Options;
        await using (var dbB = new SpikeDbContext(optionsB))
        {
            var rowB = await dbB.Widgets.FirstAsync();
            rowB.Name = "beta";
            await dbB.SaveChangesAsync();
        }

        // Now A tries to update with the stale captured OriginalValue.
        rowA.Name = "gamma";
        dbA.Entry(rowA).Property(w => w.Version).OriginalValue = versionAtLoad;

        var act = async () => await dbA.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    private sealed class SpikeWidget
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public uint Version { get; set; }
    }

    private sealed class SpikeDbContext(DbContextOptions<SpikeDbContext> opts) : DbContext(opts)
    {
        public DbSet<SpikeWidget> Widgets => Set<SpikeWidget>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<SpikeWidget>(e =>
            {
                e.ToTable("spike_widgets");
                e.HasKey(x => x.Id);
                e.Property(x => x.Name).IsRequired();
                e.Property(x => x.Version)
                    .HasColumnName("xmin")
                    .HasColumnType("xid")
                    .ValueGeneratedOnAddOrUpdate()
                    .IsRowVersion()
                    .IsConcurrencyToken();
            });
        }
    }
}
```

- [ ] **Step 2: Verify Docker is available.**

```bash
docker info
```

Expected: Docker daemon responds. If "Cannot connect to the Docker daemon," **stop** — start Docker Desktop, then re-run.

- [ ] **Step 3: Run the spike test.**

```bash
cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter \"FullyQualifiedName~XminConcurrencyTokenSpikeTests\" --nologo -v normal"
```

Expected: PASS. If FAIL with `DbUpdateConcurrencyException` not thrown → the mapping doesn't work; **stop** and pivot to an explicit `version BIGINT` column. Open the spec, document the pivot at §10, and adjust Task 7 entity config accordingly. Otherwise proceed.

- [ ] **Step 4: Delete the spike test (it served its purpose; it's not part of the kept suite).**

```bash
rm src/Modules/Catalog/Kartova.Catalog.IntegrationTests/XminConcurrencyTokenSpikeTests.cs
```

The spike was a one-off verification. The real test surface for the mapping is `EditApplicationTests.PUT_with_stale_If_Match_returns_412` (Task 12).

- [ ] **Step 5: Commit (no kept files — git tracks the deletion-after-add as a no-op when the test was never committed).**

If you committed the spike before deletion (acceptable), commit the deletion separately:

```bash
git add -u src/Modules/Catalog/Kartova.Catalog.IntegrationTests/
git commit -m "$(cat <<'EOF'
chore(test): remove xmin mapping spike — proven; carrying real coverage in EditApplicationTests

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

If you didn't commit before deletion, no commit is needed for this task — proceed to Task 4.

---

## Task 4: `Lifecycle` enum + `InvalidLifecycleTransitionException` + arch tests

**Goal:** Introduce the lifecycle vocabulary into the domain layer. Two arch tests pin enum stability (3 explicit values, linear ordering) so future changes are deliberate.

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Domain/Lifecycle.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Domain/InvalidLifecycleTransitionException.cs`
- Create: `tests/Kartova.ArchitectureTests/LifecycleEnumRules.cs`

- [ ] **Step 1: Write the failing arch tests.**

```csharp
// tests/Kartova.ArchitectureTests/LifecycleEnumRules.cs
using FluentAssertions;
using Kartova.Catalog.Domain;

namespace Kartova.ArchitectureTests;

/// <summary>
/// Pins the Lifecycle enum's wire stability. The numeric values (1=Active,
/// 2=Deprecated, 3=Decommissioned) are load-bearing — comparison ops in
/// Application.Decommission rely on monotonic ordering. Inserting or
/// reordering members shifts every comparison and changes the wire shape.
/// These tests force a deliberate reckoning when changing the enum.
/// </summary>
public class LifecycleEnumRules
{
    [Fact]
    public void Lifecycle_has_exactly_three_members_with_explicit_values()
    {
        Enum.GetValues<Lifecycle>().Should().HaveCount(3);

        ((int)Lifecycle.Active).Should().Be(1);
        ((int)Lifecycle.Deprecated).Should().Be(2);
        ((int)Lifecycle.Decommissioned).Should().Be(3);
    }

    [Fact]
    public void Lifecycle_members_are_linearly_ordered()
    {
        ((int)Lifecycle.Active).Should().BeLessThan((int)Lifecycle.Deprecated);
        ((int)Lifecycle.Deprecated).Should().BeLessThan((int)Lifecycle.Decommissioned);
    }
}
```

- [ ] **Step 2: Run, observe RED (compile error — `Lifecycle` doesn't exist yet).**

```bash
cmd //c "dotnet test tests/Kartova.ArchitectureTests --filter \"FullyQualifiedName~LifecycleEnumRules\" --nologo -v minimal"
```

Expected: `CS0246: The type or namespace name 'Lifecycle' could not be found`.

- [ ] **Step 3: Create the enum.**

```csharp
// src/Modules/Catalog/Kartova.Catalog.Domain/Lifecycle.cs
namespace Kartova.Catalog.Domain;

/// <summary>
/// Application lifecycle states per ADR-0073. Linear forward progression
/// (Active → Deprecated → Decommissioned). Backward transitions require Org
/// Admin (deferred to RBAC slice — spec §13.2). Numeric values are
/// load-bearing — reordering breaks Application.Decommission's monotonic
/// comparisons. Pinned by LifecycleEnumRules arch tests.
/// </summary>
public enum Lifecycle
{
    Active = 1,
    Deprecated = 2,
    Decommissioned = 3,
}
```

- [ ] **Step 4: Create the domain exception.**

```csharp
// src/Modules/Catalog/Kartova.Catalog.Domain/InvalidLifecycleTransitionException.cs
namespace Kartova.Catalog.Domain;

/// <summary>
/// Thrown by Application.EditMetadata / Deprecate / Decommission when an
/// ADR-0073 lifecycle invariant is violated (transition not allowed from the
/// current state, or "decommission before sunset_date" without admin override).
/// Mapped to RFC 7807 409 Conflict by LifecycleConflictExceptionHandler.
/// </summary>
public sealed class InvalidLifecycleTransitionException : InvalidOperationException
{
    public Lifecycle CurrentLifecycle { get; }
    public string AttemptedTransition { get; }
    public DateTimeOffset? SunsetDate { get; }
    public string? Reason { get; }

    public InvalidLifecycleTransitionException(
        Lifecycle current,
        string attempted,
        DateTimeOffset? sunsetDate = null,
        string? reason = null)
        : base(BuildMessage(current, attempted, reason))
    {
        CurrentLifecycle = current;
        AttemptedTransition = attempted;
        SunsetDate = sunsetDate;
        Reason = reason;
    }

    private static string BuildMessage(Lifecycle current, string attempted, string? reason)
        => reason is null
            ? $"Cannot {attempted.ToLowerInvariant()} application currently in state {current}."
            : $"Cannot {attempted.ToLowerInvariant()} application currently in state {current} ({reason}).";
}
```

- [ ] **Step 5: Run the arch tests.**

```bash
cmd //c "dotnet test tests/Kartova.ArchitectureTests --filter \"FullyQualifiedName~LifecycleEnumRules\" --nologo -v minimal"
```

Expected: PASS (2/2).

- [ ] **Step 6: Build the whole solution to make sure no other test was broken.**

```bash
cmd //c "dotnet build Kartova.slnx -c Debug --nologo -v minimal"
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 7: Commit.**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Domain/Lifecycle.cs src/Modules/Catalog/Kartova.Catalog.Domain/InvalidLifecycleTransitionException.cs tests/Kartova.ArchitectureTests/LifecycleEnumRules.cs
git commit -m "$(cat <<'EOF'
feat(catalog): Lifecycle enum + InvalidLifecycleTransitionException (ADR-0073)

- 3 explicit values: Active=1, Deprecated=2, Decommissioned=3
- LifecycleEnumRules arch tests pin enum stability + linear ordering
- InvalidLifecycleTransitionException carries current state, attempted
  transition, sunset_date, optional reason — projected to 409 by
  LifecycleConflictExceptionHandler (Task 9)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: `Application` aggregate — properties, methods, unit tests (TDD)

**Goal:** Extend the existing `Application` aggregate with `Lifecycle`, `SunsetDate?`, `Version` properties and the three new domain methods (`EditMetadata`, `Deprecate`, `Decommission`). Drive every method via tests RED first.

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Domain/Application.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Tests/ApplicationLifecycleTests.cs`

- [ ] **Step 1: Add `Microsoft.Extensions.TimeProvider.Testing` to the test project.**

```bash
cmd //c "dotnet add src/Modules/Catalog/Kartova.Catalog.Tests package Microsoft.Extensions.TimeProvider.Testing"
```

Expected: `info : PackageReference for package 'Microsoft.Extensions.TimeProvider.Testing' added`.

- [ ] **Step 2: Write all 16 failing tests at once.**

```csharp
// src/Modules/Catalog/Kartova.Catalog.Tests/ApplicationLifecycleTests.cs
using FluentAssertions;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.Extensions.Time.Testing;

namespace Kartova.Catalog.Tests;

public class ApplicationLifecycleTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"));
    private static readonly Guid Owner = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = new(2026, 5, 6, 12, 0, 0, TimeSpan.Zero);

    private static FakeTimeProvider Clock(DateTimeOffset? now = null)
    {
        var c = new FakeTimeProvider();
        c.SetUtcNow(now ?? Now);
        return c;
    }

    private static Application NewActive() =>
        Application.Create("payments-api", "Payments API", "Description.", Owner, Tenant);

    [Fact]
    public void New_application_starts_in_Active_state_with_null_sunsetDate()
    {
        var app = NewActive();
        app.Lifecycle.Should().Be(Lifecycle.Active);
        app.SunsetDate.Should().BeNull();
    }

    [Fact]
    public void EditMetadata_with_valid_args_updates_displayName_and_description()
    {
        var app = NewActive();
        app.EditMetadata("New Display", "New description.");
        app.DisplayName.Should().Be("New Display");
        app.Description.Should().Be("New description.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EditMetadata_throws_on_empty_displayName(string displayName)
    {
        var app = NewActive();
        var act = () => app.EditMetadata(displayName, "desc");
        act.Should().Throw<ArgumentException>().WithMessage("*display name*");
    }

    [Fact]
    public void EditMetadata_throws_on_displayName_over_128()
    {
        var app = NewActive();
        var tooLong = new string('x', 129);
        var act = () => app.EditMetadata(tooLong, "desc");
        act.Should().Throw<ArgumentException>().WithMessage("*128*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EditMetadata_throws_on_empty_description(string description)
    {
        var app = NewActive();
        var act = () => app.EditMetadata("Display", description);
        act.Should().Throw<ArgumentException>().WithMessage("*description*");
    }

    [Fact]
    public void EditMetadata_does_not_change_Name_or_OwnerUserId_or_TenantId_or_CreatedAt()
    {
        var app = NewActive();
        var origName = app.Name;
        var origOwner = app.OwnerUserId;
        var origTenant = app.TenantId;
        var origCreated = app.CreatedAt;

        app.EditMetadata("Different", "Different.");

        app.Name.Should().Be(origName);
        app.OwnerUserId.Should().Be(origOwner);
        app.TenantId.Should().Be(origTenant);
        app.CreatedAt.Should().Be(origCreated);
    }

    [Fact]
    public void EditMetadata_on_Decommissioned_throws_InvalidLifecycleTransitionException()
    {
        var app = NewActive();
        app.Deprecate(Now.AddDays(1), Clock());
        app.Decommission(Clock(Now.AddDays(2)));

        var act = () => app.EditMetadata("X", "Y");
        act.Should().Throw<InvalidLifecycleTransitionException>()
           .Which.CurrentLifecycle.Should().Be(Lifecycle.Decommissioned);
    }

    [Fact]
    public void Deprecate_with_valid_args_sets_state_and_sunsetDate()
    {
        var app = NewActive();
        var sunset = Now.AddDays(30);
        app.Deprecate(sunset, Clock());

        app.Lifecycle.Should().Be(Lifecycle.Deprecated);
        app.SunsetDate.Should().Be(sunset);
    }

    [Fact]
    public void Deprecate_throws_on_past_sunsetDate()
    {
        var app = NewActive();
        var act = () => app.Deprecate(Now.AddDays(-1), Clock());
        act.Should().Throw<ArgumentException>().WithMessage("*sunset*future*");
    }

    [Fact]
    public void Deprecate_throws_on_now_sunsetDate()
    {
        var app = NewActive();
        var act = () => app.Deprecate(Now, Clock());
        act.Should().Throw<ArgumentException>().WithMessage("*sunset*future*");
    }

    [Fact]
    public void Deprecate_when_already_Deprecated_throws_InvalidLifecycleTransitionException()
    {
        var app = NewActive();
        app.Deprecate(Now.AddDays(30), Clock());

        var act = () => app.Deprecate(Now.AddDays(60), Clock());
        act.Should().Throw<InvalidLifecycleTransitionException>()
           .Which.CurrentLifecycle.Should().Be(Lifecycle.Deprecated);
    }

    [Fact]
    public void Deprecate_when_Decommissioned_throws_InvalidLifecycleTransitionException()
    {
        var app = NewActive();
        app.Deprecate(Now.AddDays(1), Clock());
        app.Decommission(Clock(Now.AddDays(2)));

        var act = () => app.Deprecate(Now.AddDays(30), Clock(Now.AddDays(3)));
        act.Should().Throw<InvalidLifecycleTransitionException>()
           .Which.CurrentLifecycle.Should().Be(Lifecycle.Decommissioned);
    }

    [Fact]
    public void Decommission_when_Deprecated_and_after_sunsetDate_succeeds()
    {
        var app = NewActive();
        var sunset = Now.AddDays(1);
        app.Deprecate(sunset, Clock());
        app.Decommission(Clock(sunset));            // exact sunset — boundary uses >=

        app.Lifecycle.Should().Be(Lifecycle.Decommissioned);
        app.SunsetDate.Should().Be(sunset);         // sunset preserved on transition
    }

    [Fact]
    public void Decommission_when_Deprecated_and_before_sunsetDate_throws_with_reason_before_sunset_date()
    {
        var app = NewActive();
        app.Deprecate(Now.AddDays(30), Clock());

        var act = () => app.Decommission(Clock(Now.AddDays(15)));
        act.Should().Throw<InvalidLifecycleTransitionException>()
           .Which.Reason.Should().Be("before-sunset-date");
    }

    [Fact]
    public void Decommission_when_Active_throws_InvalidLifecycleTransitionException()
    {
        var app = NewActive();
        var act = () => app.Decommission(Clock());
        act.Should().Throw<InvalidLifecycleTransitionException>()
           .Which.CurrentLifecycle.Should().Be(Lifecycle.Active);
    }

    [Fact]
    public void Decommission_when_already_Decommissioned_throws_InvalidLifecycleTransitionException()
    {
        var app = NewActive();
        app.Deprecate(Now.AddDays(1), Clock());
        app.Decommission(Clock(Now.AddDays(2)));

        var act = () => app.Decommission(Clock(Now.AddDays(3)));
        act.Should().Throw<InvalidLifecycleTransitionException>()
           .Which.CurrentLifecycle.Should().Be(Lifecycle.Decommissioned);
    }
}
```

- [ ] **Step 3: Run tests, observe RED.**

```bash
cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests --filter \"FullyQualifiedName~ApplicationLifecycleTests\" --nologo -v minimal"
```

Expected: many `CS1061: 'Application' does not contain a definition for 'Lifecycle'/'EditMetadata'/'Deprecate'/'Decommission'` errors.

- [ ] **Step 4: Add the new properties + methods to the aggregate.**

Open `src/Modules/Catalog/Kartova.Catalog.Domain/Application.cs`. After the `CreatedAt` property declaration, add:

```csharp
    public Lifecycle Lifecycle { get; private set; } = Lifecycle.Active;
    public DateTimeOffset? SunsetDate { get; private set; }
    public uint Version { get; private set; }
```

Then add the three new methods at the bottom of the class, before the `KebabCase` regex declaration:

```csharp
    public void EditMetadata(string displayName, string description)
    {
        if (Lifecycle == Lifecycle.Decommissioned)
        {
            throw new InvalidLifecycleTransitionException(Lifecycle, "EditMetadata");
        }

        ValidateDisplayName(displayName);
        ValidateDescription(description);

        DisplayName = displayName;
        Description = description;
    }

    public void Deprecate(DateTimeOffset sunsetDate, TimeProvider clock)
    {
        if (Lifecycle != Lifecycle.Active)
        {
            throw new InvalidLifecycleTransitionException(Lifecycle, "Deprecate", SunsetDate);
        }

        if (sunsetDate <= clock.GetUtcNow())
        {
            throw new ArgumentException(
                "sunsetDate must be in the future.", nameof(sunsetDate));
        }

        Lifecycle = Lifecycle.Deprecated;
        SunsetDate = sunsetDate;
    }

    public void Decommission(TimeProvider clock)
    {
        if (Lifecycle != Lifecycle.Deprecated)
        {
            throw new InvalidLifecycleTransitionException(Lifecycle, "Decommission", SunsetDate);
        }

        if (clock.GetUtcNow() < SunsetDate!.Value)
        {
            throw new InvalidLifecycleTransitionException(
                Lifecycle, "Decommission", SunsetDate, reason: "before-sunset-date");
        }

        Lifecycle = Lifecycle.Decommissioned;
    }
```

- [ ] **Step 5: Run the new tests — observe GREEN.**

```bash
cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests --filter \"FullyQualifiedName~ApplicationLifecycleTests\" --nologo -v minimal"
```

Expected: 16 tests PASS.

- [ ] **Step 6: Run the existing `ApplicationTests` to confirm no regression.**

```bash
cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests --filter \"FullyQualifiedName~ApplicationTests\" --nologo -v minimal"
```

Expected: all existing tests PASS (the new properties default to sensible values; the existing `Create` factory is unchanged).

- [ ] **Step 7: Commit.**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Domain/Application.cs src/Modules/Catalog/Kartova.Catalog.Tests/Kartova.Catalog.Tests.csproj src/Modules/Catalog/Kartova.Catalog.Tests/ApplicationLifecycleTests.cs
git commit -m "$(cat <<'EOF'
feat(catalog): Application aggregate — Lifecycle/SunsetDate/Version + EditMetadata/Deprecate/Decommission

ADR-0073 invariants enforced at the aggregate boundary:
- EditMetadata blocked on Decommissioned (terminal state)
- Deprecate requires Active + future sunsetDate (strict >)
- Decommission requires Deprecated + now >= sunsetDate
  (admin override comes with RBAC slice — spec §13.2)

16 unit tests cover happy paths + every transition rule + boundaries.
Uses FakeTimeProvider for deterministic now-arithmetic.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: EF entity config + migration `AddApplicationLifecycle`

**Goal:** Map the new properties (`Lifecycle`, `SunsetDate`, `Version`) in `EfApplicationConfiguration` and ship a migration that adds two columns + RLS-toggle backfill. `Version` maps to Postgres `xmin` (proven in Task 3 spike).

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/EfApplicationConfiguration.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/Migrations/<ts>_AddApplicationLifecycle.cs` (auto-generated by EF tools)
- Auto-modified: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/Migrations/CatalogDbContextModelSnapshot.cs`

- [ ] **Step 1: Update `EfApplicationConfiguration` with the three new mappings.**

Open `src/Modules/Catalog/Kartova.Catalog.Infrastructure/EfApplicationConfiguration.cs`. Append three property mappings inside `Configure(...)`, after the `CreatedAt` mapping:

```csharp
        b.Property(x => x.Lifecycle)
            .HasColumnName("lifecycle")
            .HasColumnType("smallint")
            .HasConversion<short>()                           // enum → smallint
            .HasDefaultValue(Lifecycle.Active)
            .IsRequired();

        b.Property(x => x.SunsetDate)
            .HasColumnName("sunset_date")
            .HasColumnType("timestamptz");                    // nullable by default

        b.Property(x => x.Version)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsRowVersion()
            .IsConcurrencyToken();
```

Add `using Kartova.Catalog.Domain;` at the top if not already present (the `Lifecycle` enum lives there).

- [ ] **Step 2: Build to confirm the entity config compiles.**

```bash
cmd //c "dotnet build src/Modules/Catalog/Kartova.Catalog.Infrastructure --nologo -v minimal"
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. Fix any binding errors before moving on.

- [ ] **Step 3: Generate the migration via EF tools.**

```bash
cmd //c "dotnet ef migrations add AddApplicationLifecycle --project src/Modules/Catalog/Kartova.Catalog.Infrastructure --startup-project src/Kartova.Migrator -- --provider postgres"
```

Expected: two new files in `src/Modules/Catalog/Kartova.Catalog.Infrastructure/Migrations/` — `<ts>_AddApplicationLifecycle.cs` and `<ts>_AddApplicationLifecycle.Designer.cs`. The model snapshot file `CatalogDbContextModelSnapshot.cs` is also updated.

- [ ] **Step 4: Hand-edit the migration to add the RLS-toggle dance.**

Open the generated `<ts>_AddApplicationLifecycle.cs`. Replace the auto-generated `Up` method body with the same FORCE-RLS pattern slice 4 used in `AddApplicationDisplayName`:

```csharp
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: add lifecycle column with default 1=Active. Default value
            // backfills every existing row to Active in a single ALTER — no
            // separate UPDATE pass required (per spec §3 Decision #15).
            migrationBuilder.AddColumn<short>(
                name: "lifecycle",
                table: "catalog_applications",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);   // Lifecycle.Active

            // Step 2: add sunset_date column (nullable — only set when transitioning
            // to Deprecated).
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "sunset_date",
                table: "catalog_applications",
                type: "timestamptz",
                nullable: true);

            // Note: xmin is a Postgres system column and is NOT added here. EF Core
            // maps the `Version` shadow property to it automatically (see
            // EfApplicationConfiguration.HasColumnType("xid")). No DB schema change
            // for the concurrency token.
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "sunset_date", table: "catalog_applications");
            migrationBuilder.DropColumn(name: "lifecycle",   table: "catalog_applications");
        }
```

The default-value approach lets us skip the `NO FORCE RLS → UPDATE → FORCE RLS` dance because there's no row-by-row UPDATE — Postgres applies the `DEFAULT` in the column-add itself, and `DEFAULT` does not run through RLS policies.

- [ ] **Step 5: Verify the migration applies cleanly.**

```bash
cmd //c "docker compose up -d postgres"
cmd //c "dotnet run --project src/Kartova.Migrator --configuration Debug"
```

Expected: migrator logs `Applying migration '<ts>_AddApplicationLifecycle'` followed by `Done.` Confirm via psql:

```bash
docker compose exec postgres psql -U kartova -d kartova -c "\\d catalog_applications"
```

Expected: `lifecycle` column type `smallint NOT NULL DEFAULT 1`; `sunset_date` column type `timestamp with time zone` nullable.

- [ ] **Step 6: Build the whole solution to confirm no regression.**

```bash
cmd //c "dotnet build Kartova.slnx -c Debug --nologo -v minimal"
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 7: Commit.**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Infrastructure/EfApplicationConfiguration.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/Migrations/
git commit -m "$(cat <<'EOF'
feat(catalog): map Lifecycle/SunsetDate/Version on Application; migration AddApplicationLifecycle

EfApplicationConfiguration:
- Lifecycle → smallint NOT NULL DEFAULT 1 (Active)
- SunsetDate → timestamptz NULL
- Version → xmin (xid type) IsRowVersion + IsConcurrencyToken

Migration adds the two real columns; xmin is a Postgres system column so
no DDL is needed for the concurrency token.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: `ApplicationResponse` + projection update + ETag header on `GET /applications/{id}`

**Goal:** Extend the wire DTO to carry `Lifecycle`, `SunsetDate`, `Version` (base64-encoded), and emit `ETag` header on the existing single-resource GET. List rows carry `version` in the body but no per-row ETag header.

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Contracts/ApplicationResponse.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Application/ApplicationResponseExtensions.cs`
- Create: `src/Kartova.SharedKernel.AspNetCore/VersionEncoding.cs` (base64 helper used here and in `IfMatchEndpointFilter` Task 8)
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs` (only the `GetApplicationByIdAsync` delegate — emit ETag header)
- Modify: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/RegisterApplicationTests.cs` (extend existing wire-shape pin)

- [ ] **Step 1: Create `VersionEncoding` helper.**

```csharp
// src/Kartova.SharedKernel.AspNetCore/VersionEncoding.cs
namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Encodes/decodes the Postgres xmin <c>uint</c> rowversion as a base64 string
/// for the wire (ETag header + ApplicationResponse.Version field). The format
/// is little-endian 4 bytes; clients MUST treat it as opaque.
/// </summary>
public static class VersionEncoding
{
    public static string Encode(uint version)
    {
        Span<byte> bytes = stackalloc byte[4];
        BitConverter.TryWriteBytes(bytes, version);
        return Convert.ToBase64String(bytes);
    }

    public static bool TryDecode(string raw, out uint version)
    {
        version = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        Span<byte> bytes = stackalloc byte[4];
        if (!Convert.TryFromBase64String(raw, bytes, out var written) || written != 4)
        {
            return false;
        }
        version = BitConverter.ToUInt32(bytes);
        return true;
    }
}
```

- [ ] **Step 2: Extend `ApplicationResponse` with three new fields.**

Open `src/Modules/Catalog/Kartova.Catalog.Contracts/ApplicationResponse.cs`. The current shape is a 7-field record. Add the new fields **at the end** (preserves ordinal positional matching for any code that uses positional construction):

```csharp
using System.Diagnostics.CodeAnalysis;
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Contracts;

[ExcludeFromCodeCoverage]
public sealed record ApplicationResponse(
    Guid Id,
    Guid TenantId,
    string Name,
    string DisplayName,
    string Description,
    Guid OwnerUserId,
    DateTimeOffset CreatedAt,
    Lifecycle Lifecycle,
    DateTimeOffset? SunsetDate,
    string Version);
```

The `Kartova.Catalog.Contracts.csproj` already references `Kartova.Catalog.Domain` (used by `ApplicationSortField` enum). If the build complains about `Lifecycle`, verify the csproj has `<ProjectReference Include="..\Kartova.Catalog.Domain\Kartova.Catalog.Domain.csproj" />`.

- [ ] **Step 3: Update the projection extension.**

Open `src/Modules/Catalog/Kartova.Catalog.Application/ApplicationResponseExtensions.cs` and update the projection:

```csharp
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.AspNetCore;

namespace Kartova.Catalog.Application;

internal static class ApplicationResponseExtensions
{
    internal static ApplicationResponse ToResponse(this Kartova.Catalog.Domain.Application app)
        => new(
            app.Id.Value,
            app.TenantId.Value,
            app.Name,
            app.DisplayName,
            app.Description,
            app.OwnerUserId,
            app.CreatedAt,
            app.Lifecycle,
            app.SunsetDate,
            VersionEncoding.Encode(app.Version));
}
```

The `Kartova.Catalog.Application.csproj` will need a project reference to `Kartova.SharedKernel.AspNetCore` if it doesn't have one. If the build fails on `VersionEncoding`, add the reference:

```bash
cmd //c "dotnet add src/Modules/Catalog/Kartova.Catalog.Application reference src/Kartova.SharedKernel.AspNetCore"
```

- [ ] **Step 4: Emit `ETag` header on `GetApplicationByIdAsync`.**

Open `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs`. Find the `GetApplicationByIdAsync` method. Replace its `return Results.Ok(resp);` line with:

```csharp
        // Emit ETag (RFC 7232 quoted) so clients can capture for a future
        // PUT If-Match request. Only the single-resource GET emits the header;
        // list rows carry `version` in the body but no per-row ETag.
        return Results.Ok(resp).WithEtag(resp.Version);
```

Then create the small helper extension in the same file (or a sibling `EndpointResultExtensions.cs` — slice-3 endpoint-delegate file already houses one-off helpers):

```csharp
internal static class EndpointResultExtensions
{
    internal static IResult WithEtag(this IResult inner, string version) =>
        new EtagWrappedResult(inner, version);

    private sealed class EtagWrappedResult(IResult inner, string version) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers["ETag"] = $"\"{version}\"";
            await inner.ExecuteAsync(httpContext);
        }
    }
}
```

This wrapper preserves existing serialization while emitting the header. The same helper is reused in Task 11 for the PUT response.

- [ ] **Step 5: Extend the existing register integration test to assert the new wire shape.**

Open `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/RegisterApplicationTests.cs` and locate the `POST_with_valid_payload_creates_row` test (or the closest equivalent that asserts the response body shape). Add assertions for the new fields right after the existing field assertions:

```csharp
        body!.Lifecycle.Should().Be(Lifecycle.Active);
        body.SunsetDate.Should().BeNull();
        body.Version.Should().NotBeNullOrWhiteSpace();        // base64 4-byte xmin

        // Verify Version round-trips via VersionEncoding (sanity check the wire encoding).
        VersionEncoding.TryDecode(body.Version, out _).Should().BeTrue();
```

Add a similar new test for the `GET /applications/{id}` ETag header:

```csharp
    [Fact]
    public async Task GET_by_id_emits_ETag_header_matching_Version_field()
    {
        var client = await Fixture.CreateAuthenticatedClientAsync(TenantA.User);
        var registered = await RegisterApplicationAsync(client, "payments-api", "Payments", "Desc.");

        var resp = await client.GetAsync($"/api/v1/catalog/applications/{registered.Id}");
        resp.IsSuccessStatusCode.Should().BeTrue();

        var etag = resp.Headers.ETag?.Tag;
        etag.Should().NotBeNullOrWhiteSpace();
        etag.Should().Be($"\"{registered.Version}\"");        // RFC 7232 quoted
    }
```

If `RegisterApplicationAsync` doesn't exist as a helper in this test class, copy the inline registration pattern from a neighboring test.

- [ ] **Step 6: Run the test suite.**

```bash
cmd //c "docker compose up -d postgres keycloak"
cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter \"FullyQualifiedName~RegisterApplicationTests\" --nologo -v minimal"
```

Expected: all existing tests still pass; the two updates pass.

- [ ] **Step 7: Commit.**

```bash
git add src/Kartova.SharedKernel.AspNetCore/VersionEncoding.cs src/Modules/Catalog/Kartova.Catalog.Contracts/ApplicationResponse.cs src/Modules/Catalog/Kartova.Catalog.Application/ src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs src/Modules/Catalog/Kartova.Catalog.IntegrationTests/RegisterApplicationTests.cs
git commit -m "$(cat <<'EOF'
feat(catalog): ApplicationResponse carries Lifecycle/SunsetDate/Version; GET emits ETag

- VersionEncoding: opaque base64 4-byte uint codec used by ETag header +
  Version body field + If-Match parsing (Task 8)
- GetApplicationByIdAsync wraps Results.Ok with WithEtag(version)
- RegisterApplicationTests pins the new wire shape + the ETag header

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 8: `IfMatchEndpointFilter` + `PreconditionRequiredException` + handler

**Goal:** Parse `If-Match` request header into `HttpContext.Items["expected-version"]` for downstream handlers, throw `PreconditionRequiredException` when missing/malformed, map that exception to RFC 7807 `428 Precondition Required`.

**Files:**
- Create: `src/Kartova.SharedKernel.AspNetCore/PreconditionRequiredException.cs`
- Create: `src/Kartova.SharedKernel.AspNetCore/IfMatchEndpointFilter.cs`
- Create: `src/Kartova.SharedKernel.AspNetCore/PreconditionRequiredExceptionHandler.cs`
- Create: `src/Kartova.SharedKernel.AspNetCore.Tests/IfMatchEndpointFilterTests.cs`
- Create: `src/Kartova.SharedKernel.AspNetCore.Tests/PreconditionRequiredExceptionHandlerTests.cs`
- Modify: `src/Kartova.Api/Program.cs` (register the new exception handler)

- [ ] **Step 1: Create the exception type.**

```csharp
// src/Kartova.SharedKernel.AspNetCore/PreconditionRequiredException.cs
namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Thrown by <see cref="IfMatchEndpointFilter"/> when the <c>If-Match</c>
/// header is absent or malformed. Mapped to RFC 7807 <c>428 Precondition
/// Required</c> by <see cref="PreconditionRequiredExceptionHandler"/>.
/// </summary>
public sealed class PreconditionRequiredException(string message) : Exception(message);
```

- [ ] **Step 2: Create the endpoint filter.**

```csharp
// src/Kartova.SharedKernel.AspNetCore/IfMatchEndpointFilter.cs
using Microsoft.AspNetCore.Http;

namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Reads the <c>If-Match</c> request header on idempotent edit endpoints (PUT)
/// and stores the decoded version (<c>uint</c>) in <c>HttpContext.Items</c>
/// under the key <see cref="ExpectedVersionKey"/>. Endpoint delegates read it
/// from there and pass to the command handler. Missing or malformed header →
/// <see cref="PreconditionRequiredException"/> → 428.
/// </summary>
public sealed class IfMatchEndpointFilter : IEndpointFilter
{
    public const string ExpectedVersionKey = "expected-version";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var headers = ctx.HttpContext.Request.Headers;
        if (!headers.TryGetValue("If-Match", out var values) || values.Count == 0)
        {
            throw new PreconditionRequiredException(
                "If-Match header is required for this endpoint.");
        }

        var raw = values.ToString().Trim('"');
        if (!VersionEncoding.TryDecode(raw, out var expected))
        {
            throw new PreconditionRequiredException(
                "If-Match header value is not a valid version token.");
        }

        ctx.HttpContext.Items[ExpectedVersionKey] = expected;
        return await next(ctx);
    }
}
```

- [ ] **Step 3: Create the handler.**

```csharp
// src/Kartova.SharedKernel.AspNetCore/PreconditionRequiredExceptionHandler.cs
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Maps <see cref="PreconditionRequiredException"/> (thrown by the
/// <see cref="IfMatchEndpointFilter"/> when the header is missing or malformed)
/// to RFC 7807 <c>428 Precondition Required</c> with
/// <c>type = </c><see cref="ProblemTypes.PreconditionRequired"/>.
/// </summary>
public sealed class PreconditionRequiredExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;

    public PreconditionRequiredExceptionHandler(IProblemDetailsService problemDetails)
    {
        _problemDetails = problemDetails;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        if (exception is not PreconditionRequiredException pre) return false;

        httpContext.Response.StatusCode = StatusCodes.Status428PreconditionRequired;

        var problem = new ProblemDetails
        {
            Type = ProblemTypes.PreconditionRequired,
            Title = "Precondition required",
            Status = StatusCodes.Status428PreconditionRequired,
            Detail = pre.Message,
        };

        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
```

- [ ] **Step 4: Write the failing tests.**

```csharp
// src/Kartova.SharedKernel.AspNetCore.Tests/IfMatchEndpointFilterTests.cs
using FluentAssertions;
using Kartova.SharedKernel.AspNetCore;
using Microsoft.AspNetCore.Http;

namespace Kartova.SharedKernel.AspNetCore.Tests;

public class IfMatchEndpointFilterTests
{
    [Fact]
    public async Task Throws_when_header_missing()
    {
        var ctx = MakeContext(headerValue: null);
        var filter = new IfMatchEndpointFilter();

        var act = async () => await filter.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(null));
        await act.Should().ThrowAsync<PreconditionRequiredException>().WithMessage("*required*");
    }

    [Fact]
    public async Task Throws_when_header_malformed()
    {
        var ctx = MakeContext(headerValue: "\"not-base64!\"");
        var filter = new IfMatchEndpointFilter();

        var act = async () => await filter.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(null));
        await act.Should().ThrowAsync<PreconditionRequiredException>().WithMessage("*valid version*");
    }

    [Fact]
    public async Task Stores_decoded_version_in_HttpContext_Items_when_header_valid()
    {
        var encoded = VersionEncoding.Encode(42u);
        var ctx = MakeContext(headerValue: $"\"{encoded}\"");
        var filter = new IfMatchEndpointFilter();
        var nextCalled = false;

        await filter.InvokeAsync(ctx, _ => { nextCalled = true; return ValueTask.FromResult<object?>(null); });

        nextCalled.Should().BeTrue();
        ctx.HttpContext.Items[IfMatchEndpointFilter.ExpectedVersionKey].Should().Be(42u);
    }

    [Fact]
    public async Task Accepts_unquoted_header_value()
    {
        var encoded = VersionEncoding.Encode(7u);
        var ctx = MakeContext(headerValue: encoded);                 // no quotes
        var filter = new IfMatchEndpointFilter();

        await filter.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(null));

        ctx.HttpContext.Items[IfMatchEndpointFilter.ExpectedVersionKey].Should().Be(7u);
    }

    private static EndpointFilterInvocationContext MakeContext(string? headerValue)
    {
        var http = new DefaultHttpContext();
        if (headerValue is not null)
        {
            http.Request.Headers["If-Match"] = headerValue;
        }
        return new DefaultEndpointFilterInvocationContext(http);
    }
}
```

- [ ] **Step 5: Add a tiny mapping pinning test for the handler.**

```csharp
// src/Kartova.SharedKernel.AspNetCore.Tests/PreconditionRequiredExceptionHandlerTests.cs
using FluentAssertions;
using Kartova.SharedKernel.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using NSubstitute;

namespace Kartova.SharedKernel.AspNetCore.Tests;

public class PreconditionRequiredExceptionHandlerTests
{
    [Fact]
    public async Task Maps_PreconditionRequiredException_to_428_with_correct_type()
    {
        var pds = Substitute.For<IProblemDetailsService>();
        pds.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(true);
        var handler = new PreconditionRequiredExceptionHandler(pds);
        var http = new DefaultHttpContext();

        var handled = await handler.TryHandleAsync(http,
            new PreconditionRequiredException("If-Match required."),
            CancellationToken.None);

        handled.Should().BeTrue();
        http.Response.StatusCode.Should().Be(StatusCodes.Status428PreconditionRequired);
        await pds.Received(1).TryWriteAsync(Arg.Is<ProblemDetailsContext>(c =>
            c.ProblemDetails.Type == ProblemTypes.PreconditionRequired &&
            c.ProblemDetails.Status == 428));
    }

    [Fact]
    public async Task Returns_false_for_unrelated_exception()
    {
        var pds = Substitute.For<IProblemDetailsService>();
        var handler = new PreconditionRequiredExceptionHandler(pds);

        var handled = await handler.TryHandleAsync(new DefaultHttpContext(),
            new InvalidOperationException("nope"), CancellationToken.None);

        handled.Should().BeFalse();
        await pds.DidNotReceive().TryWriteAsync(Arg.Any<ProblemDetailsContext>());
    }
}
```

If `NSubstitute` isn't already a dep on the test project, install it:

```bash
cmd //c "dotnet add src/Kartova.SharedKernel.AspNetCore.Tests package NSubstitute"
```

- [ ] **Step 6: Register the handler in `Program.cs`.**

Open `src/Kartova.Api/Program.cs`. Find the existing `builder.Services.AddExceptionHandler<DomainValidationExceptionHandler>();` line and add the new handler **before** it (order matters: more-specific handlers first):

```csharp
builder.Services.AddExceptionHandler<PreconditionRequiredExceptionHandler>();
builder.Services.AddExceptionHandler<DomainValidationExceptionHandler>();
// ... existing handlers
```

- [ ] **Step 7: Run the tests.**

```bash
cmd //c "dotnet test src/Kartova.SharedKernel.AspNetCore.Tests --filter \"FullyQualifiedName~IfMatchEndpointFilterTests|FullyQualifiedName~PreconditionRequiredExceptionHandlerTests\" --nologo -v minimal"
```

Expected: 6 tests PASS.

- [ ] **Step 8: Commit.**

```bash
git add src/Kartova.SharedKernel.AspNetCore/{PreconditionRequiredException,IfMatchEndpointFilter,PreconditionRequiredExceptionHandler}.cs src/Kartova.SharedKernel.AspNetCore.Tests/{IfMatchEndpointFilterTests,PreconditionRequiredExceptionHandlerTests}.cs src/Kartova.SharedKernel.AspNetCore.Tests/Kartova.SharedKernel.AspNetCore.Tests.csproj src/Kartova.Api/Program.cs
git commit -m "$(cat <<'EOF'
feat(api): IfMatchEndpointFilter + PreconditionRequiredException → 428

Filter parses If-Match header into HttpContext.Items[\"expected-version\"]
for downstream handlers; missing or malformed header throws
PreconditionRequiredException, mapped to RFC 7807 428 by the new handler.

Wire format is opaque base64 4-byte uint (VersionEncoding, Task 7).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 9: `ConcurrencyConflictExceptionHandler`

**Goal:** Map EF Core `DbUpdateConcurrencyException` (raised when `xmin` doesn't match the supplied `OriginalValue`) to RFC 7807 `412 Precondition Failed` with `currentVersion` extension.

**Files:**
- Create: `src/Kartova.SharedKernel.AspNetCore/ConcurrencyConflictExceptionHandler.cs`
- Create: `src/Kartova.SharedKernel.AspNetCore.Tests/ConcurrencyConflictExceptionHandlerTests.cs`
- Modify: `src/Kartova.Api/Program.cs` (register the handler)

- [ ] **Step 1: Create the handler.**

```csharp
// src/Kartova.SharedKernel.AspNetCore/ConcurrencyConflictExceptionHandler.cs
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Maps EF Core <see cref="DbUpdateConcurrencyException"/> (raised when the
/// supplied row-version <c>OriginalValue</c> doesn't match the database's
/// current value) to RFC 7807 <c>412 Precondition Failed</c> with
/// <c>type = </c><see cref="ProblemTypes.ConcurrencyConflict"/>.
///
/// The current version is captured from the entry's <c>DatabaseValues</c>
/// when available so the client can retry against the latest state.
/// </summary>
public sealed class ConcurrencyConflictExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;

    public ConcurrencyConflictExceptionHandler(IProblemDetailsService problemDetails)
    {
        _problemDetails = problemDetails;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        if (exception is not DbUpdateConcurrencyException dbEx) return false;

        httpContext.Response.StatusCode = StatusCodes.Status412PreconditionFailed;

        var problem = new ProblemDetails
        {
            Type = ProblemTypes.ConcurrencyConflict,
            Title = "Concurrency conflict",
            Status = StatusCodes.Status412PreconditionFailed,
            Detail = "The resource was modified by another request. Reload and reapply.",
        };

        // Best-effort: pull the database's current version into the extensions
        // dictionary so the client can resync without a separate GET.
        var entry = dbEx.Entries.FirstOrDefault();
        if (entry is not null)
        {
            var dbValues = await entry.GetDatabaseValuesAsync(ct);
            if (dbValues is not null && dbValues["Version"] is uint currentVersion)
            {
                problem.Extensions["currentVersion"] = VersionEncoding.Encode(currentVersion);
            }
        }

        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
```

- [ ] **Step 2: Write the mapping test.**

```csharp
// src/Kartova.SharedKernel.AspNetCore.Tests/ConcurrencyConflictExceptionHandlerTests.cs
using FluentAssertions;
using Kartova.SharedKernel.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Kartova.SharedKernel.AspNetCore.Tests;

public class ConcurrencyConflictExceptionHandlerTests
{
    [Fact]
    public async Task Maps_DbUpdateConcurrencyException_to_412_with_correct_type()
    {
        var pds = Substitute.For<IProblemDetailsService>();
        pds.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(true);
        var handler = new ConcurrencyConflictExceptionHandler(pds);
        var http = new DefaultHttpContext();

        // Construct the exception with no entries — the handler still produces
        // the 412 envelope, just without the currentVersion extension.
        var ex = new DbUpdateConcurrencyException("conflict", new List<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry>());

        var handled = await handler.TryHandleAsync(http, ex, CancellationToken.None);

        handled.Should().BeTrue();
        http.Response.StatusCode.Should().Be(StatusCodes.Status412PreconditionFailed);
        await pds.Received(1).TryWriteAsync(Arg.Is<ProblemDetailsContext>(c =>
            c.ProblemDetails.Type == ProblemTypes.ConcurrencyConflict &&
            c.ProblemDetails.Status == 412));
    }

    [Fact]
    public async Task Returns_false_for_unrelated_exception()
    {
        var pds = Substitute.For<IProblemDetailsService>();
        var handler = new ConcurrencyConflictExceptionHandler(pds);

        var handled = await handler.TryHandleAsync(new DefaultHttpContext(),
            new InvalidOperationException(), CancellationToken.None);

        handled.Should().BeFalse();
    }
}
```

(The "currentVersion populated from real EF entry" path is exercised by the integration test `PUT_with_stale_If_Match_returns_412_with_currentVersion` in Task 12 — that's the right layer for the full roundtrip.)

- [ ] **Step 3: Register the handler.**

Open `src/Kartova.Api/Program.cs`. Insert the registration before `DomainValidationExceptionHandler` and after `PreconditionRequiredExceptionHandler` (Task 8):

```csharp
builder.Services.AddExceptionHandler<PreconditionRequiredExceptionHandler>();
builder.Services.AddExceptionHandler<ConcurrencyConflictExceptionHandler>();    // NEW
builder.Services.AddExceptionHandler<DomainValidationExceptionHandler>();
```

- [ ] **Step 4: Run the tests.**

```bash
cmd //c "dotnet test src/Kartova.SharedKernel.AspNetCore.Tests --filter \"FullyQualifiedName~ConcurrencyConflictExceptionHandlerTests\" --nologo -v minimal"
```

Expected: 2 tests PASS.

- [ ] **Step 5: Commit.**

```bash
git add src/Kartova.SharedKernel.AspNetCore/ConcurrencyConflictExceptionHandler.cs src/Kartova.SharedKernel.AspNetCore.Tests/ConcurrencyConflictExceptionHandlerTests.cs src/Kartova.Api/Program.cs
git commit -m "$(cat <<'EOF'
feat(api): ConcurrencyConflictExceptionHandler → 412 with currentVersion

Maps EF DbUpdateConcurrencyException to RFC 7807 412 Precondition Failed.
When EF entry is available, pulls Version from DatabaseValues and emits as
currentVersion extension so clients can resync without a separate GET.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 10: `LifecycleConflictExceptionHandler`

**Goal:** Map `InvalidLifecycleTransitionException` (Task 4) to RFC 7807 `409 Conflict` with `currentLifecycle`, `attemptedTransition`, `sunsetDate?`, `reason?` extensions.

**Files:**
- Create: `src/Kartova.SharedKernel.AspNetCore/LifecycleConflictExceptionHandler.cs`
- Create: `src/Kartova.SharedKernel.AspNetCore.Tests/LifecycleConflictExceptionHandlerTests.cs`
- Modify: `src/Kartova.Api/Program.cs` (register the handler)

**Decision:** This handler lives in `SharedKernel.AspNetCore` (alongside the other handlers), but it depends on `Kartova.Catalog.Domain.InvalidLifecycleTransitionException`. To avoid a dependency from SharedKernel back to Catalog, we'll detect the exception type via reflection-by-name (`exception.GetType().Name == "InvalidLifecycleTransitionException"`) and read the public properties via dynamic. The trade-off is mild: the handler is generic across modules so future entities (Service, Component) reuse it without further coupling.

Alternative (if reflection feels brittle): introduce a `Kartova.SharedKernel.Domain.IDomainConflict` interface in `Kartova.SharedKernel`, have `InvalidLifecycleTransitionException` implement it, and have the handler match on the interface. Cleaner but adds two more files. **Picking the reflection approach** for this slice — call it out as backlog if it becomes an issue.

- [ ] **Step 1: Create the handler.**

```csharp
// src/Kartova.SharedKernel.AspNetCore/LifecycleConflictExceptionHandler.cs
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Maps any module's <c>InvalidLifecycleTransitionException</c> (matched by
/// type name to avoid SharedKernel → module coupling) to RFC 7807
/// <c>409 Conflict</c> with <c>type = </c><see cref="ProblemTypes.LifecycleConflict"/>.
///
/// Extensions populated from the exception's public properties:
/// <list type="bullet">
///   <item><c>currentLifecycle</c> — string name of the current state.</item>
///   <item><c>attemptedTransition</c> — name of the rejected transition.</item>
///   <item><c>sunsetDate</c> — present when the exception carries one (deprecate/decommission paths).</item>
///   <item><c>reason</c> — present when set (e.g. <c>before-sunset-date</c>).</item>
/// </list>
/// </summary>
public sealed class LifecycleConflictExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;

    public LifecycleConflictExceptionHandler(IProblemDetailsService problemDetails)
    {
        _problemDetails = problemDetails;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        if (exception.GetType().Name != "InvalidLifecycleTransitionException")
        {
            return false;
        }

        var t = exception.GetType();
        var current = t.GetProperty("CurrentLifecycle")?.GetValue(exception)?.ToString();
        var attempted = t.GetProperty("AttemptedTransition")?.GetValue(exception)?.ToString();
        var sunset = t.GetProperty("SunsetDate")?.GetValue(exception) as DateTimeOffset?;
        var reason = t.GetProperty("Reason")?.GetValue(exception)?.ToString();

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;

        var problem = new ProblemDetails
        {
            Type = ProblemTypes.LifecycleConflict,
            Title = "Lifecycle transition not allowed",
            Status = StatusCodes.Status409Conflict,
            Detail = exception.Message,
        };

        if (current is not null)   problem.Extensions["currentLifecycle"]   = current;
        if (attempted is not null) problem.Extensions["attemptedTransition"] = attempted;
        if (sunset.HasValue)       problem.Extensions["sunsetDate"]          = sunset.Value;
        if (reason is not null)    problem.Extensions["reason"]              = reason;

        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
```

- [ ] **Step 2: Write the mapping tests.**

```csharp
// src/Kartova.SharedKernel.AspNetCore.Tests/LifecycleConflictExceptionHandlerTests.cs
using FluentAssertions;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using NSubstitute;

namespace Kartova.SharedKernel.AspNetCore.Tests;

public class LifecycleConflictExceptionHandlerTests
{
    [Fact]
    public async Task Maps_to_409_with_currentLifecycle_and_attemptedTransition_extensions()
    {
        var pds = Substitute.For<IProblemDetailsService>();
        pds.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(true);
        var handler = new LifecycleConflictExceptionHandler(pds);
        var http = new DefaultHttpContext();

        var ex = new InvalidLifecycleTransitionException(
            Lifecycle.Decommissioned, "Deprecate");

        var handled = await handler.TryHandleAsync(http, ex, CancellationToken.None);

        handled.Should().BeTrue();
        http.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        await pds.Received(1).TryWriteAsync(Arg.Is<ProblemDetailsContext>(c =>
            c.ProblemDetails.Type == ProblemTypes.LifecycleConflict &&
            (string)c.ProblemDetails.Extensions["currentLifecycle"]! == "Decommissioned" &&
            (string)c.ProblemDetails.Extensions["attemptedTransition"]! == "Deprecate"));
    }

    [Fact]
    public async Task Includes_sunsetDate_and_reason_when_provided()
    {
        var pds = Substitute.For<IProblemDetailsService>();
        pds.TryWriteAsync(Arg.Any<ProblemDetailsContext>()).Returns(true);
        var handler = new LifecycleConflictExceptionHandler(pds);
        var http = new DefaultHttpContext();
        var sunset = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

        var ex = new InvalidLifecycleTransitionException(
            Lifecycle.Deprecated, "Decommission", sunset, "before-sunset-date");

        await handler.TryHandleAsync(http, ex, CancellationToken.None);

        await pds.Received(1).TryWriteAsync(Arg.Is<ProblemDetailsContext>(c =>
            c.ProblemDetails.Extensions.ContainsKey("sunsetDate") &&
            (string)c.ProblemDetails.Extensions["reason"]! == "before-sunset-date"));
    }

    [Fact]
    public async Task Returns_false_for_unrelated_exception()
    {
        var pds = Substitute.For<IProblemDetailsService>();
        var handler = new LifecycleConflictExceptionHandler(pds);

        var handled = await handler.TryHandleAsync(new DefaultHttpContext(),
            new InvalidOperationException(), CancellationToken.None);

        handled.Should().BeFalse();
    }
}
```

If `Kartova.SharedKernel.AspNetCore.Tests` doesn't reference `Kartova.Catalog.Domain`, add the reference for the test only:

```bash
cmd //c "dotnet add src/Kartova.SharedKernel.AspNetCore.Tests reference src/Modules/Catalog/Kartova.Catalog.Domain"
```

- [ ] **Step 3: Register the handler.**

Open `src/Kartova.Api/Program.cs`. Add after `ConcurrencyConflictExceptionHandler`:

```csharp
builder.Services.AddExceptionHandler<PreconditionRequiredExceptionHandler>();
builder.Services.AddExceptionHandler<ConcurrencyConflictExceptionHandler>();
builder.Services.AddExceptionHandler<LifecycleConflictExceptionHandler>();      // NEW
builder.Services.AddExceptionHandler<DomainValidationExceptionHandler>();
```

- [ ] **Step 4: Run the tests.**

```bash
cmd //c "dotnet test src/Kartova.SharedKernel.AspNetCore.Tests --filter \"FullyQualifiedName~LifecycleConflictExceptionHandlerTests\" --nologo -v minimal"
```

Expected: 3 tests PASS.

- [ ] **Step 5: Commit.**

```bash
git add src/Kartova.SharedKernel.AspNetCore/LifecycleConflictExceptionHandler.cs src/Kartova.SharedKernel.AspNetCore.Tests/LifecycleConflictExceptionHandlerTests.cs src/Kartova.SharedKernel.AspNetCore.Tests/Kartova.SharedKernel.AspNetCore.Tests.csproj src/Kartova.Api/Program.cs
git commit -m "$(cat <<'EOF'
feat(api): LifecycleConflictExceptionHandler → 409 with current state extensions

Module-agnostic handler matches InvalidLifecycleTransitionException by name
(avoids SharedKernel → Catalog coupling) and projects the public properties
to RFC 7807 extensions: currentLifecycle, attemptedTransition, sunsetDate?,
reason?.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 11: `EditApplicationCommand` + handler + endpoint + integration tests

**Goal:** Wire the PUT edit endpoint end-to-end. Command in Application layer, handler in Infrastructure (matches slice-3 pattern), endpoint in `CatalogModule.MapEndpoints` + `CatalogEndpointDelegates`, full integration coverage.

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Contracts/EditApplicationRequest.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Application/EditApplicationCommand.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/EditApplicationHandler.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs` (add `EditApplicationAsync`)
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs` (register endpoint + handler)
- Create: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/EditApplicationTests.cs`

- [ ] **Step 1: Create the request DTO.**

```csharp
// src/Modules/Catalog/Kartova.Catalog.Contracts/EditApplicationRequest.cs
using System.Diagnostics.CodeAnalysis;

namespace Kartova.Catalog.Contracts;

[ExcludeFromCodeCoverage]
public sealed record EditApplicationRequest(string DisplayName, string Description);
```

- [ ] **Step 2: Create the command record.**

```csharp
// src/Modules/Catalog/Kartova.Catalog.Application/EditApplicationCommand.cs
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Application;

/// <summary>
/// Edit metadata on an existing Application. ExpectedVersion drives optimistic
/// concurrency — handler sets it as EF OriginalValue so SaveChanges raises
/// DbUpdateConcurrencyException on stale ETag.
/// </summary>
public sealed record EditApplicationCommand(
    ApplicationId Id,
    string DisplayName,
    string Description,
    uint ExpectedVersion);
```

- [ ] **Step 3: Create the handler.**

```csharp
// src/Modules/Catalog/Kartova.Catalog.Infrastructure/EditApplicationHandler.cs
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Direct-dispatch handler for <see cref="EditApplicationCommand"/>. Mirrors
/// the GetApplicationByIdHandler nullable-return pattern: null = not found in
/// current tenant scope (RLS auto-filters cross-tenant rows). Endpoint
/// delegate maps null to RFC 7807 404.
///
/// Concurrency: handler sets <c>OriginalValue(Version)</c> to the supplied
/// ExpectedVersion so EF's UPDATE includes <c>WHERE xmin = :expected</c>;
/// mismatch raises DbUpdateConcurrencyException → 412
/// (ConcurrencyConflictExceptionHandler).
/// </summary>
public sealed class EditApplicationHandler
{
    public async Task<ApplicationResponse?> Handle(
        EditApplicationCommand cmd,
        CatalogDbContext db,
        ITenantContext tenant,
        CancellationToken ct)
    {
        var app = await db.Applications
            .FirstOrDefaultAsync(ApplicationSortSpecs.IdEquals(cmd.Id.Value), ct);
        if (app is null) return null;

        db.Entry(app).Property(a => a.Version).OriginalValue = cmd.ExpectedVersion;

        app.EditMetadata(cmd.DisplayName, cmd.Description);
        await db.SaveChangesAsync(ct);

        return app.ToResponse();
    }
}
```

- [ ] **Step 4: Add the endpoint delegate.**

Open `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs`. Append below `ListApplicationsAsync`:

```csharp
    /// <summary>
    /// PUT edit metadata on an existing Application. Full-replacement body
    /// (DisplayName, Description) per ADR-0096. If-Match required (parsed by
    /// IfMatchEndpointFilter into HttpContext.Items["expected-version"]).
    /// Stale If-Match → DbUpdateConcurrencyException → 412 ProblemDetails.
    /// Edit on Decommissioned → InvalidLifecycleTransitionException → 409.
    /// </summary>
    internal static async Task<IResult> EditApplicationAsync(
        Guid id,
        [FromBody] EditApplicationRequest request,
        EditApplicationHandler handler,
        CatalogDbContext db,
        ITenantContext tenant,
        HttpContext http,
        CancellationToken ct)
    {
        var expected = (uint)http.Items[IfMatchEndpointFilter.ExpectedVersionKey]!;

        var resp = await handler.Handle(
            new EditApplicationCommand(new ApplicationId(id), request.DisplayName, request.Description, expected),
            db, tenant, ct);

        if (resp is null)
        {
            return Results.Problem(
                type: ProblemTypes.ResourceNotFound,
                title: "Application not found",
                detail: "No application with that id is visible in the current tenant.",
                statusCode: StatusCodes.Status404NotFound);
        }

        return Results.Ok(resp).WithEtag(resp.Version);
    }
```

Add `using Kartova.Catalog.Domain;` at the top if not already present (`ApplicationId` is in Domain).

- [ ] **Step 5: Register the endpoint + handler in `CatalogModule`.**

Open `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs`.

In `MapEndpoints`, after the existing `ListApplications` registration:

```csharp
        tenant.MapPut("/applications/{id:guid}", CatalogEndpointDelegates.EditApplicationAsync)
              .WithName("EditApplication")
              .AddEndpointFilter<IfMatchEndpointFilter>()
              .Produces<ApplicationResponse>(StatusCodes.Status200OK)
              .ProducesProblem(StatusCodes.Status400BadRequest)
              .ProducesProblem(StatusCodes.Status404NotFound)
              .ProducesProblem(StatusCodes.Status409Conflict)
              .ProducesProblem(StatusCodes.Status412PreconditionFailed)
              .ProducesProblem(StatusCodes.Status428PreconditionRequired);
```

In `RegisterServices`, alongside the existing handler registrations:

```csharp
        services.AddScoped<EditApplicationHandler>();
```

- [ ] **Step 6: Build to confirm wiring compiles.**

```bash
cmd //c "dotnet build src/Modules/Catalog/Kartova.Catalog.Infrastructure --nologo -v minimal"
```

Expected: clean build.

- [ ] **Step 7: Write the integration tests.**

```csharp
// src/Modules/Catalog/Kartova.Catalog.IntegrationTests/EditApplicationTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;

namespace Kartova.Catalog.IntegrationTests;

public class EditApplicationTests : IClassFixture<KartovaApiFixture>
{
    private readonly KartovaApiFixture Fixture;
    public EditApplicationTests(KartovaApiFixture fixture) => Fixture = fixture;

    [Fact]
    public async Task PUT_with_valid_payload_returns_200_and_advances_version()
    {
        var client = await Fixture.CreateAuthenticatedClientAsync(KartovaApiFixture.OrgAUser);
        var registered = await RegisterAsync(client, "edit-app-1", "Edit App 1", "Desc 1.");

        var put = NewPut(registered.Id, registered.Version, "Edit App 1 Renamed", "Desc 1 Renamed.");
        var resp = await client.SendAsync(put);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>();
        body!.DisplayName.Should().Be("Edit App 1 Renamed");
        body.Description.Should().Be("Desc 1 Renamed.");
        body.Version.Should().NotBe(registered.Version, because: "xmin advances on update");

        resp.Headers.ETag?.Tag.Should().Be($"\"{body.Version}\"");
    }

    [Fact]
    public async Task PUT_without_If_Match_returns_428()
    {
        var client = await Fixture.CreateAuthenticatedClientAsync(KartovaApiFixture.OrgAUser);
        var registered = await RegisterAsync(client, "edit-app-2", "Edit App 2", "Desc.");

        var put = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/catalog/applications/{registered.Id}")
        {
            Content = JsonContent.Create(new EditApplicationRequest("X", "Y")),
        };
        // Intentionally omit If-Match.

        var resp = await client.SendAsync(put);
        resp.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemPayload>();
        problem!.Type.Should().Be(ProblemTypes.PreconditionRequired);
    }

    [Fact]
    public async Task PUT_with_stale_If_Match_returns_412_with_currentVersion()
    {
        var client = await Fixture.CreateAuthenticatedClientAsync(KartovaApiFixture.OrgAUser);
        var registered = await RegisterAsync(client, "edit-app-3", "Edit App 3", "Desc.");

        // First PUT advances xmin.
        var firstPut = NewPut(registered.Id, registered.Version, "Edit App 3 v2", "Desc v2.");
        var firstResp = await client.SendAsync(firstPut);
        firstResp.IsSuccessStatusCode.Should().BeTrue();

        // Second PUT uses the original (now stale) version.
        var stalePut = NewPut(registered.Id, registered.Version, "Edit App 3 v3", "Desc v3.");
        var staleResp = await client.SendAsync(stalePut);
        staleResp.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);

        var problem = await staleResp.Content.ReadFromJsonAsync<ProblemPayload>();
        problem!.Type.Should().Be(ProblemTypes.ConcurrencyConflict);
        problem.Extensions.Should().ContainKey("currentVersion");
    }

    [Theory]
    [InlineData("", "Desc.", "displayName")]
    [InlineData("   ", "Desc.", "displayName")]
    [InlineData("DisplayName", "", "description")]
    [InlineData("DisplayName", "  ", "description")]
    public async Task PUT_with_invalid_field_returns_400_with_field_error(string displayName, string description, string expectedErrorField)
    {
        var client = await Fixture.CreateAuthenticatedClientAsync(KartovaApiFixture.OrgAUser);
        var registered = await RegisterAsync(client, "edit-app-4", "Edit App 4", "Desc.");

        var put = NewPut(registered.Id, registered.Version, displayName, description);
        var resp = await client.SendAsync(put);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemPayload>();
        problem!.Type.Should().Be(ProblemTypes.ValidationFailed);
        problem.Errors.Should().ContainKey(expectedErrorField);
    }

    [Fact]
    public async Task PUT_with_over_length_displayName_returns_400()
    {
        var client = await Fixture.CreateAuthenticatedClientAsync(KartovaApiFixture.OrgAUser);
        var registered = await RegisterAsync(client, "edit-app-5", "Edit App 5", "Desc.");

        var put = NewPut(registered.Id, registered.Version, new string('x', 129), "Desc.");
        var resp = await client.SendAsync(put);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemPayload>();
        problem!.Errors.Should().ContainKey("displayName");
    }

    [Fact]
    public async Task PUT_for_other_tenants_id_returns_404()
    {
        var orgAClient = await Fixture.CreateAuthenticatedClientAsync(KartovaApiFixture.OrgAUser);
        var orgARegistered = await RegisterAsync(orgAClient, "edit-app-6", "App", "Desc.");

        var orgBClient = await Fixture.CreateAuthenticatedClientAsync(KartovaApiFixture.OrgBUser);
        var put = NewPut(orgARegistered.Id, orgARegistered.Version, "Hijack", "Hijack.");
        var resp = await orgBClient.SendAsync(put);

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PUT_without_token_returns_401()
    {
        var anon = Fixture.CreateAnonymousClient();
        var put = NewPut(Guid.NewGuid(), VersionEncoding.Encode(0u), "X", "Y");
        var resp = await anon.SendAsync(put);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PUT_on_Decommissioned_application_returns_409()
    {
        // Setup: create + deprecate + decommission an application
        // (Deprecate/Decommission endpoints land in Tasks 12 and 13 — this test
        // is added to the PUT suite for proximity but uses those endpoints
        // once they exist. If running this task alone, the test fails with
        // "endpoint not found" — that's expected; it'll go green at end of Task 13.)
        var client = await Fixture.CreateAuthenticatedClientAsync(KartovaApiFixture.OrgAUser);
        var registered = await RegisterAsync(client, "edit-app-7", "App", "Desc.");

        var deprecate = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/catalog/applications/{registered.Id}/deprecate")
        {
            Content = JsonContent.Create(new { sunsetDate = DateTimeOffset.UtcNow.AddSeconds(1) }),
        };
        var deprecateResp = await client.SendAsync(deprecate);
        deprecateResp.IsSuccessStatusCode.Should().BeTrue();

        // Wait a bit and decommission
        await Task.Delay(2000);
        var decommission = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/catalog/applications/{registered.Id}/decommission");
        var decommissionResp = await client.SendAsync(decommission);
        decommissionResp.IsSuccessStatusCode.Should().BeTrue();

        var current = await decommissionResp.Content.ReadFromJsonAsync<ApplicationResponse>();

        // Now try to edit
        var put = NewPut(registered.Id, current!.Version, "X", "Y");
        var resp = await client.SendAsync(put);

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemPayload>();
        problem!.Type.Should().Be(ProblemTypes.LifecycleConflict);
        problem.Extensions["currentLifecycle"]!.ToString().Should().Be("Decommissioned");
        problem.Extensions["attemptedTransition"]!.ToString().Should().Be("EditMetadata");
    }

    private async Task<ApplicationResponse> RegisterAsync(HttpClient client, string name, string displayName, string description)
    {
        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest(name, displayName, description));
        resp.IsSuccessStatusCode.Should().BeTrue();
        return (await resp.Content.ReadFromJsonAsync<ApplicationResponse>())!;
    }

    private static HttpRequestMessage NewPut(Guid id, string version, string displayName, string description)
    {
        var msg = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/catalog/applications/{id}")
        {
            Content = JsonContent.Create(new EditApplicationRequest(displayName, description)),
        };
        msg.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{version}\""));
        return msg;
    }

    // Helper record for parsing ProblemDetails responses with extensions and validation errors.
    private sealed record ProblemPayload(
        string Type,
        string Title,
        int Status,
        string? Detail,
        Dictionary<string, string[]>? Errors,
        Dictionary<string, object>? Extensions)
    {
        public Dictionary<string, object> Extensions => _extensions ??= new();
        private Dictionary<string, object>? _extensions;
        public Dictionary<string, string[]> Errors => _errors ??= new();
        private Dictionary<string, string[]>? _errors;
    }
}
```

If `KartovaApiFixture.OrgBUser` doesn't exist as a static, mirror what the existing `RegisterApplicationTests` does for the cross-tenant case (likely `Fixture.OrgBUser` instance prop or `KartovaApiFixture.OrgBUser` const). Adjust to match the actual API.

`ProblemPayload` is a local helper. Some test files in the repo define their own — feel free to extract to a shared `tests/Kartova.Testing.Auth/ProblemPayload.cs` if duplication appears in Tasks 12/13.

- [ ] **Step 8: Run the test suite.**

```bash
cmd //c "docker compose up -d postgres keycloak"
cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter \"FullyQualifiedName~EditApplicationTests\" --nologo -v minimal"
```

Expected: 8 tests; **7 PASS** + 1 (`PUT_on_Decommissioned_application_returns_409`) **FAIL** with "endpoint not found" because deprecate/decommission endpoints don't exist yet. The failing test is GREEN by end of Task 13.

If any of the other 7 tests fail, fix before moving on.

- [ ] **Step 9: Commit.**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Contracts/EditApplicationRequest.cs src/Modules/Catalog/Kartova.Catalog.Application/EditApplicationCommand.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/{EditApplicationHandler,CatalogEndpointDelegates,CatalogModule}.cs src/Modules/Catalog/Kartova.Catalog.IntegrationTests/EditApplicationTests.cs
git commit -m "$(cat <<'EOF'
feat(catalog): PUT /applications/{id} — edit metadata (E-02.F-01.S-03)

- EditApplicationRequest DTO + EditApplicationCommand
- EditApplicationHandler sets OriginalValue(Version) so xmin mismatch
  raises DbUpdateConcurrencyException → 412
- Endpoint registered with IfMatchEndpointFilter for header parsing
- Integration tests: happy path, missing/stale If-Match, validation,
  cross-tenant 404. Decommissioned-edit-409 test goes green at end of Task 13.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 12: `DeprecateApplicationCommand` + handler + endpoint + integration tests

**Goal:** Wire `POST /applications/{id}/deprecate` end-to-end. No If-Match (lifecycle endpoints don't take it — domain invariant serializes transitions per spec §3 #7).

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Contracts/DeprecateApplicationRequest.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Application/DeprecateApplicationCommand.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/DeprecateApplicationHandler.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs` (add `DeprecateApplicationAsync`)
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/DeprecateApplicationTests.cs`

- [ ] **Step 1: DTO + command + handler.**

```csharp
// src/Modules/Catalog/Kartova.Catalog.Contracts/DeprecateApplicationRequest.cs
using System.Diagnostics.CodeAnalysis;
namespace Kartova.Catalog.Contracts;

[ExcludeFromCodeCoverage]
public sealed record DeprecateApplicationRequest(DateTimeOffset SunsetDate);
```

```csharp
// src/Modules/Catalog/Kartova.Catalog.Application/DeprecateApplicationCommand.cs
using Kartova.Catalog.Domain;
namespace Kartova.Catalog.Application;

public sealed record DeprecateApplicationCommand(ApplicationId Id, DateTimeOffset SunsetDate);
```

```csharp
// src/Modules/Catalog/Kartova.Catalog.Infrastructure/DeprecateApplicationHandler.cs
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

public sealed class DeprecateApplicationHandler
{
    private readonly TimeProvider _clock;
    public DeprecateApplicationHandler(TimeProvider clock) => _clock = clock;

    public async Task<ApplicationResponse?> Handle(
        DeprecateApplicationCommand cmd,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var app = await db.Applications
            .FirstOrDefaultAsync(ApplicationSortSpecs.IdEquals(cmd.Id.Value), ct);
        if (app is null) return null;

        app.Deprecate(cmd.SunsetDate, _clock);
        await db.SaveChangesAsync(ct);
        return app.ToResponse();
    }
}
```

- [ ] **Step 2: Endpoint delegate.**

Append to `CatalogEndpointDelegates.cs`:

```csharp
    /// <summary>
    /// POST deprecate transitions Active → Deprecated. No If-Match — domain
    /// invariant ("current state must be Active") is the implicit version.
    /// Domain rejection → InvalidLifecycleTransitionException → 409.
    /// </summary>
    internal static async Task<IResult> DeprecateApplicationAsync(
        Guid id,
        [FromBody] DeprecateApplicationRequest request,
        DeprecateApplicationHandler handler,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var resp = await handler.Handle(
            new DeprecateApplicationCommand(new ApplicationId(id), request.SunsetDate),
            db, ct);

        if (resp is null)
        {
            return Results.Problem(
                type: ProblemTypes.ResourceNotFound,
                title: "Application not found",
                detail: "No application with that id is visible in the current tenant.",
                statusCode: StatusCodes.Status404NotFound);
        }
        return Results.Ok(resp);
    }
```

- [ ] **Step 3: Register endpoint + handler + TimeProvider.**

In `CatalogModule.MapEndpoints` after the `EditApplication` registration:

```csharp
        tenant.MapPost("/applications/{id:guid}/deprecate", CatalogEndpointDelegates.DeprecateApplicationAsync)
              .WithName("DeprecateApplication")
              .Produces<ApplicationResponse>(StatusCodes.Status200OK)
              .ProducesProblem(StatusCodes.Status400BadRequest)
              .ProducesProblem(StatusCodes.Status404NotFound)
              .ProducesProblem(StatusCodes.Status409Conflict);
```

In `RegisterServices`:

```csharp
        services.AddScoped<DeprecateApplicationHandler>();
        // TimeProvider — register the system default; tests override via fixture.
        services.TryAddSingleton(TimeProvider.System);
```

(`TryAddSingleton` because other modules may already register; require `using Microsoft.Extensions.DependencyInjection.Extensions;` if not present.)

- [ ] **Step 4: Integration tests.**

```csharp
// src/Modules/Catalog/Kartova.Catalog.IntegrationTests/DeprecateApplicationTests.cs
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;

namespace Kartova.Catalog.IntegrationTests;

public class DeprecateApplicationTests : IClassFixture<KartovaApiFixture>
{
    private readonly KartovaApiFixture Fixture;
    public DeprecateApplicationTests(KartovaApiFixture f) => Fixture = f;

    [Fact]
    public async Task POST_deprecate_with_future_sunsetDate_returns_200_and_sets_lifecycle_and_sunsetDate()
    {
        var client = await Fixture.CreateAuthenticatedClientAsync(KartovaApiFixture.OrgAUser);
        var registered = await RegisterAsync(client, "dep-app-1", "App", "Desc.");
        var sunset = DateTimeOffset.UtcNow.AddDays(30);

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new DeprecateApplicationRequest(sunset));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>();
        body!.Lifecycle.Should().Be(Lifecycle.Deprecated);
        body.SunsetDate.Should().BeCloseTo(sunset, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task POST_deprecate_with_past_sunsetDate_returns_400_with_field_error()
    {
        var client = await Fixture.CreateAuthenticatedClientAsync(KartovaApiFixture.OrgAUser);
        var registered = await RegisterAsync(client, "dep-app-2", "App", "Desc.");

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new DeprecateApplicationRequest(DateTimeOffset.UtcNow.AddDays(-1)));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await resp.Content.ReadFromJsonAsync<EditApplicationTests.ProblemPayload>();
        problem!.Type.Should().Be(ProblemTypes.ValidationFailed);
        problem.Errors.Should().ContainKey("sunsetDate");
    }

    [Fact]
    public async Task POST_deprecate_already_Deprecated_returns_409()
    {
        var client = await Fixture.CreateAuthenticatedClientAsync(KartovaApiFixture.OrgAUser);
        var registered = await RegisterAsync(client, "dep-app-3", "App", "Desc.");

        var first = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new DeprecateApplicationRequest(DateTimeOffset.UtcNow.AddDays(30)));
        first.IsSuccessStatusCode.Should().BeTrue();

        var second = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new DeprecateApplicationRequest(DateTimeOffset.UtcNow.AddDays(60)));

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await second.Content.ReadFromJsonAsync<EditApplicationTests.ProblemPayload>();
        problem!.Type.Should().Be(ProblemTypes.LifecycleConflict);
        problem.Extensions["currentLifecycle"]!.ToString().Should().Be("Deprecated");
    }

    [Fact]
    public async Task POST_deprecate_for_other_tenants_id_returns_404()
    {
        var orgA = await Fixture.CreateAuthenticatedClientAsync(KartovaApiFixture.OrgAUser);
        var registered = await RegisterAsync(orgA, "dep-app-4", "App", "Desc.");

        var orgB = await Fixture.CreateAuthenticatedClientAsync(KartovaApiFixture.OrgBUser);
        var resp = await orgB.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new DeprecateApplicationRequest(DateTimeOffset.UtcNow.AddDays(30)));

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task POST_deprecate_without_token_returns_401()
    {
        var anon = Fixture.CreateAnonymousClient();
        var resp = await anon.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{Guid.NewGuid()}/deprecate",
            new DeprecateApplicationRequest(DateTimeOffset.UtcNow.AddDays(30)));
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<ApplicationResponse> RegisterAsync(HttpClient client, string name, string displayName, string description)
    {
        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest(name, displayName, description));
        resp.IsSuccessStatusCode.Should().BeTrue();
        return (await resp.Content.ReadFromJsonAsync<ApplicationResponse>())!;
    }
}
```

If you extracted `ProblemPayload` to a shared test helper in Task 11, reference it directly.

- [ ] **Step 5: Run.**

```bash
cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter \"FullyQualifiedName~DeprecateApplicationTests\" --nologo -v minimal"
```

Expected: 5 tests PASS.

- [ ] **Step 6: Commit.**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Contracts/DeprecateApplicationRequest.cs src/Modules/Catalog/Kartova.Catalog.Application/DeprecateApplicationCommand.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/{DeprecateApplicationHandler,CatalogEndpointDelegates,CatalogModule}.cs src/Modules/Catalog/Kartova.Catalog.IntegrationTests/DeprecateApplicationTests.cs
git commit -m "$(cat <<'EOF'
feat(catalog): POST /applications/{id}/deprecate (E-02.F-01.S-04 part 1)

ADR-0073 Active → Deprecated transition. No If-Match — domain invariant
\"current state must be Active\" is the implicit version. Past sunsetDate
→ 400 ValidationProblemDetails. Wrong source state → 409 LifecycleConflict
with currentLifecycle + attemptedTransition extensions.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 13: `DecommissionApplicationCommand` + handler + endpoint + integration tests

**Goal:** Wire `POST /applications/{id}/decommission` end-to-end. Empty body. The before-sunset-date 409 path is unique to this transition.

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Application/DecommissionApplicationCommand.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/DecommissionApplicationHandler.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/DecommissionApplicationTests.cs`

- [ ] **Step 1: Command + handler.**

```csharp
// src/Modules/Catalog/Kartova.Catalog.Application/DecommissionApplicationCommand.cs
using Kartova.Catalog.Domain;
namespace Kartova.Catalog.Application;
public sealed record DecommissionApplicationCommand(ApplicationId Id);
```

```csharp
// src/Modules/Catalog/Kartova.Catalog.Infrastructure/DecommissionApplicationHandler.cs
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

public sealed class DecommissionApplicationHandler
{
    private readonly TimeProvider _clock;
    public DecommissionApplicationHandler(TimeProvider clock) => _clock = clock;

    public async Task<ApplicationResponse?> Handle(
        DecommissionApplicationCommand cmd, CatalogDbContext db, CancellationToken ct)
    {
        var app = await db.Applications
            .FirstOrDefaultAsync(ApplicationSortSpecs.IdEquals(cmd.Id.Value), ct);
        if (app is null) return null;

        app.Decommission(_clock);
        await db.SaveChangesAsync(ct);
        return app.ToResponse();
    }
}
```

- [ ] **Step 2: Endpoint delegate.**

Append to `CatalogEndpointDelegates.cs`:

```csharp
    /// <summary>
    /// POST decommission transitions Deprecated → Decommissioned. No body, no
    /// If-Match. Source-state mismatch or before-sunset-date → 409.
    /// </summary>
    internal static async Task<IResult> DecommissionApplicationAsync(
        Guid id,
        DecommissionApplicationHandler handler,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var resp = await handler.Handle(
            new DecommissionApplicationCommand(new ApplicationId(id)), db, ct);

        if (resp is null)
        {
            return Results.Problem(
                type: ProblemTypes.ResourceNotFound,
                title: "Application not found",
                detail: "No application with that id is visible in the current tenant.",
                statusCode: StatusCodes.Status404NotFound);
        }
        return Results.Ok(resp);
    }
```

- [ ] **Step 3: Register endpoint + handler.**

In `CatalogModule.MapEndpoints` after `DeprecateApplication`:

```csharp
        tenant.MapPost("/applications/{id:guid}/decommission", CatalogEndpointDelegates.DecommissionApplicationAsync)
              .WithName("DecommissionApplication")
              .Produces<ApplicationResponse>(StatusCodes.Status200OK)
              .ProducesProblem(StatusCodes.Status404NotFound)
              .ProducesProblem(StatusCodes.Status409Conflict);
```

In `RegisterServices`:

```csharp
        services.AddScoped<DecommissionApplicationHandler>();
```

- [ ] **Step 4: Integration tests.**

```csharp
// src/Modules/Catalog/Kartova.Catalog.IntegrationTests/DecommissionApplicationTests.cs
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;

namespace Kartova.Catalog.IntegrationTests;

public class DecommissionApplicationTests : IClassFixture<KartovaApiFixture>
{
    private readonly KartovaApiFixture Fixture;
    public DecommissionApplicationTests(KartovaApiFixture f) => Fixture = f;

    [Fact]
    public async Task POST_decommission_when_Deprecated_and_past_sunsetDate_returns_200_and_sets_lifecycle_to_Decommissioned()
    {
        var client = await Fixture.CreateAuthenticatedClientAsync(KartovaApiFixture.OrgAUser);
        var registered = await RegisterAsync(client, "dec-app-1", "App", "Desc.");

        // Deprecate with sunsetDate in the past + 1 second future to satisfy
        // the strict-future invariant on Deprecate, then sleep past it.
        var sunset = DateTimeOffset.UtcNow.AddSeconds(1);
        var deprecate = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new DeprecateApplicationRequest(sunset));
        deprecate.IsSuccessStatusCode.Should().BeTrue();

        await Task.Delay(2000);

        var resp = await client.PostAsync(
            $"/api/v1/catalog/applications/{registered.Id}/decommission", null);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>();
        body!.Lifecycle.Should().Be(Lifecycle.Decommissioned);
        body.SunsetDate.Should().NotBeNull();
    }

    [Fact]
    public async Task POST_decommission_when_Deprecated_and_before_sunsetDate_returns_409_with_reason_before_sunset_date()
    {
        var client = await Fixture.CreateAuthenticatedClientAsync(KartovaApiFixture.OrgAUser);
        var registered = await RegisterAsync(client, "dec-app-2", "App", "Desc.");

        var deprecate = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new DeprecateApplicationRequest(DateTimeOffset.UtcNow.AddDays(30)));
        deprecate.IsSuccessStatusCode.Should().BeTrue();

        var resp = await client.PostAsync(
            $"/api/v1/catalog/applications/{registered.Id}/decommission", null);

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await resp.Content.ReadFromJsonAsync<EditApplicationTests.ProblemPayload>();
        problem!.Type.Should().Be(ProblemTypes.LifecycleConflict);
        problem.Extensions["reason"]!.ToString().Should().Be("before-sunset-date");
        problem.Extensions["sunsetDate"].Should().NotBeNull();
    }

    [Fact]
    public async Task POST_decommission_when_Active_returns_409()
    {
        var client = await Fixture.CreateAuthenticatedClientAsync(KartovaApiFixture.OrgAUser);
        var registered = await RegisterAsync(client, "dec-app-3", "App", "Desc.");

        var resp = await client.PostAsync(
            $"/api/v1/catalog/applications/{registered.Id}/decommission", null);

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await resp.Content.ReadFromJsonAsync<EditApplicationTests.ProblemPayload>();
        problem!.Extensions["currentLifecycle"]!.ToString().Should().Be("Active");
    }

    [Fact]
    public async Task POST_decommission_when_already_Decommissioned_returns_409()
    {
        var client = await Fixture.CreateAuthenticatedClientAsync(KartovaApiFixture.OrgAUser);
        var registered = await RegisterAsync(client, "dec-app-4", "App", "Desc.");

        var sunset = DateTimeOffset.UtcNow.AddSeconds(1);
        await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new DeprecateApplicationRequest(sunset));
        await Task.Delay(2000);
        await client.PostAsync($"/api/v1/catalog/applications/{registered.Id}/decommission", null);

        var resp = await client.PostAsync(
            $"/api/v1/catalog/applications/{registered.Id}/decommission", null);

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await resp.Content.ReadFromJsonAsync<EditApplicationTests.ProblemPayload>();
        problem!.Extensions["currentLifecycle"]!.ToString().Should().Be("Decommissioned");
    }

    [Fact]
    public async Task POST_decommission_for_other_tenants_id_returns_404()
    {
        var orgA = await Fixture.CreateAuthenticatedClientAsync(KartovaApiFixture.OrgAUser);
        var registered = await RegisterAsync(orgA, "dec-app-5", "App", "Desc.");

        var orgB = await Fixture.CreateAuthenticatedClientAsync(KartovaApiFixture.OrgBUser);
        var resp = await orgB.PostAsync(
            $"/api/v1/catalog/applications/{registered.Id}/decommission", null);
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<ApplicationResponse> RegisterAsync(HttpClient client, string name, string displayName, string description)
    {
        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest(name, displayName, description));
        resp.IsSuccessStatusCode.Should().BeTrue();
        return (await resp.Content.ReadFromJsonAsync<ApplicationResponse>())!;
    }
}
```

- [ ] **Step 5: Run.**

```bash
cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter \"FullyQualifiedName~DecommissionApplicationTests\" --nologo -v minimal"
```

Expected: 5 tests PASS.

- [ ] **Step 6: Re-run the deferred Edit-on-Decommissioned test from Task 11.**

```bash
cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter \"FullyQualifiedName~EditApplicationTests.PUT_on_Decommissioned_application_returns_409\" --nologo -v minimal"
```

Expected: PASS now (deprecate + decommission endpoints exist).

- [ ] **Step 7: Commit.**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Application/DecommissionApplicationCommand.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/{DecommissionApplicationHandler,CatalogEndpointDelegates,CatalogModule}.cs src/Modules/Catalog/Kartova.Catalog.IntegrationTests/DecommissionApplicationTests.cs
git commit -m "$(cat <<'EOF'
feat(catalog): POST /applications/{id}/decommission (E-02.F-01.S-04 part 2)

ADR-0073 Deprecated → Decommissioned transition with strict
\"now >= sunsetDate\" enforcement (admin override deferred to RBAC slice
§13.2). Wrong source state → 409 with currentLifecycle. Before-sunset →
409 with reason=before-sunset-date and sunsetDate extension.

Closes the deferred Edit-on-Decommissioned 409 test from Task 11.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 14: Extend `EndpointRouteRules` arch test for the three new named routes

**Goal:** The slice-3 §13.9 endpoint inventory test asserts every expected named route exists. Add the three new routes to its inventory so future `MapPut(...) → ;` / `MapPost(...) → ;` mutants are killed by the arch test.

**Files:**
- Modify: `tests/Kartova.ArchitectureTests/EndpointRouteRules.cs`

- [ ] **Step 1: Locate the existing inventory.**

Open `tests/Kartova.ArchitectureTests/EndpointRouteRules.cs`. Find the data array (likely a `static readonly` `(string verb, string template, string name)[]` field) and the test that walks it.

- [ ] **Step 2: Add three rows for slice 5.**

```csharp
            // Slice 5 — applications edit + lifecycle transitions
            ("PUT",  "/api/v1/catalog/applications/{id:guid}",                "EditApplication"),
            ("POST", "/api/v1/catalog/applications/{id:guid}/deprecate",      "DeprecateApplication"),
            ("POST", "/api/v1/catalog/applications/{id:guid}/decommission",   "DecommissionApplication"),
```

The exact field name and tuple shape is whatever the file already uses — match it.

- [ ] **Step 3: Run the arch tests.**

```bash
cmd //c "dotnet test tests/Kartova.ArchitectureTests --filter \"FullyQualifiedName~EndpointRouteRules\" --nologo -v minimal"
```

Expected: PASS for both inventory checks (named routes exist; every endpoint has a name; names are unique).

- [ ] **Step 4: Run the full arch suite.**

```bash
cmd //c "dotnet test tests/Kartova.ArchitectureTests --nologo -v minimal"
```

Expected: all arch tests PASS.

- [ ] **Step 5: Commit.**

```bash
git add tests/Kartova.ArchitectureTests/EndpointRouteRules.cs
git commit -m "$(cat <<'EOF'
test(arch): EndpointRouteRules inventory includes slice-5 named routes

EditApplication (PUT) + DeprecateApplication / DecommissionApplication
(POST) added to the named-route inventory. Kills MapPut/MapPost(...) → ;
mutants in CatalogModule.MapEndpoints.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 15: Backend full-suite verification before SPA work

**Goal:** Make sure the backend is fully green before flipping to the SPA. SPA codegen reads the live OpenAPI doc and any drift here surfaces during `npm run dev`.

- [ ] **Step 1: Full backend build clean.**

```bash
cmd //c "dotnet build Kartova.slnx -c Debug --nologo -v minimal"
```

Expected: 0 warnings, 0 errors.

- [ ] **Step 2: Full unit + arch test suite.**

```bash
cmd //c "dotnet test Kartova.slnx --no-build --filter \"FullyQualifiedName!~IntegrationTests\" --nologo -v minimal"
```

Expected: every test passes.

- [ ] **Step 3: Full integration test suite.**

```bash
cmd //c "docker compose up -d postgres keycloak"
cmd //c "dotnet test Kartova.slnx --no-build --filter \"FullyQualifiedName~IntegrationTests\" --nologo -v minimal"
```

Expected: every integration test passes (slice-3 + slice-4 + new slice-5 suites).

- [ ] **Step 4: API host boots.**

```bash
cmd //c "docker compose up --build -d api"
sleep 5
curl -s http://localhost:5080/api/v1/version | head -c 100
```

Expected: JSON response (not an error). If failing, check `docker compose logs api`.

- [ ] **Step 5: OpenAPI document includes new endpoints.**

```bash
curl -s http://localhost:5080/openapi/v1.json | grep -E "EditApplication|DeprecateApplication|DecommissionApplication"
```

Expected: three operationId mentions in the output.

- [ ] **Step 6: Commit (no code change — placeholder for verification milestone).**

No commit needed — this task is purely a verification gate. If anything fails, fix it before starting Task 16.

---

## Task 16: SPA — OpenAPI codegen + new mutation hooks

**Goal:** Regenerate the typed OpenAPI client to surface the three new operations (`editApplication`, `deprecateApplication`, `decommissionApplication`), then add three TanStack Query mutation hooks plus a `LifecycleBadge`-friendly type alias for the `Lifecycle` enum.

**Files:**
- Auto-generated: `web/src/generated/openapi.ts` (gitignored — regenerated each `npm run codegen`)
- Modify: `web/src/features/catalog/api/applications.ts` (add mutation hooks)
- Modify: `web/src/features/catalog/api/__tests__/applications.test.ts` (or create if not present — Vitest hook coverage)

- [ ] **Step 1: Regenerate the OpenAPI client.**

```bash
cd web && npm run codegen && cd ..
```

Expected: `web/src/generated/openapi.ts` updated. Verify by grepping for the new operations:

```bash
grep -E "EditApplication|DeprecateApplication|DecommissionApplication" web/src/generated/openapi.ts | head -10
```

Expected: matches showing the operation IDs.

- [ ] **Step 2: Add mutation hooks to `applications.ts`.**

Append to `web/src/features/catalog/api/applications.ts`:

```typescript
import type { EditApplicationInput } from "../schemas/editApplication";
import type { DeprecateApplicationInput } from "../schemas/deprecateApplication";

type Lifecycle = ApplicationResponse["lifecycle"];

/**
 * PUT /applications/{id} — full-replacement edit (ADR-0096). If-Match header
 * carries the optimistic concurrency token from the cached version. On 412
 * the caller is expected to refetch and reapply.
 */
export function useEditApplication(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: { values: EditApplicationInput; expectedVersion: string }) => {
      const { data, error, response } = await apiClient.PUT(
        "/api/v1/catalog/applications/{id}",
        {
          params: { path: { id } },
          body: input.values,
          headers: { "If-Match": `"${input.expectedVersion}"` },
        }
      );
      if (error) {
        // Attach status so the dialog can branch on 412 / 409 / 400.
        const enriched = error as Record<string, unknown>;
        enriched.__status = response.status;
        throw enriched;
      }
      if (!data) throw new Error("API returned neither data nor error");
      return data;
    },
    onSuccess: (data) => {
      qc.setQueryData(applicationKeys.detail(id), data);
      qc.invalidateQueries({ queryKey: applicationKeys.all });
    },
  });
}

/**
 * POST /applications/{id}/deprecate — Active → Deprecated. No If-Match.
 * 409 LifecycleConflict on wrong source state.
 */
export function useDeprecateApplication(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: DeprecateApplicationInput) => {
      const { data, error, response } = await apiClient.POST(
        "/api/v1/catalog/applications/{id}/deprecate",
        { params: { path: { id } }, body: input }
      );
      if (error) {
        const enriched = error as Record<string, unknown>;
        enriched.__status = response.status;
        throw enriched;
      }
      if (!data) throw new Error("API returned neither data nor error");
      return data;
    },
    onSuccess: (data) => {
      qc.setQueryData(applicationKeys.detail(id), data);
      qc.invalidateQueries({ queryKey: applicationKeys.all });
    },
  });
}

/**
 * POST /applications/{id}/decommission — Deprecated → Decommissioned.
 * No body, no If-Match. 409 with reason=before-sunset-date when called early.
 */
export function useDecommissionApplication(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      const { data, error, response } = await apiClient.POST(
        "/api/v1/catalog/applications/{id}/decommission",
        { params: { path: { id } } }
      );
      if (error) {
        const enriched = error as Record<string, unknown>;
        enriched.__status = response.status;
        throw enriched;
      }
      if (!data) throw new Error("API returned neither data nor error");
      return data;
    },
    onSuccess: (data) => {
      qc.setQueryData(applicationKeys.detail(id), data);
      qc.invalidateQueries({ queryKey: applicationKeys.all });
    },
  });
}

export type { ApplicationResponse, Lifecycle };
```

The two `import type` lines at the top reference schemas that land in Task 17. If you're running this task in isolation, the TS check fails until those exist — that's expected. We'll verify at the end of Task 17.

- [ ] **Step 3: Vitest coverage of the mutation hooks.**

Create `web/src/features/catalog/api/__tests__/editApplication.test.ts`:

```typescript
import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useEditApplication, applicationKeys } from "../applications";
import { apiClient } from "../client";

vi.mock("../client", () => ({
  apiClient: {
    PUT: vi.fn(),
    POST: vi.fn(),
    GET: vi.fn(),
  },
}));

function wrapper({ children }: { children: ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
}

describe("useEditApplication", () => {
  it("PUTs with If-Match header and invalidates queries on success", async () => {
    const fakeResponse = { id: "abc", displayName: "X", description: "Y", lifecycle: "Active", sunsetDate: null, version: "v2" };
    vi.mocked(apiClient.PUT).mockResolvedValue({
      data: fakeResponse,
      error: undefined,
      response: { status: 200 } as Response,
    });

    const { result } = renderHook(() => useEditApplication("abc"), { wrapper });
    await result.current.mutateAsync({
      values: { displayName: "X", description: "Y" },
      expectedVersion: "v1",
    });

    expect(apiClient.PUT).toHaveBeenCalledWith(
      "/api/v1/catalog/applications/{id}",
      expect.objectContaining({
        params: { path: { id: "abc" } },
        body: { displayName: "X", description: "Y" },
        headers: { "If-Match": '"v1"' },
      })
    );
  });

  it("attaches __status on error so the dialog can branch", async () => {
    vi.mocked(apiClient.PUT).mockResolvedValue({
      data: undefined,
      error: { type: "https://kartova.io/problems/concurrency-conflict" },
      response: { status: 412 } as Response,
    });

    const { result } = renderHook(() => useEditApplication("abc"), { wrapper });

    await waitFor(async () => {
      try {
        await result.current.mutateAsync({
          values: { displayName: "X", description: "Y" },
          expectedVersion: "v1",
        });
        throw new Error("should have thrown");
      } catch (err: any) {
        expect(err.__status).toBe(412);
      }
    });
  });
});
```

Mirror this for `deprecateApplication.test.ts` and `decommissionApplication.test.ts` — same shape, different operation. Keep them tight.

- [ ] **Step 4: Run Vitest.**

```bash
cd web && npm run test --run -- features/catalog/api/__tests__ && cd ..
```

Expected: all hook tests pass.

- [ ] **Step 5: TypeScript clean.**

```bash
cd web && npm run typecheck && cd ..
```

Expected: zero TS errors. If the schemas Task 17 produces aren't ready yet, expect a single failure on the `import type { EditApplicationInput }` line — proceed to Task 17 to clear it.

- [ ] **Step 6: Commit.**

```bash
git add web/src/features/catalog/api/applications.ts web/src/features/catalog/api/__tests__/
git commit -m "$(cat <<'EOF'
feat(web): TanStack Query mutation hooks — edit/deprecate/decommission

useEditApplication attaches If-Match from cached version; surfaces __status
on error so dialogs can branch on 412 / 409 / 400. Lifecycle hooks invalidate
detail + list caches on success.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 17: SPA — schemas + `LifecycleBadge` component

**Goal:** Two zod schemas (`editApplication`, `deprecateApplication`) and the shared `LifecycleBadge` component used everywhere lifecycle is rendered (detail header, list cell, register dialog).

**Files:**
- Create: `web/src/features/catalog/schemas/editApplication.ts`
- Create: `web/src/features/catalog/schemas/deprecateApplication.ts`
- Create: `web/src/features/catalog/components/LifecycleBadge.tsx`
- Modify: `web/src/features/catalog/components/RegisterApplicationDialog.tsx` (replace hardcoded "Active" pill)
- Create: `web/src/features/catalog/schemas/__tests__/editApplication.test.ts`
- Create: `web/src/features/catalog/schemas/__tests__/deprecateApplication.test.ts`

- [ ] **Step 1: Create `editApplicationSchema`.**

```typescript
// web/src/features/catalog/schemas/editApplication.ts
import { z } from "zod";

export const editApplicationSchema = z.object({
  displayName: z
    .string()
    .min(1, "Display name is required.")
    .max(128, "Display name must be 128 characters or fewer.")
    .refine((v) => v.trim().length > 0, { message: "Display name must not be only whitespace." }),
  description: z
    .string()
    .min(1, "Description is required.")
    .refine((v) => v.trim().length > 0, { message: "Description must not be only whitespace." }),
});

export type EditApplicationInput = z.infer<typeof editApplicationSchema>;
```

- [ ] **Step 2: Create `deprecateApplicationSchema`.**

```typescript
// web/src/features/catalog/schemas/deprecateApplication.ts
import { z } from "zod";

export const deprecateApplicationSchema = z.object({
  sunsetDate: z
    .string()
    .min(1, "Sunset date is required.")
    .refine((v) => {
      const d = new Date(v);
      return !Number.isNaN(d.getTime()) && d.getTime() > Date.now();
    }, { message: "Sunset date must be in the future." }),
});

export type DeprecateApplicationInput = z.infer<typeof deprecateApplicationSchema>;
```

- [ ] **Step 3: Create `LifecycleBadge`.**

```tsx
// web/src/features/catalog/components/LifecycleBadge.tsx
import { Badge } from "@/components/base/badges/badges";
import type { Lifecycle } from "@/features/catalog/api/applications";

export interface LifecycleBadgeProps {
  lifecycle: Lifecycle;
  sunsetDate?: string | null;
  size?: "sm" | "md";
  showSunsetSubline?: boolean;       // detail page: yes; list cell: no
}

const COLOR: Record<Lifecycle, "success" | "warning" | "gray"> = {
  Active: "success",
  Deprecated: "warning",
  Decommissioned: "gray",
};

const LABEL: Record<Lifecycle, string> = {
  Active: "Active",
  Deprecated: "Deprecated",
  Decommissioned: "Decommissioned",
};

export function LifecycleBadge({
  lifecycle,
  sunsetDate,
  size = "sm",
  showSunsetSubline = false,
}: LifecycleBadgeProps) {
  return (
    <span className="inline-flex flex-col items-start gap-0.5">
      <Badge color={COLOR[lifecycle]} type="pill-color" size={size}>
        {LABEL[lifecycle]}
      </Badge>
      {showSunsetSubline && lifecycle === "Deprecated" && sunsetDate && (
        <span className="text-xs text-tertiary">
          Sunset: {new Date(sunsetDate).toLocaleDateString()}
        </span>
      )}
    </span>
  );
}
```

- [ ] **Step 4: Adopt `LifecycleBadge` in `RegisterApplicationDialog`.**

Open `web/src/features/catalog/components/RegisterApplicationDialog.tsx`. Find the hardcoded "Active" `Badge` element and replace with:

```tsx
<LifecycleBadge lifecycle="Active" />
```

Add `import { LifecycleBadge } from "./LifecycleBadge";` at the top.

- [ ] **Step 5: Schema unit tests.**

```typescript
// web/src/features/catalog/schemas/__tests__/editApplication.test.ts
import { describe, expect, it } from "vitest";
import { editApplicationSchema } from "../editApplication";

describe("editApplicationSchema", () => {
  it("accepts valid input", () => {
    expect(editApplicationSchema.safeParse({ displayName: "X", description: "Y" }).success).toBe(true);
  });

  it("rejects empty displayName", () => {
    expect(editApplicationSchema.safeParse({ displayName: "", description: "Y" }).success).toBe(false);
  });

  it("rejects whitespace-only displayName", () => {
    expect(editApplicationSchema.safeParse({ displayName: "   ", description: "Y" }).success).toBe(false);
  });

  it("rejects displayName over 128 chars", () => {
    expect(editApplicationSchema.safeParse({ displayName: "x".repeat(129), description: "Y" }).success).toBe(false);
  });

  it("rejects empty description", () => {
    expect(editApplicationSchema.safeParse({ displayName: "X", description: "" }).success).toBe(false);
  });

  it("rejects whitespace-only description", () => {
    expect(editApplicationSchema.safeParse({ displayName: "X", description: "  " }).success).toBe(false);
  });
});
```

```typescript
// web/src/features/catalog/schemas/__tests__/deprecateApplication.test.ts
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { deprecateApplicationSchema } from "../deprecateApplication";

describe("deprecateApplicationSchema", () => {
  beforeEach(() => vi.useFakeTimers().setSystemTime(new Date("2026-05-06T12:00:00Z")));
  afterEach(() => vi.useRealTimers());

  it("accepts a future ISO date", () => {
    expect(deprecateApplicationSchema.safeParse({ sunsetDate: "2026-12-31T00:00:00Z" }).success).toBe(true);
  });

  it("rejects a past ISO date", () => {
    expect(deprecateApplicationSchema.safeParse({ sunsetDate: "2026-01-01T00:00:00Z" }).success).toBe(false);
  });

  it("rejects an unparseable string", () => {
    expect(deprecateApplicationSchema.safeParse({ sunsetDate: "not-a-date" }).success).toBe(false);
  });

  it("rejects an empty string", () => {
    expect(deprecateApplicationSchema.safeParse({ sunsetDate: "" }).success).toBe(false);
  });
});
```

- [ ] **Step 6: Run Vitest.**

```bash
cd web && npm run test --run -- features/catalog/schemas && cd ..
```

Expected: 10 tests pass.

- [ ] **Step 7: TypeScript + lint clean.**

```bash
cd web && npm run typecheck && npm run lint && cd ..
```

Expected: clean. The Task 16 `import type` issues should now resolve.

- [ ] **Step 8: Commit.**

```bash
git add web/src/features/catalog/schemas/ web/src/features/catalog/components/LifecycleBadge.tsx web/src/features/catalog/components/RegisterApplicationDialog.tsx
git commit -m "$(cat <<'EOF'
feat(web): editApplicationSchema + deprecateApplicationSchema + LifecycleBadge

zod schemas mirror domain invariants (displayName 1-128 non-whitespace,
description non-whitespace, sunsetDate strictly future). LifecycleBadge
unifies the pill rendering across detail header, list cell, and register
dialog (replaces hardcoded \"Active\" Badge in RegisterApplicationDialog).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 18: SPA — `EditApplicationDialog`

**Goal:** Modal dialog for editing displayName + description. Mirrors `RegisterApplicationDialog` shape. Wires `useEditApplication`. Maps 400/409/412 to UX per spec §8.3.

**Files:**
- Create: `web/src/features/catalog/components/EditApplicationDialog.tsx`
- Create: `web/src/features/catalog/components/__tests__/EditApplicationDialog.test.tsx`

- [ ] **Step 1: Create the component.**

```tsx
// web/src/features/catalog/components/EditApplicationDialog.tsx
import { useEffect } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { toast } from "sonner";
import { Modal } from "@/components/application/modals/modal";
import { Button } from "@/components/base/buttons/button";
import { Input } from "@/components/base/input/input";
import { TextArea } from "@/components/base/textarea/textarea";
import { Form } from "@/components/base/form/form";
import { applyProblemDetailsToForm } from "@/lib/problem-details/apply-problem-details-to-form";
import {
  editApplicationSchema,
  type EditApplicationInput,
} from "@/features/catalog/schemas/editApplication";
import { useEditApplication, type ApplicationResponse } from "@/features/catalog/api/applications";

export interface EditApplicationDialogProps {
  application: ApplicationResponse;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function EditApplicationDialog({ application, open, onOpenChange }: EditApplicationDialogProps) {
  const form = useForm<EditApplicationInput>({
    resolver: zodResolver(editApplicationSchema),
    defaultValues: {
      displayName: application.displayName,
      description: application.description,
    },
  });

  // Re-sync defaults when the application prop changes (e.g. after 412 refetch).
  useEffect(() => {
    form.reset({ displayName: application.displayName, description: application.description });
  }, [application.displayName, application.description, form]);

  const mutation = useEditApplication(application.id);

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      await mutation.mutateAsync({ values, expectedVersion: application.version });
      toast.success("Application updated");
      onOpenChange(false);
    } catch (err: any) {
      const status = err?.__status as number | undefined;
      const handled = applyProblemDetailsToForm(err, form);
      if (handled) return;                  // 400 — field errors set

      if (status === 412) {
        toast.error("Someone else edited this. Reloaded latest values.");
        // The hook's onSuccess won't fire on 412; manually re-fetch by leaving
        // dialog open. The detail page's useApplication query refreshes via
        // its query-key invalidation in our outer flow.
        return;
      }
      if (status === 409) {
        toast.error("This application has been decommissioned and can no longer be edited.");
        onOpenChange(false);
        return;
      }
      toast.error("Could not update application");
    }
  });

  return (
    <Modal isOpen={open} onOpenChange={onOpenChange} aria-label="Edit application">
      <Form form={form} onSubmit={onSubmit}>
        <Modal.Header>
          <Modal.Title>Edit application</Modal.Title>
          <Modal.Description>
            Update the display name and description. Name (slug) and ownership are not editable here.
          </Modal.Description>
        </Modal.Header>

        <Modal.Body className="space-y-4">
          <Input
            label="Display name"
            placeholder="Payment Gateway"
            {...form.register("displayName")}
            isInvalid={!!form.formState.errors.displayName}
            errorMessage={form.formState.errors.displayName?.message}
          />
          <TextArea
            label="Description"
            placeholder="What this application does"
            {...form.register("description")}
            isInvalid={!!form.formState.errors.description}
            errorMessage={form.formState.errors.description?.message}
          />
        </Modal.Body>

        <Modal.Footer>
          <Button type="button" color="secondary" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button type="submit" isLoading={mutation.isPending}>
            Save changes
          </Button>
        </Modal.Footer>
      </Form>
    </Modal>
  );
}
```

The exact import paths for `Modal`, `Button`, `Input`, `TextArea`, `Form` and the Untitled UI primitives mirror the existing `RegisterApplicationDialog`. If your repo uses different paths or component names, mirror that file's imports — don't invent.

- [ ] **Step 2: Vitest for the dialog.**

```tsx
// web/src/features/catalog/components/__tests__/EditApplicationDialog.test.tsx
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { EditApplicationDialog } from "../EditApplicationDialog";
import { apiClient } from "@/features/catalog/api/client";

vi.mock("@/features/catalog/api/client", () => ({
  apiClient: { PUT: vi.fn(), POST: vi.fn(), GET: vi.fn() },
}));

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

function wrapper({ children }: { children: ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
}

const baseApp = {
  id: "abc",
  tenantId: "t1",
  name: "payments",
  displayName: "Payments",
  description: "Payments service.",
  ownerUserId: "u1",
  createdAt: "2026-04-30T00:00:00Z",
  lifecycle: "Active" as const,
  sunsetDate: null,
  version: "v1",
};

describe("EditApplicationDialog", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("pre-fills form from application data and submits PUT with If-Match", async () => {
    vi.mocked(apiClient.PUT).mockResolvedValue({
      data: { ...baseApp, displayName: "New", description: "New description.", version: "v2" },
      error: undefined,
      response: { status: 200 } as Response,
    });

    const onOpenChange = vi.fn();
    render(<EditApplicationDialog application={baseApp} open onOpenChange={onOpenChange} />, { wrapper });

    expect(screen.getByLabelText(/display name/i)).toHaveValue("Payments");

    await userEvent.clear(screen.getByLabelText(/display name/i));
    await userEvent.type(screen.getByLabelText(/display name/i), "New");
    await userEvent.click(screen.getByRole("button", { name: /save/i }));

    await waitFor(() => {
      expect(apiClient.PUT).toHaveBeenCalledWith(
        "/api/v1/catalog/applications/{id}",
        expect.objectContaining({ headers: { "If-Match": '"v1"' } })
      );
      expect(onOpenChange).toHaveBeenCalledWith(false);
    });
  });

  it("on 412 keeps dialog open and toasts", async () => {
    vi.mocked(apiClient.PUT).mockResolvedValue({
      data: undefined,
      error: { type: "https://kartova.io/problems/concurrency-conflict" },
      response: { status: 412 } as Response,
    });

    const onOpenChange = vi.fn();
    render(<EditApplicationDialog application={baseApp} open onOpenChange={onOpenChange} />, { wrapper });

    await userEvent.click(screen.getByRole("button", { name: /save/i }));

    await waitFor(() => {
      expect(onOpenChange).not.toHaveBeenCalledWith(false);
    });
  });
});
```

- [ ] **Step 3: Run Vitest.**

```bash
cd web && npm run test --run -- features/catalog/components/__tests__/EditApplicationDialog && cd ..
```

Expected: tests pass.

- [ ] **Step 4: TypeScript + lint clean.**

```bash
cd web && npm run typecheck && npm run lint && cd ..
```

Expected: clean.

- [ ] **Step 5: Commit.**

```bash
git add web/src/features/catalog/components/EditApplicationDialog.tsx web/src/features/catalog/components/__tests__/EditApplicationDialog.test.tsx
git commit -m "$(cat <<'EOF'
feat(web): EditApplicationDialog (E-02.F-01.S-03 — UI)

Modal mirrors RegisterApplicationDialog shape. Pre-fills from current
application; on 412 keeps dialog open + toasts; on 409 (Decommissioned)
toasts and closes; on 400 routes via applyProblemDetailsToForm.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 19: SPA — `LifecycleMenu` + `DeprecateConfirmDialog` + `DecommissionConfirmDialog`

**Goal:** State-aware dropdown anchored on the lifecycle Badge, plus the two confirmation dialogs each transition triggers.

**Files:**
- Create: `web/src/features/catalog/components/LifecycleMenu.tsx`
- Create: `web/src/features/catalog/components/DeprecateConfirmDialog.tsx`
- Create: `web/src/features/catalog/components/DecommissionConfirmDialog.tsx`
- Create: `web/src/features/catalog/components/__tests__/LifecycleMenu.test.tsx`

- [ ] **Step 1: Create `LifecycleMenu`.**

```tsx
// web/src/features/catalog/components/LifecycleMenu.tsx
import { useState } from "react";
import { MenuTrigger } from "react-aria-components";
import { Menu, MenuItem } from "@/components/base/menu/menu";
import { Button } from "@/components/base/buttons/button";
import { LifecycleBadge } from "./LifecycleBadge";
import { DeprecateConfirmDialog } from "./DeprecateConfirmDialog";
import { DecommissionConfirmDialog } from "./DecommissionConfirmDialog";
import type { ApplicationResponse } from "@/features/catalog/api/applications";

export interface LifecycleMenuProps {
  application: ApplicationResponse;
}

export function LifecycleMenu({ application }: LifecycleMenuProps) {
  const [openDialog, setOpenDialog] = useState<"deprecate" | "decommission" | null>(null);

  // Decommissioned: badge is non-interactive, no menu rendered.
  if (application.lifecycle === "Decommissioned") {
    return <LifecycleBadge lifecycle={application.lifecycle} sunsetDate={application.sunsetDate} showSunsetSubline />;
  }

  const items: { id: string; label: string; action: "deprecate" | "decommission"; disabled?: boolean; tooltip?: string }[] = [];
  const now = Date.now();

  if (application.lifecycle === "Active") {
    items.push({ id: "deprecate", label: "Deprecate…", action: "deprecate" });
  }
  if (application.lifecycle === "Deprecated") {
    const sunsetMs = application.sunsetDate ? new Date(application.sunsetDate).getTime() : 0;
    const canDecommission = now >= sunsetMs;
    items.push({
      id: "decommission",
      label: "Decommission",
      action: "decommission",
      disabled: !canDecommission,
      tooltip: canDecommission ? undefined : `Available after ${new Date(sunsetMs).toLocaleDateString()}`,
    });
  }

  return (
    <>
      <MenuTrigger>
        <Button color="link-color" aria-label="Lifecycle actions">
          <LifecycleBadge lifecycle={application.lifecycle} sunsetDate={application.sunsetDate} showSunsetSubline />
          <span aria-hidden="true" className="ml-1">▾</span>
        </Button>
        <Menu>
          {items.map((item) => (
            <MenuItem
              key={item.id}
              isDisabled={item.disabled}
              onAction={() => setOpenDialog(item.action)}
            >
              <span title={item.tooltip}>{item.label}</span>
            </MenuItem>
          ))}
        </Menu>
      </MenuTrigger>

      {openDialog === "deprecate" && (
        <DeprecateConfirmDialog
          application={application}
          open
          onOpenChange={(o) => !o && setOpenDialog(null)}
        />
      )}
      {openDialog === "decommission" && (
        <DecommissionConfirmDialog
          application={application}
          open
          onOpenChange={(o) => !o && setOpenDialog(null)}
        />
      )}
    </>
  );
}
```

The exact `Menu` / `MenuItem` / `MenuTrigger` import paths follow the existing Untitled UI repo layout — match neighboring usages if they differ.

- [ ] **Step 2: Create `DeprecateConfirmDialog`.**

```tsx
// web/src/features/catalog/components/DeprecateConfirmDialog.tsx
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { toast } from "sonner";
import { Modal } from "@/components/application/modals/modal";
import { Button } from "@/components/base/buttons/button";
import { Form } from "@/components/base/form/form";
import { DatePicker } from "@/components/application/date-picker/date-picker";
import {
  deprecateApplicationSchema,
  type DeprecateApplicationInput,
} from "@/features/catalog/schemas/deprecateApplication";
import { useDeprecateApplication, type ApplicationResponse } from "@/features/catalog/api/applications";
import { applyProblemDetailsToForm } from "@/lib/problem-details/apply-problem-details-to-form";

export interface DeprecateConfirmDialogProps {
  application: ApplicationResponse;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function DeprecateConfirmDialog({ application, open, onOpenChange }: DeprecateConfirmDialogProps) {
  // Default sunset = today + 30 days.
  const defaultSunset = new Date(Date.now() + 30 * 24 * 60 * 60 * 1000)
    .toISOString().slice(0, 10);

  const form = useForm<DeprecateApplicationInput>({
    resolver: zodResolver(deprecateApplicationSchema),
    defaultValues: { sunsetDate: `${defaultSunset}T00:00:00Z` },
  });

  const mutation = useDeprecateApplication(application.id);

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      await mutation.mutateAsync(values);
      toast.success(`Deprecated. Sunset: ${new Date(values.sunsetDate).toLocaleDateString()}`);
      onOpenChange(false);
    } catch (err: any) {
      const handled = applyProblemDetailsToForm(err, form);
      if (handled) return;
      const status = err?.__status as number | undefined;
      if (status === 409) {
        toast.error(`Cannot deprecate — current state is ${err.extensions?.currentLifecycle}.`);
        onOpenChange(false);
        return;
      }
      toast.error("Could not deprecate application");
    }
  });

  return (
    <Modal isOpen={open} onOpenChange={onOpenChange} aria-label="Deprecate application">
      <Form form={form} onSubmit={onSubmit}>
        <Modal.Header>
          <Modal.Title>Deprecate {application.displayName}?</Modal.Title>
          <Modal.Description>
            Pick a sunset date — consumers should migrate before then. After the sunset date, this
            application can be Decommissioned.
          </Modal.Description>
        </Modal.Header>

        <Modal.Body>
          <DatePicker
            label="Sunset date"
            value={form.watch("sunsetDate")}
            onChange={(v) => form.setValue("sunsetDate", v)}
            minValue={new Date(Date.now() + 24 * 60 * 60 * 1000).toISOString().slice(0, 10)}
            isInvalid={!!form.formState.errors.sunsetDate}
            errorMessage={form.formState.errors.sunsetDate?.message}
          />
        </Modal.Body>

        <Modal.Footer>
          <Button type="button" color="secondary" onClick={() => onOpenChange(false)}>Cancel</Button>
          <Button type="submit" color="destructive" isLoading={mutation.isPending}>Deprecate</Button>
        </Modal.Footer>
      </Form>
    </Modal>
  );
}
```

- [ ] **Step 3: Create `DecommissionConfirmDialog`.**

```tsx
// web/src/features/catalog/components/DecommissionConfirmDialog.tsx
import { toast } from "sonner";
import { Modal } from "@/components/application/modals/modal";
import { Button } from "@/components/base/buttons/button";
import { useDecommissionApplication, type ApplicationResponse } from "@/features/catalog/api/applications";

export interface DecommissionConfirmDialogProps {
  application: ApplicationResponse;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function DecommissionConfirmDialog({ application, open, onOpenChange }: DecommissionConfirmDialogProps) {
  const mutation = useDecommissionApplication(application.id);

  const onConfirm = async () => {
    try {
      await mutation.mutateAsync();
      toast.success("Application decommissioned");
      onOpenChange(false);
    } catch (err: any) {
      const status = err?.__status as number | undefined;
      if (status === 409) {
        const reason = err.extensions?.reason;
        const sunset = err.extensions?.sunsetDate;
        if (reason === "before-sunset-date" && sunset) {
          toast.error(`Cannot decommission before sunset date ${new Date(sunset).toLocaleDateString()}`);
        } else {
          toast.error(`Cannot decommission — current state is ${err.extensions?.currentLifecycle}.`);
        }
        onOpenChange(false);
        return;
      }
      toast.error("Could not decommission application");
    }
  };

  return (
    <Modal isOpen={open} onOpenChange={onOpenChange} aria-label="Decommission application">
      <Modal.Header>
        <Modal.Title>Decommission {application.displayName}?</Modal.Title>
        <Modal.Description>
          This is a terminal state. The application will be hidden from default views and become
          read-only. This cannot be undone in the current product version.
        </Modal.Description>
      </Modal.Header>
      <Modal.Footer>
        <Button type="button" color="secondary" onClick={() => onOpenChange(false)}>Cancel</Button>
        <Button type="button" color="destructive" onClick={onConfirm} isLoading={mutation.isPending}>
          Decommission
        </Button>
      </Modal.Footer>
    </Modal>
  );
}
```

- [ ] **Step 4: Vitest for `LifecycleMenu` (state-driven rendering).**

```tsx
// web/src/features/catalog/components/__tests__/LifecycleMenu.test.tsx
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { LifecycleMenu } from "../LifecycleMenu";

function wrapper({ children }: { children: ReactNode }) {
  const qc = new QueryClient();
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
}

const baseApp = {
  id: "abc",
  tenantId: "t",
  name: "n",
  displayName: "App",
  description: "d",
  ownerUserId: "u",
  createdAt: "2026-04-30T00:00:00Z",
  version: "v1",
};

describe("LifecycleMenu", () => {
  beforeEach(() => vi.useFakeTimers().setSystemTime(new Date("2026-05-06T12:00:00Z")));
  afterEach(() => vi.useRealTimers());

  it("Active state shows Deprecate menu item", async () => {
    render(<LifecycleMenu application={{ ...baseApp, lifecycle: "Active", sunsetDate: null }} />, { wrapper });
    await userEvent.click(screen.getByRole("button"));
    expect(screen.getByText(/deprecate/i)).toBeVisible();
  });

  it("Deprecated + before sunset disables Decommission with tooltip", async () => {
    render(<LifecycleMenu application={{ ...baseApp, lifecycle: "Deprecated", sunsetDate: "2026-12-31T00:00:00Z" }} />, { wrapper });
    await userEvent.click(screen.getByRole("button"));
    const item = screen.getByText(/decommission/i);
    expect(item.closest('[role="menuitem"]')).toHaveAttribute("aria-disabled", "true");
  });

  it("Deprecated + after sunset enables Decommission", async () => {
    render(<LifecycleMenu application={{ ...baseApp, lifecycle: "Deprecated", sunsetDate: "2026-04-01T00:00:00Z" }} />, { wrapper });
    await userEvent.click(screen.getByRole("button"));
    const item = screen.getByText(/decommission/i);
    expect(item.closest('[role="menuitem"]')).not.toHaveAttribute("aria-disabled", "true");
  });

  it("Decommissioned does not render a dropdown trigger (badge only)", () => {
    render(<LifecycleMenu application={{ ...baseApp, lifecycle: "Decommissioned", sunsetDate: "2026-04-01T00:00:00Z" }} />, { wrapper });
    expect(screen.queryByRole("button", { name: /lifecycle/i })).toBeNull();
  });
});
```

- [ ] **Step 5: Run Vitest.**

```bash
cd web && npm run test --run -- features/catalog/components/__tests__/LifecycleMenu && cd ..
```

Expected: tests pass.

- [ ] **Step 6: Type + lint.**

```bash
cd web && npm run typecheck && npm run lint && cd ..
```

Expected: clean.

- [ ] **Step 7: Commit.**

```bash
git add web/src/features/catalog/components/{LifecycleMenu,DeprecateConfirmDialog,DecommissionConfirmDialog}.tsx web/src/features/catalog/components/__tests__/LifecycleMenu.test.tsx
git commit -m "$(cat <<'EOF'
feat(web): LifecycleMenu + Deprecate/Decommission confirm dialogs (E-02.F-01.S-04 — UI)

State-aware dropdown anchored on the lifecycle Badge:
- Active → \"Deprecate…\" item
- Deprecated, before sunset → \"Decommission\" disabled with tooltip
- Deprecated, after sunset → \"Decommission\" enabled
- Decommissioned → badge only, no menu

DeprecateConfirmDialog uses Untitled UI DatePicker (default today+30d, min
tomorrow). DecommissionConfirmDialog is a plain confirm with terminal-state
warning copy.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 20: SPA — `ApplicationDetailPage` wiring + `ApplicationsTable` lifecycle column

**Goal:** Surface the new components on the detail page and add a Lifecycle column to the catalog list. State-driven hide rules per spec §8.1.

**Files:**
- Modify: `web/src/features/catalog/pages/ApplicationDetailPage.tsx`
- Modify: `web/src/features/catalog/components/ApplicationsTable.tsx`

- [ ] **Step 1: Update `ApplicationDetailPage`.**

Open `web/src/features/catalog/pages/ApplicationDetailPage.tsx`. Replace the hardcoded `<Badge color="success" type="pill-color" size="sm">Active</Badge>` and the `Field` group at the bottom with a state-driven layout:

```tsx
import { useState } from "react";
import { useParams } from "react-router-dom";
import { Card, CardContent, CardHeader } from "@/components/base/card/card";
import { Badge } from "@/components/base/badges/badges";
import { Button } from "@/components/base/buttons/button";
import { Skeleton } from "@/components/base/skeleton/skeleton";
import { useApplication } from "@/features/catalog/api/applications";
import { LifecycleMenu } from "@/features/catalog/components/LifecycleMenu";
import { EditApplicationDialog } from "@/features/catalog/components/EditApplicationDialog";

export function ApplicationDetailPage() {
  const { id } = useParams<{ id: string }>();
  const query = useApplication(id ?? "");
  const [editOpen, setEditOpen] = useState(false);

  if (query.isLoading) {
    return (
      <Card data-testid="detail-skeleton">
        <CardHeader>
          <Skeleton className="h-7 w-64" />
          <Skeleton className="mt-2 h-4 w-32" />
        </CardHeader>
        <CardContent className="space-y-4">
          <Skeleton className="h-20 w-full" />
          <Skeleton className="h-12 w-2/3" />
        </CardContent>
      </Card>
    );
  }

  if (query.isError || !query.data) {
    return (
      <Card className="mx-auto max-w-md">
        <CardContent className="space-y-2 p-6 text-center">
          <p className="text-base font-medium text-error-primary">Application not found</p>
          <p className="text-sm text-tertiary">
            It may have been deleted, or you may not have access in this tenant.
          </p>
        </CardContent>
      </Card>
    );
  }

  const app = query.data;
  const canEdit = app.lifecycle !== "Decommissioned";

  return (
    <>
      <Card>
        <CardHeader className="space-y-3">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div className="flex flex-wrap items-baseline gap-3">
              <h2 className="text-2xl font-semibold text-primary">{app.displayName}</h2>
              <Badge color="gray" type="pill-color" size="sm" className="font-mono">{app.name}</Badge>
              <LifecycleMenu application={app} />
            </div>
            {canEdit && (
              <Button color="secondary" onClick={() => setEditOpen(true)}>
                Edit
              </Button>
            )}
          </div>
        </CardHeader>
        <CardContent className="space-y-6">
          <section>
            <h3 className="text-sm font-medium text-tertiary">Description</h3>
            <p className="mt-1 text-sm text-secondary">
              {app.description ? app.description : <span className="italic">No description</span>}
            </p>
          </section>
          <hr className="border-secondary" />
          <section className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <Field label="ID" value={app.id} mono />
            <Field label="Owner" value={app.ownerUserId ?? "—"} mono />
            <Field label="Created" value={app.createdAt ?? "—"} />
          </section>
        </CardContent>
      </Card>

      {editOpen && (
        <EditApplicationDialog application={app} open onOpenChange={setEditOpen} />
      )}
    </>
  );
}

function Field({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div>
      <div className="text-xs uppercase tracking-wide text-tertiary">{label}</div>
      <div className={mono ? "mt-1 font-mono text-sm text-primary" : "mt-1 text-sm text-primary"}>{value}</div>
    </div>
  );
}
```

- [ ] **Step 2: Add Lifecycle column to `ApplicationsTable`.**

Open `web/src/features/catalog/components/ApplicationsTable.tsx`. Find the existing column definitions. Add a new column between `name` (or `displayName`) and `description`:

```tsx
import { LifecycleBadge } from "./LifecycleBadge";

// In the table body where rows are rendered:
<td className="px-4 py-3">
  <LifecycleBadge lifecycle={row.lifecycle} sunsetDate={row.sunsetDate} />
</td>
```

If the table has a header definition array, mirror the new column there with label "Lifecycle" (no sortable indicator — sorting by lifecycle is deferred per spec §8.5).

- [ ] **Step 3: Run Vitest to confirm no regressions.**

```bash
cd web && npm run test --run -- features/catalog && cd ..
```

Expected: existing `ApplicationsTable` and `ApplicationDetailPage` tests still pass. (If they break on the new column or new affordances, update those tests minimally to reflect the new render.)

- [ ] **Step 4: Type + lint.**

```bash
cd web && npm run typecheck && npm run lint && cd ..
```

Expected: clean.

- [ ] **Step 5: Build.**

```bash
cd web && npm run build && cd ..
```

Expected: `vite build` succeeds; bundle emits to `dist/`.

- [ ] **Step 6: Commit.**

```bash
git add web/src/features/catalog/pages/ApplicationDetailPage.tsx web/src/features/catalog/components/ApplicationsTable.tsx
git commit -m "$(cat <<'EOF'
feat(web): wire Edit + LifecycleMenu on detail page; lifecycle column on list

Detail header: replaces hardcoded \"Active\" badge with LifecycleMenu;
adds Edit button (hidden when lifecycle === Decommissioned, defense-in-depth
against the server-side 409). List adds a Lifecycle column (filtering
deferred to E-05.F-01.S-02 per spec §8.5).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 21: Docker compose smoke + Playwright MCP manual verification (DoD §5)

**Goal:** Cold-start the full stack and run the seven happy-path + error-path checks from spec §9.8 + the SPA flow from spec §8. Capture screenshots + console-clean evidence into the PR description.

**No source files change in this task** — it's verification.

- [ ] **Step 1: Cold-start the stack.**

```bash
docker compose down -v
docker compose up --build -d
```

Wait ~30 seconds for KeyCloak realm import to complete. Verify:

```bash
docker compose ps
docker compose logs api --tail 40
```

Expected: `api` healthy; KeyCloak healthy; Postgres ready.

- [ ] **Step 2: Run the migrator in dev mode (--seed=dev).**

```bash
cmd //c "dotnet run --project src/Kartova.Migrator -- --seed=dev"
```

Expected: migration applied (already applied is fine), Org A row inserted (already inserted is fine — idempotent).

- [ ] **Step 3: Run the backend HTTP smoke checks via curl. Capture outputs into the PR description.**

```bash
# 1. Login as admin@orga and capture JWT (use the helper if your repo has one,
#    or curl the KeyCloak token endpoint directly).
TOKEN=$(curl -s -X POST http://localhost:8180/realms/kartova/protocol/openid-connect/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=password&client_id=kartova-web&username=admin@orga.kartova.local&password=dev_pass" \
  | python -c "import sys,json; print(json.load(sys.stdin)['access_token'])")

# 2. Register an application.
REG=$(curl -s -X POST http://localhost:5080/api/v1/catalog/applications \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"name":"smoke-app","displayName":"Smoke","description":"Smoke test."}')
echo "$REG"
ID=$(echo "$REG" | python -c "import sys,json; print(json.load(sys.stdin)['id'])")
VERSION=$(echo "$REG" | python -c "import sys,json; print(json.load(sys.stdin)['version'])")

# 3. PUT edit with valid If-Match → 200.
curl -i -X PUT "http://localhost:5080/api/v1/catalog/applications/$ID" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -H "If-Match: \"$VERSION\"" \
  -d '{"displayName":"Smoke Renamed","description":"Renamed."}'

# 4. PUT edit with stale If-Match → 412.
curl -i -X PUT "http://localhost:5080/api/v1/catalog/applications/$ID" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -H "If-Match: \"$VERSION\"" \
  -d '{"displayName":"Stale","description":"Stale."}'

# 5. POST deprecate (sunsetDate +1 second so step 7 can decommission immediately after sleep).
SUNSET=$(date -u -d "+2 seconds" +%Y-%m-%dT%H:%M:%SZ)
curl -i -X POST "http://localhost:5080/api/v1/catalog/applications/$ID/deprecate" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d "{\"sunsetDate\":\"$SUNSET\"}"

# 6. POST decommission immediately → 409 with reason=before-sunset-date.
curl -i -X POST "http://localhost:5080/api/v1/catalog/applications/$ID/decommission" \
  -H "Authorization: Bearer $TOKEN"

# 7. Wait then POST decommission → 200.
sleep 3
curl -i -X POST "http://localhost:5080/api/v1/catalog/applications/$ID/decommission" \
  -H "Authorization: Bearer $TOKEN"

# 8. POST decommission for an Active app → 409.
REG2=$(curl -s -X POST http://localhost:5080/api/v1/catalog/applications \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"name":"smoke-active","displayName":"Active","description":"Active."}')
ID2=$(echo "$REG2" | python -c "import sys,json; print(json.load(sys.stdin)['id'])")
curl -i -X POST "http://localhost:5080/api/v1/catalog/applications/$ID2/decommission" \
  -H "Authorization: Bearer $TOKEN"
```

Expected outputs:
- Step 3: `200 OK` + `ETag` header + new `version`.
- Step 4: `412 Precondition Failed` + ProblemDetails with `currentVersion`.
- Step 5: `200 OK` + `lifecycle: "Deprecated"`.
- Step 6: `409 Conflict` + ProblemDetails with `reason: "before-sunset-date"`.
- Step 7: `200 OK` + `lifecycle: "Decommissioned"`.
- Step 8: `409 Conflict` + ProblemDetails with `currentLifecycle: "Active"`.

Capture each `curl -i` block in the PR description as evidence.

- [ ] **Step 4: Frontend dev server up.**

```bash
cd web && npm run dev
```

Expected: Vite dev server on `http://localhost:5173`.

- [ ] **Step 5: Playwright MCP browser flow.**

Open Playwright MCP and run the following script. Capture console-clean snapshots after each step into the PR description.

1. Navigate to `http://localhost:5173`. Expect KeyCloak redirect.
2. Login as `user@orga.kartova.local` / `dev_pass`. Land on `/catalog`.
3. List shows `smoke-active` (and `smoke-app` Decommissioned). Click `smoke-active`.
4. Detail page shows green "Active" badge, Edit button visible.
5. Click Edit. Modal opens, fields pre-filled.
6. Change displayName to "Smoke Active Renamed". Save. Toast appears, modal closes, new value rendered, list cell updates.
7. Click the lifecycle badge. Dropdown opens; "Deprecate…" enabled.
8. Click Deprecate. Confirmation dialog opens with date picker (default today+30). Pick today+1 day. Confirm.
9. Detail re-renders with amber "Deprecated" + "Sunset: …" subline.
10. Click badge → "Decommission" disabled with tooltip ("Available after …").
11. Open the second tab, change system clock or seed a Deprecated row with sunsetDate=now-1s for the manual step (alternatively: skip ahead to a tomorrow run).
12. Click Decommission → confirmation dialog → confirm → gray badge, Edit button hides, dropdown not rendered.
13. Refresh the page. State persists.
14. Console is clean (no errors, no warnings).

Capture screenshots at steps 4, 6, 9, 12 into the PR description.

- [ ] **Step 6: Commit (no code change — verification milestone).**

No commit. Tear down the stack:

```bash
docker compose down
```

If verification surfaced bugs, fix them and re-run. **Do not** advance to Task 22 until all 14 SPA steps + 8 backend curl steps are evidenced.

---

## Task 22: CHECKLIST ticks + final review + PR

**Goal:** Wrap up the slice — update the product checklist, run the multi-lens review pipeline (per CLAUDE.md DoD), open the PR with all evidence.

**Files:**
- Modify: `docs/product/CHECKLIST.md`

- [ ] **Step 1: Tick the checklist rows.**

Open `docs/product/CHECKLIST.md`. Mark these rows as `[x]` and append the PR reference once known (after Step 6):

```
- [x] E-02.F-01.S-03 — Edit application metadata (slice 5 — PR #<n>, 2026-05-06)
- [x] E-02.F-01.S-04 — Application lifecycle status transitions (slice 5 — PR #<n>, 2026-05-06)
- [x] E-02.F-01.S-06 — Field-level ProblemDetails errors (shipped slice-4-cleanup — PR #18, 2026-05-01)
- [x] E-02.F-01.S-07 — Move kebab-case Name validation into Application.Create domain invariant (shipped slice-4-cleanup — PR #18, 2026-05-01)
- [x] E-01.F-01.S-04 — Dev-stack seed data Org A (shipped slice-4-cleanup — PR #18, 2026-05-01)
```

Also bump the Phase 0 / Phase 1 progress counters at the top of the file. Phase 1 was `0/55`; this slice closes 2 stories → `2/55`. Phase 0 was `7/33`; the F-01.S-04 housekeeping bumps it to `8/33`. Total `7/209` → `9/209`.

- [ ] **Step 2: Update top-of-file `Last updated` line to today.**

```
**Last updated:** 2026-05-06
```

- [ ] **Step 3: Commit checklist update.**

```bash
git add docs/product/CHECKLIST.md
git commit -m "$(cat <<'EOF'
docs(checklist): tick S-03 + S-04 (slice 5) and stale S-06/S-07/F-01.S-04 (PR #18)

Phase 0: 7 → 8 (E-01.F-01.S-04 dev seed retroactively ticked).
Phase 1: 0 → 2 (E-02.F-01.S-03 edit metadata, E-02.F-01.S-04 lifecycle).
Total: 7/209 → 9/209.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 4: Push the branch.**

```bash
git push -u origin feat/slice-5-applications-edit-lifecycle
```

- [ ] **Step 5: Run `/simplify` against the branch diff.**

In the Claude Code terminal:

```
/simplify
```

The skill scans the branch diff for reuse / quality / efficiency findings. Address Should-fix items inline (commit each fix as a separate commit on the branch) or skip with an explicit reason in the PR description.

- [ ] **Step 6: Run `/deep-review` against the branch diff.**

```
/deep-review
```

Use the spec (`c8b92d9`), this plan, ADR-0073, ADR-0096, and ADR-0090 as context. Address every Blocking item; resolve Should-fix items or document why deferred; triage nits.

- [ ] **Step 7: Run `superpowers:requesting-code-review` at slice boundary.**

```
/superpowers:requesting-code-review
```

Use the same context bundle. Catches cross-task design issues the per-task review can't see.

- [ ] **Step 8: Open the PR.**

```bash
gh pr create --title "feat(slice-5): Applications edit + lifecycle transitions (E-02.F-01.S-03 + S-04)" --body "$(cat <<'EOF'
## Summary

- Closes E-02.F-01.S-03 (edit metadata) + E-02.F-01.S-04 (lifecycle transitions). PUT /applications/{id} for full-replacement edit; POST /{id}/deprecate + /{id}/decommission for ADR-0073 lifecycle transitions.
- Co-ships ADR-0096 (REST verb policy: PUT for replacement, POST for actions, no PATCH) — first edit slice instantiates it. Arch test pins absence of PATCH endpoints.
- Optimistic concurrency on edit via Postgres xmin + If-Match + 412 (first edit endpoint of ~20 across catalog entities — pattern set here).

## Spec, plan, ADR

- Spec: `docs/superpowers/specs/2026-05-06-slice-5-applications-edit-lifecycle-design.md` (commit c8b92d9)
- Plan: `docs/superpowers/plans/2026-05-06-slice-5-applications-edit-lifecycle-plan.md`
- ADR: `docs/architecture/decisions/ADR-0096-rest-verb-policy.md`

## Test plan

- [ ] `dotnet build Kartova.slnx -c Debug` — 0 warnings, 0 errors
- [ ] `cd web && npm run build` — TS strict + ESLint clean
- [ ] Backend unit + arch suite green (incl. 4 new arch tests + 16 new domain tests + 6 new SharedKernel.AspNetCore tests)
- [ ] Backend integration suite green (~18 new tests across EditApplicationTests, DeprecateApplicationTests, DecommissionApplicationTests)
- [ ] Frontend Vitest green (~14 new tests — schemas, hooks, dialogs, menu)
- [ ] Docker compose smoke per Task 21 — 8 backend curl checks captured below
- [ ] Playwright MCP browser flow per Task 21 — 14 steps + 4 screenshots captured below
- [ ] /simplify findings addressed
- [ ] /deep-review Blocking + Should-fix addressed
- [ ] mutation-sentinel ≥80% on changed files
- [ ] Copilot review requested + resolved

## DoD §5 — Docker compose smoke evidence

<paste the curl -i outputs from Task 21 step 3 here>

## DoD §5 — SPA flow evidence

<attach the Playwright MCP snapshots from Task 21 step 5>

## Notes

- Audit log of transitions deferred — depends on E-01.F-03.S-03 (audit table), captured spec §13.1.
- Notifications on transitions deferred — depends on ADR-0047 / E-06 notification infra, spec §13.3.
- Admin override on backward transitions deferred — depends on E-01.F-04.S-03 RBAC, spec §13.2.
- Successor reference deferred — captured spec §13.4 with cascade-on-decommissioned question owned by the slice that gives the field a consumer.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

- [ ] **Step 9: Run mutation-sentinel + test-generator loop.**

```
/mutation-sentinel
```

Target ≥80% per `stryker-config.json`. For surviving mutants, run:

```
/test-generator
```

…until either the score is met or remaining survivors are documented as accepted (low-value) in a PR comment.

- [ ] **Step 10: Request Copilot review.**

```bash
PR_NUMBER=$(gh pr view --json number -q .number)
gh pr edit "$PR_NUMBER" --add-reviewer copilot-pull-request-reviewer
```

Wait for Copilot's findings; address or dismiss with a reason in PR comments.

- [ ] **Step 11: Tick the PR-number placeholder in CHECKLIST.md and amend.**

After the PR is open and the number is known:

```bash
sed -i "s|slice 5 — PR #<n>|slice 5 — PR #$PR_NUMBER|g" docs/product/CHECKLIST.md
git add docs/product/CHECKLIST.md
git commit -m "$(cat <<'EOF'
docs(checklist): record slice-5 PR number

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
git push
```

- [ ] **Step 12: Final DoD check.** Verify every CLAUDE.md DoD bullet is cited in the PR description with command + output. The PR is ready to merge only when all nine bullets are green.

---

## Plan self-review

### 1. Spec coverage

| Spec section | Implementing task(s) |
|---|---|
| §3 Decision #1 — slice scope (S-03 + S-04, S-06/S-07/F-01.S-04 housekeeping) | Tasks 11–13 (code), Task 22 (housekeeping) |
| §3 Decision #2 — lifecycle scope (3 states + sunset_date strict + linear forward + backward 400) | Tasks 4 (enum), 5 (domain methods), 12-13 (transitions) |
| §3 Decision #3 — edit field scope (displayName + description) | Task 11 (DTO + handler) |
| §3 Decision #4 — REST verb (PUT for edit, POST for actions) | Tasks 11–13 (endpoints), Task 14 (arch test) |
| §3 Decision #5 — ADR-0096 in-PR | Task 1 (ADR), Task 2 (arch test) |
| §3 Decision #6 — xmin + If-Match + 412 | Task 3 (spike), 6 (config), 7 (helper), 8 (filter), 9 (handler), 11 (endpoint), 11 (test) |
| §3 Decision #7 — lifecycle endpoints don't take If-Match | Tasks 12, 13 (endpoints emit no header) |
| §3 Decision #8 — successor deferred | Captured §13.4 of spec; not implemented |
| §3 Decision #9 — UI: edit modal + lifecycle dropdown | Tasks 18 (dialog), 19 (menu + confirm dialogs), 20 (wiring) |
| §3 Decision #10 — sunsetDate strictly future | Task 5 (domain test boundary), Task 12 (integration), Task 17 (zod) |
| §3 Decision #11 — re-deprecate = 409, not idempotent | Task 5 (domain test), Task 12 (integration test) |
| §3 Decision #12 — edit on Decommissioned = 409 | Task 5 (domain), Task 11 (integration) |
| §3 Decision #13 — wire enum is string, DB is smallint | Task 6 (config), Task 7 (response shape) |
| §3 Decision #14 — TimeProvider new methods only | Task 5 (domain methods take clock), Task 12 (handler injects TimeProvider.System) |
| §3 Decision #15 — migration adds columns with default | Task 6 (migration body) |
| §6 Data flow + §7 error handling | Tasks 8–10 (handlers), Tasks 11–13 (integration coverage) |
| §8 UI surface | Tasks 17–20 |
| §9 Testing | Coverage threaded through Tasks 4, 5, 8–13, 14, 16, 17, 18, 19 |
| §10 Out of scope | Captured §13 spec (no tasks) |
| §11 Success criteria | Task 15 (backend gate), Task 21 (smoke), Task 22 (DoD bullets) |

### 2. Placeholder scan

No "TBD", "TODO", or "implement later". One intentional `<n>` placeholder for the PR number in the CHECKLIST entry — replaced via `sed` in Task 22 step 11.

### 3. Type / contract consistency

- `EditApplicationCommand` shape `(ApplicationId Id, string DisplayName, string Description, uint ExpectedVersion)` consistent across Task 11 (record), Task 11 (handler signature), Task 11 (endpoint construction).
- `DeprecateApplicationCommand` shape `(ApplicationId Id, DateTimeOffset SunsetDate)` consistent across Task 12 record + handler + endpoint.
- `DecommissionApplicationCommand` shape `(ApplicationId Id)` consistent across Task 13 record + handler + endpoint.
- `Lifecycle` enum members consistent with arch test (Task 4) + EF mapping (Task 6, `HasConversion<short>` rounds to enum int) + wire (Task 7, `JsonStringEnumConverter` emits PascalCase string) + SPA (Task 17 LifecycleBadge uses literal string union "Active" | "Deprecated" | "Decommissioned").
- `Version` wire encoding (base64 of `xmin` `uint`) consistent across `VersionEncoding.Encode/TryDecode` (Task 7), `IfMatchEndpointFilter` parsing (Task 8), `ConcurrencyConflictExceptionHandler` extension (Task 9), SPA hooks (Task 16).
- `If-Match` quoted-RFC-7232 form consistent across Task 8 filter parsing, Task 11 integration test sending, Task 16 hook attaching.

### 4. Scope check

22 tasks, comparable to slice 4 (24) and significantly larger than slice 3 (14). Plan length ~3500-4000 lines tracks slice-3+slice-4 combined density. Single PR. The walking-skeleton discipline is preserved by the explicit deferrals (audit, notifications, RBAC, successor) — each a follow-up slice with a concrete trigger.

### 5. Ambiguity

- "If `RegisterApplicationAsync` doesn't exist as a helper, copy the inline registration pattern from a neighboring test." (Task 7) — intentional, resolved at implementation time.
- "If your repo uses different paths or component names, mirror that file's imports — don't invent." (Task 18, 19) — Untitled UI imports vary; the existing `RegisterApplicationDialog` is the reference.

No issues found.
