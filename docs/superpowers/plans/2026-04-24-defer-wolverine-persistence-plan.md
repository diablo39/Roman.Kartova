# Defer Wolverine PostgreSQL Persistence — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove Wolverine's PostgreSQL persistence from Slice 2 (API has no outbox publishers), add a NetArchTest guard so nobody re-introduces it outside `Kartova.Migrator`, and replace the stale comment in the migrator.

**Architecture:** Wolverine stays registered as an in-process CQRS mediator via `builder.Host.UseWolverine(...)` — the call to `PersistMessagesWithPostgresql` and its package reference are removed. A new fitness function in `Kartova.ArchitectureTests` fails the build if any production assembly reintroduces a dependency on the `Wolverine.Postgresql` namespace. Test-driven ordering: write the failing arch test first, then remove the code to make it pass.

**Tech Stack:** .NET 10, WolverineFx 5.32, NetArchTest.Rules, FluentAssertions, xUnit, Testcontainers Postgres.

**Spec:** `docs/superpowers/specs/2026-04-24-defer-wolverine-persistence-design.md`

---

## File Structure

| Path | Action | Responsibility |
|------|--------|----------------|
| `tests/Kartova.ArchitectureTests/WolverinePersistenceBoundaryTests.cs` | Create | Fitness function: no production assembly may reference `Wolverine.Postgresql`. |
| `src/Kartova.Api/Program.cs` | Modify (lines 14, 61-70) | Remove `using Wolverine.Postgresql;`, drop the `PersistMessagesWithPostgresql` call, update the surrounding comment. |
| `src/Kartova.Api/Kartova.Api.csproj` | Modify (line 17) | Remove the `WolverineFx.Postgresql` `PackageReference`. |
| `src/Kartova.Migrator/Program.cs` | Modify (lines 22-26) | Replace the stale comment claiming Wolverine tables are created lazily by the API with one stating the migrator is the sole DDL owner (ADR-0085). |

No other files change. `KartovaApiFixture.cs` role-creation SQL stays as-is.

---

### Task 1: Add failing architecture test (TDD red)

**Files:**
- Create: `tests/Kartova.ArchitectureTests/WolverinePersistenceBoundaryTests.cs`

The existing arch tests (`ForbiddenDependencyTests.cs`) use `NetArchTest.Rules` with
`NotHaveDependencyOn("MediatR")`. The same pattern applies here: ban `Wolverine.Postgresql`
namespace references across every production assembly listed in `AssemblyRegistry.AllProduction()`.
`Kartova.Migrator` is **not** in `AllProduction()`, so when a future slice re-enables
Wolverine persistence inside the migrator, this test will still pass without modification.

- [ ] **Step 1: Write the failing test**

Create the file with this exact content:

```csharp
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Kartova.ArchitectureTests;

public class WolverinePersistenceBoundaryTests
{
    [Fact]
    public void No_Production_Assembly_Depends_On_WolverinePostgresql()
    {
        foreach (var assembly in AssemblyRegistry.AllProduction())
        {
            var result = Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOn("Wolverine.Postgresql")
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                $"Wolverine PostgreSQL persistence is deferred per " +
                $"docs/superpowers/specs/2026-04-24-defer-wolverine-persistence-design.md. " +
                $"Assembly {assembly.GetName().Name} must not reference Wolverine.Postgresql. " +
                $"When an outbox-using slice lands, introduce the dependency in Kartova.Migrator " +
                $"(not listed in AllProduction()) and add API-side auto-create suppression. " +
                $"Violating types: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:
```bash
cmd //c dotnet test tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj --filter "FullyQualifiedName~WolverinePersistenceBoundaryTests" --nologo
```

Expected: **FAIL** — `Kartova.Api` currently references `Wolverine.Postgresql` via `using Wolverine.Postgresql;` and the `PersistMessagesWithPostgresql` call, so the assertion fails with a message naming `Kartova.Api` as the violating assembly and `<Program>$` (or similar) as the violating type.

- [ ] **Step 3: Commit the failing test**

```bash
git add tests/Kartova.ArchitectureTests/WolverinePersistenceBoundaryTests.cs
git commit -m "test(arch): forbid Wolverine.Postgresql in production assemblies (failing)"
```

The failing commit is intentional — it locks in the guard before the code change so reviewers can see the red→green transition across two commits.

---

### Task 2: Remove Wolverine persistence from `Kartova.Api`

**Files:**
- Modify: `src/Kartova.Api/Program.cs` (line 14; lines 61-70)
- Modify: `src/Kartova.Api/Kartova.Api.csproj` (line 17)

This is the change that flips the arch test from red to green.

- [ ] **Step 1: Remove the `using Wolverine.Postgresql;` import**

In `src/Kartova.Api/Program.cs`, delete line 14 which currently reads:

```csharp
using Wolverine.Postgresql;
```

Surrounding imports (`using JasperFx;`, `using Wolverine;`, etc.) stay. Do not re-sort — just delete that one line.

- [ ] **Step 2: Remove the `PersistMessagesWithPostgresql` call and rewrite the comment**

In `src/Kartova.Api/Program.cs`, replace lines 61-70:

```csharp
// Wolverine — persistence only; no message routing in Slice 2.
builder.Host.UseWolverine(opts =>
{
    opts.PersistMessagesWithPostgresql(kartovaConnection, schemaName: "wolverine");

    foreach (var module in modules)
    {
        module.ConfigureWolverine(opts);
    }
});
```

…with:

```csharp
// Wolverine — in-process CQRS mediator only.
// Postgres persistence (outbox) is deferred until a slice publishes domain events.
// See ADR-0080 and docs/superpowers/specs/2026-04-24-defer-wolverine-persistence-design.md.
// When persistence is re-enabled, the `wolverine.*` schema must be created by
// Kartova.Migrator (ADR-0085), and API-side auto-create must be disabled at the
// same time.
builder.Host.UseWolverine(opts =>
{
    foreach (var module in modules)
    {
        module.ConfigureWolverine(opts);
    }
});
```

- [ ] **Step 3: Remove the `WolverineFx.Postgresql` package reference**

In `src/Kartova.Api/Kartova.Api.csproj`, delete line 17:

```xml
    <PackageReference Include="WolverineFx.Postgresql" Version="5.32.0" />
```

Leave `WolverineFx` and `WolverineFx.EntityFrameworkCore` references untouched — only `WolverineFx.Postgresql` goes.

- [ ] **Step 4: Build the API project to surface any lingering references**

Run:
```bash
cmd //c dotnet build src/Kartova.Api/Kartova.Api.csproj --nologo
```

Expected: **0 errors, 0 warnings**. If a warning says something like `CS8019: Unnecessary using directive` on any remaining line, delete that using. If the build fails, it means something else in `Program.cs` was calling a type from `Wolverine.Postgresql` — investigate before proceeding.

- [ ] **Step 5: Run the architecture test to verify it now passes**

Run:
```bash
cmd //c dotnet test tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj --filter "FullyQualifiedName~WolverinePersistenceBoundaryTests" --nologo
```

Expected: **PASS** (1 test passed). If it still fails, re-grep for `Wolverine.Postgresql` or `PersistMessagesWithPostgresql` in `src/` and remove any straggler.

- [ ] **Step 6: Commit**

```bash
git add src/Kartova.Api/Program.cs src/Kartova.Api/Kartova.Api.csproj
git commit -m "refactor(api): drop Wolverine Postgres persistence — no outbox users in Slice 2"
```

---

### Task 3: Rewrite the stale comment in `Kartova.Migrator/Program.cs`

**Files:**
- Modify: `src/Kartova.Migrator/Program.cs` (lines 22-26)

The current comment claims Wolverine tables are created lazily by the API. After Task 2 that statement is false. Replace it with a forward-looking one that records the deferral.

- [ ] **Step 1: Replace the comment block**

In `src/Kartova.Migrator/Program.cs`, replace lines 22-26:

```csharp
// The migrator doesn't route Kafka messages, but Wolverine may want its own tables
// (outbox persistence) — we still register schema so migrations include them in Slice 3.
// For Slice 1 we skip Wolverine bootstrap in the migrator itself; wolverine tables are
// created lazily by the API.
```

…with:

```csharp
// Kartova.Migrator is the sole DDL owner (ADR-0085). Slice 2 has no Wolverine
// persistence — see docs/superpowers/specs/2026-04-24-defer-wolverine-persistence-design.md.
// When a later slice enables Wolverine persistence (outbox), the `wolverine.*`
// schema must be created here under the `migrator` role (Option A — host Wolverine
// in this process and call JasperFx IStatefulResource / IMessageStore.Admin.MigrateAsync).
```

- [ ] **Step 2: Build the migrator to confirm no accidental code damage**

Run:
```bash
cmd //c dotnet build src/Kartova.Migrator/Kartova.Migrator.csproj --nologo
```

Expected: **0 errors, 0 warnings**.

- [ ] **Step 3: Commit**

```bash
git add src/Kartova.Migrator/Program.cs
git commit -m "docs(migrator): record deferred Wolverine schema ownership (ADR-0085)"
```

---

### Task 4: Definition-of-Done verification (all five gates)

**Files:**
- None modified. This task runs commands and records their output.

This task is required by `CLAUDE.md`'s five-gate DoD rule. Do **not** mark this task complete unless every sub-step produced the expected result and the output was captured. If any gate fails for a reason unrelated to this change (e.g., a pre-existing integration-test failure), record the failure verbatim — it is the hand-off point to the next slice (integration-test debugging), not a reason to patch around.

- [ ] **Step 1: Full-solution build, warnings-as-errors**

Run:
```bash
cmd //c dotnet build Kartova.slnx --nologo
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. Record the "Build succeeded" line and the `0 Warning(s) 0 Error(s)` summary in the task notes.

- [ ] **Step 2: Architecture tests — full suite**

Run:
```bash
cmd //c dotnet test tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj --nologo
```

Expected: `Passed! - Failed: 0`. All existing rules (`No_Module_References_MediatR`, `No_Module_References_MassTransit`, Clean Architecture layer rules, module boundary rules, tenant-scope rules) plus the new `No_Production_Assembly_Depends_On_WolverinePostgresql` all green.

- [ ] **Step 3: Unit tests — full suite**

Run:
```bash
cmd //c dotnet test --filter "FullyQualifiedName!~IntegrationTests&FullyQualifiedName!~ArchitectureTests" --nologo
```

Expected: `Passed! - Failed: 0` across all projects matching (organization unit tests, catalog unit tests, shared-kernel tests, etc.). The filter excludes integration and architecture tests — those are separate gates.

- [ ] **Step 4: Integration tests — Organization module end-to-end**

Docker Desktop must be running for Testcontainers.

Run:
```bash
cmd //c dotnet test src/Modules/Organization/Kartova.Organization.IntegrationTests/Kartova.Organization.IntegrationTests.csproj --nologo
```

Expected: `Passed! - Failed: 0`. If a test fails, capture the failure message verbatim and STOP. Do **not** attempt to fix it in this task — per the spec's "Explicitly out of scope" section, integration-test debugging is a separate follow-up slice. The honest status for this plan becomes *"gates 1-3 green; integration-test failures pre-existing and out of scope — see notes"*.

- [ ] **Step 5: Docker-compose smoke — happy path + negative path**

Required by `CLAUDE.md` for any slice that wires HTTP / auth / DB / middleware. Even though this change *removes* a runtime interaction, the purpose is to confirm nothing was silently papered over by Wolverine's auto-create.

From the repo root:

```bash
cmd //c docker compose up --build -d
```

Wait for `kartova-api` to report healthy (poll `http://localhost:8080/health/ready` until 200). Then:

**Happy path** — `GET /api/v1/organizations/me` with a valid JWT. Use the repo's existing test-JWT-minting helper or a staged token. Expected: **200 OK** with organization JSON.

**Negative path** — same endpoint without a token. Expected: **401 Unauthorized**.

Capture the two HTTP status lines + response bodies in the task notes.

Tear down:
```bash
cmd //c docker compose down -v
```

If Docker is unavailable on the execution machine, say so explicitly in the task notes: *"Step 5 not executed — Docker unavailable; flagged for pending user verification"* — do not imply success.

- [ ] **Step 6: Final status entry**

Write one line per gate to the task notes in the form:

```
Gate 1 (build): PASS — 0 warnings, 0 errors
Gate 2 (arch): PASS — N tests passed
Gate 3 (unit): PASS — N tests passed
Gate 4 (integration): PASS | FAIL (capture) | skipped — reason
Gate 5 (docker smoke): PASS — 200 OK / 401 | skipped — reason
```

Only when gates 1-3 are PASS and gate 4 is PASS (or explicitly documented as a pre-existing, out-of-scope failure) and gate 5 is PASS (or explicitly skipped with reason) is this plan **done**. Otherwise status is *"implementation staged, verification pending"*.

- [ ] **Step 7: Commit verification log (if any untracked files were produced)**

No file changes are expected from this task. If any logs or scratch artifacts were produced, remove them rather than commit. The task's "output" lives in the agent's task notes / PR description, not in the repo.

---

## Self-Review

**Spec coverage:**
- Remove `PersistMessagesWithPostgresql` → Task 2.
- Remove unused `using Wolverine.Postgresql;` → Task 2 Step 1.
- Rewrite stale migrator comment → Task 3.
- Rewrite API comment → Task 2 Step 2.
- Architecture-test guard → Task 1 + green transition in Task 2.
- Verification per CLAUDE.md 5-gate DoD → Task 4 (all five).
- Preserve `KartovaApiFixture` role-creation SQL (explicitly *not* changed) → no task, correct by omission.
- Out-of-scope clause for integration-test failures → Task 4 Step 4.

All spec sections are implemented.

**Placeholder scan:** no "TBD", no "implement later", no "add appropriate error handling", no unnamed types. Every code snippet is concrete.

**Type / name consistency:**
- Arch test class: `WolverinePersistenceBoundaryTests` — same in Task 1 Step 1, Step 2, Step 3, and Task 2 Step 5.
- Namespace banned: `Wolverine.Postgresql` — same string in arch test, grep guidance, and comment rewrites.
- Package ref removed: `WolverineFx.Postgresql` Version 5.32.0 — matches the actual line 17 in `Kartova.Api.csproj`.
- Migrator path: `src/Kartova.Migrator/Program.cs` — same in Task 3 Steps 1, 2, 3.
- `AssemblyRegistry.AllProduction()` — referenced by same name in Task 1's code and self-review.
