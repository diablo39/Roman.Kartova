# xUnit → MSTest v4 Migration Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace xUnit + FluentAssertions with MSTest v4 + native asserts across all 10 xUnit-using test projects in the repo, and supersede ADR-0083. VSTest runner, `Microsoft.NET.Sdk`, and `coverlet.collector` all unchanged — MTP adoption is deferred (Stryker.NET does not support it at the version probed in Phase 0; see stryker-net#3094 and baseline-doc §"Stryker × MTP compatibility probe").

**Architecture:** Phased per-project migration. Phase 0 lands tooling + ADR + mutation baseline. Phases 1–11 each migrate one project (xUnit and MSTest tests coexist within a project during the file-by-file translation window). Phase 8 is an *additive* contract change in `KartovaApiFixtureBase` — both `IAsyncLifetime` and `IAsyncDisposable` interfaces live there until Phase 12 removes `IAsyncLifetime`. Phase 12 drops xUnit/FluentAssertions packages from CPM. Project SDK stays on `Microsoft.NET.Sdk` throughout.

**Tech Stack:** .NET 10, MSTest v4 (`MSTest.TestFramework`, `MSTest.TestAdapter`, `MSTest.Analyzers`), `Microsoft.NET.Test.Sdk` (unchanged), VSTest (unchanged), `coverlet.collector` (unchanged), NSubstitute (unchanged), Testcontainers (unchanged), NetArchTest (unchanged), Stryker.NET (unchanged).

**Source spec:** [`docs/superpowers/specs/2026-05-08-xunit-to-mstest-migration-design.md`](../specs/2026-05-08-xunit-to-mstest-migration-design.md). The plan refers to **spec §4** for the canonical translation rules and **spec §5** for fixture migration patterns. Engineers must read both sections before starting any per-file translation.

---

## Pre-flight (one-time, before Phase 0)

### Task PF-1: Read the spec

**Files:** Read-only.

- [ ] **Step 1: Read spec sections 1–9 end to end.**

Read: `docs/superpowers/specs/2026-05-08-xunit-to-mstest-migration-design.md`

Focus on §4 (translation rules) and §5 (fixture migration). Every translation task in this plan defers to those sections for the actual rules.

- [ ] **Step 2: Verify the worktree is clean.**

Run:
```
git status
```
Expected: `working tree clean` on `master` (or your migration branch).

---

## Stryker invocation note (referenced from Phase 1 / 2 / 9 / 10 / 11 mutation steps)

At the Stryker version pinned during Phase 0, the root `stryker-config.json` invocation can fail at a source-generator/interceptor bug in `Microsoft.AspNetCore.OpenApi.SourceGenerators` (CS9234). The repo's working pattern is per-project orchestration via `mutation-targets.json` + the `mutation-sentinel` skill. Mutation steps in Phases 1, 2, 9, 10, 11 below explicitly use per-project Stryker configs (`src/Kartova.SharedKernel/stryker-config.json`, `src/Kartova.SharedKernel.AspNetCore/stryker-config.json`, etc.) — do NOT use the root config in those per-phase mutation steps.

**Phase 12 (Task 12.6 — final mutation regression check) is the exception:** Phase 12 deliberately re-runs the root config as the post-migration full-suite gate. If the root invocation still trips CS9234 at that point, fall back to per-project runs and aggregate the scores manually.

(See also: baseline doc §"Why not a fresh run?" for the original diagnosis.)

---

## Phase 0 — Tooling, ADR, baseline (no test code rewritten)

After Phase 0 lands, the existing xUnit suite still runs. Plumbing only.

### Task 0.1: Add MSTest packages to central package management

**Files:**
- Modify: `Directory.Packages.props`

- [ ] **Step 1: Add MSTest v4 package versions.**

Open `Directory.Packages.props`. In the `<ItemGroup>` under `<!-- Test dependencies -->`, add **after** the `Microsoft.NET.Test.Sdk` line:

```xml
    <!-- MSTest v4 — added during xUnit→MSTest migration; xUnit lines are removed in Phase 12 -->
    <PackageVersion Include="MSTest.TestFramework" Version="4.0.0" />
    <PackageVersion Include="MSTest.TestAdapter" Version="4.0.0" />
    <PackageVersion Include="MSTest.Analyzers" Version="4.0.0" />
```

Verify the actual latest 4.x version from NuGet at execution time — bump to the latest patch release.

- [ ] **Step 2: Verify build still works.**

Run:
```
dotnet restore Kartova.slnx
```
Expected: success, no version conflicts. The new packages are registered at CPM level but no project references them yet.

- [ ] **Step 3: Commit.**

```
git add Directory.Packages.props
git commit -m "chore(test): register MSTest v4 packages in CPM"
```

### Task 0.2: Create root Directory.Build.props

**Files:**
- Create: `Directory.Build.props` (repo root)

- [ ] **Step 1: Create the file.**

Path: `Directory.Build.props` (repo root).

```xml
<Project>
  <!-- Cross-cutting test settings. Per-project test settings live in each .csproj. -->
</Project>
```

Keep it minimal. The runner stays on VSTest throughout the migration; no MTP-specific properties are added at any phase.

- [ ] **Step 2: Verify build still works.**

Run:
```
dotnet build Kartova.slnx -warnaserror
```
Expected: success. `Directory.Build.props` at the root applies to every project but adds no behavior yet.

- [ ] **Step 3: Commit.**

```
git add Directory.Build.props
git commit -m "chore(build): add root Directory.Build.props for cross-cutting test settings"
```

### Task 0.3: Verify global.json (no change expected)

**Files:**
- Read: `global.json`

- [ ] **Step 1: Inspect the SDK pin.**

Read `global.json`. The runner stays on VSTest, project SDK stays on `Microsoft.NET.Sdk`, and the only new packages (`MSTest.TestFramework`/`TestAdapter`/`Analyzers` 4.x) target .NET 8+ which is satisfied by the .NET 10 SDK. No `global.json` change is expected.

- [ ] **Step 2: Build verification.**

Run:
```
dotnet build Kartova.slnx -warnaserror
```
Expected: success.

- [ ] **Step 3: No commit needed.**

### Task 0.3a: CI workflows (no change expected)

**Files:**
- Read-only: `.github/workflows/*.yml`

Since the runner stays on VSTest, `dotnet test` invocations behave identically before and after the migration. No CI workflow changes are needed.

- [ ] **Step 1: Sanity check — full test run still green.**

```
dotnet test Kartova.slnx
```
Expected: exit code 0, all tests green.

- [ ] **Step 2: No commit needed.**

### Task 0.4: Capture mutation baseline for Stryker target projects

**Files:**
- Create: `docs/superpowers/specs/baselines/2026-05-08-mstest-migration-mutation-baseline.md`

- [ ] **Step 1: Run Stryker against the root config.**

Run:
```
cd Kartova.slnx-root
dotnet stryker -f stryker-config.json
```
(If `dotnet stryker` is not installed globally, install with `dotnet tool install -g dotnet-stryker` first.)

Expected: Stryker mutates `src/Modules/Catalog/**` and `src/Modules/Organization/**` per the per-module test projects. Capture the final mutation score per module from the output.

- [ ] **Step 2: Record the baseline.**

Create `docs/superpowers/specs/baselines/2026-05-08-mstest-migration-mutation-baseline.md`:

```markdown
# MSTest migration — mutation baseline (Phase 0)

**Date:** 2026-05-08
**Stryker config:** `stryker-config.json` (root)
**Targets per config:** `Kartova.Catalog.Tests`, `Kartova.Organization.Tests`

## Baseline scores (xUnit, pre-migration)

| Project | Mutation score | Killed | Survived | No coverage | Timeout |
|---|---|---|---|---|---|
| Kartova.Catalog.Tests | TBD% | n | n | n | n |
| Kartova.Organization.Tests | TBD% | n | n | n | n |

## Mutation gate

See "Per-phase mutation-gate ownership" section below for the canonical phase-to-target mapping (Phases 1, 2, 4, 5, 9, 10, 11, 12 are gate-owners per `mutation-targets.json` orchestration). Merge gate, secondary CompileError-delta check, and ±1pt regression budget all defined in this doc.
```

Replace `TBD%` and the counts with the actual values from the Stryker run in Step 1.

- [ ] **Step 3: Commit.**

```
git add docs/superpowers/specs/baselines/2026-05-08-mstest-migration-mutation-baseline.md
git commit -m "docs(test): capture mutation baseline before MSTest migration"
```

### Task 0.5: Audit FluentAssertions `BeEquivalentTo` sites

**Files:**
- Create: `docs/superpowers/specs/baselines/2026-05-08-mstest-migration-beequivalentto-audit.md` (only if any sites exist)

- [ ] **Step 1: Count occurrences across the entire repo.**

Run:
```
git grep -nE "BeEquivalentTo" -- "*.cs"
```

- [ ] **Step 2: Decide path forward.**

If 0 sites: no audit doc needed, proceed.

If 1–15 sites: list each in the audit doc and note "translate to per-property `Assert.AreEqual` at migration time" per spec §4.5.

If > 15 sites: create the audit doc, escalate the assertion-library decision to the user before continuing — escape hatch is to retain `FluentAssertions` for those specific files (or pivot to AwesomeAssertions for that subset). **Stop the phase and ask.**

- [ ] **Step 3: Commit (if audit doc was created).**

```
git add docs/superpowers/specs/baselines/2026-05-08-mstest-migration-beequivalentto-audit.md
git commit -m "docs(test): audit BeEquivalentTo sites for MSTest migration"
```

### Task 0.6: Stryker × MTP compatibility probe (already executed; outcome drove MTP-drop)

**Status:** Already complete. Result: **FAIL** — Stryker.NET (at the version probed in Phase 0; see baseline-doc §"Stryker × MTP compatibility probe") does not support Microsoft.Testing.Platform (tracked at [stryker-mutator/stryker-net#3094](https://github.com/stryker-mutator/stryker-net/issues/3094)).

**Outcome recorded in:** `docs/superpowers/specs/baselines/2026-05-08-mstest-migration-mutation-baseline.md` §"Stryker × MTP compatibility probe".

**Decision driven by this task:** MTP is dropped from this migration's scope entirely. All test projects stay on `Microsoft.NET.Sdk` + VSTest + `coverlet.collector` + `Microsoft.NET.Test.Sdk`. Phase 12 cleanup no longer flips any project to `MSTest.Sdk`. Tasks 12.3 (SDK flip) and 12.5 (coverlet → Microsoft.Testing.Extensions.CodeCoverage) are removed accordingly. Revisit MTP in a future migration once stryker-net#3094 closes.

**Why this stub remains in the plan:** preserves task IDs (engineers reaching here via cross-reference see the rationale rather than a missing section) and records the historical sequence — the probe was the deciding factor in the migration's MTP scope.

- [x] Already executed during Phase 0; no further action.

### Task 0.7: Write ADR-0097 superseding ADR-0083

**Files:**
- Create: `docs/architecture/decisions/ADR-0097-mstest-supersedes-xunit.md`

- [ ] **Step 1: Verify the ADR number is free.**

Run:
```
ls docs/architecture/decisions/ADR-0097-*
```
Expected: no matches. If anything exists, take the next free integer and adjust filenames + cross-references throughout this task.

- [ ] **Step 2: Write the ADR.**

Path: `docs/architecture/decisions/ADR-NNNN-mstest-supersedes-xunit.md` (use the next free integer).

Use the Michael Nygard template established by other ADRs in `docs/architecture/decisions/`. Body should follow the structure of ADR-0097 as committed (see commit `753c570` for the original draft and `55b4990` for the post-MTP-drop revision). Status `Accepted`. Supersedes `ADR-0083`.

(The actual body is recorded in ADR-0097 itself; do not duplicate inline here.)

- [ ] **Step 3: Commit.**

```
git add docs/architecture/decisions/ADR-0097-mstest-supersedes-xunit.md
git commit -m "docs(adr): ADR-0097 — MSTest v4 supersedes xUnit"
```

### Task 0.8: Update ADR-0083 status to Superseded

**Files:**
- Modify: `docs/architecture/decisions/ADR-0083-testing-strategy-with-architecture-tests.md` (status line only)

- [ ] **Step 1: Edit the status line.**

In `docs/architecture/decisions/ADR-0083-testing-strategy-with-architecture-tests.md`, change:

```
**Status:** Accepted
```

to:

```
**Status:** Superseded by ADR-0097 (test framework + assertion library)
```

Do not edit the body — ADRs are append-only after `Accepted`.

- [ ] **Step 2: Commit.**

```
git add docs/architecture/decisions/ADR-0083-testing-strategy-with-architecture-tests.md
git commit -m "docs(adr): mark ADR-0083 superseded by ADR-0097"
```

### Task 0.9: Update ADR README keyword index

**Files:**
- Modify: `docs/architecture/decisions/README.md`

- [ ] **Step 1: Read the file to find the existing keyword-index format.**

Read `docs/architecture/decisions/README.md` to learn the row shape (typically a markdown table with `Number | Title | Keywords`).

- [ ] **Step 2: Add ADR-0097 row.**

Add a row for ADR-0097 with keywords: `test framework, MSTest, mutation, FluentAssertions, NSubstitute, Testcontainers`.

If the README has a "Superseded" or "Replaces" column, mark the ADR-0083 row as `Superseded by 0097` and the ADR-0097 row as `Supersedes 0083`. Match whatever convention the README already uses.

- [ ] **Step 3: Verify file renders cleanly.**

Read the diff. If a markdown table column is mis-aligned, fix it.

- [ ] **Step 4: Commit.**

```
git add docs/architecture/decisions/README.md
git commit -m "docs(adr): index ADR-0097 in keyword catalog; mark 0083 superseded"
```

### Task 0.10: Update CLAUDE.md testing bullet

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Edit the testing line.**

In `CLAUDE.md`, find the "Architectural guardrails" section. Change:

```markdown
- **Testing:** five-tier pyramid — architecture (NetArchTest, mandatory CI gate) + unit + integration (Testcontainers) + contract (Pact) + E2E (Playwright) (ADR-0083)
```

to:

```markdown
- **Testing:** five-tier pyramid — architecture (NetArchTest, mandatory CI gate) + unit + integration (Testcontainers) + contract (Pact) + E2E (Playwright). Framework: MSTest v4 (VSTest runner unchanged); assertions: MSTest v4 native (no FluentAssertions); mocks: NSubstitute (ADR-0097, supersedes ADR-0083)
```

- [ ] **Step 2: Commit.**

```
git add CLAUDE.md
git commit -m "docs(claude): update testing bullet for MSTest (ADR-0097)"
```

### Task 0.11: Phase 0 verification

**Files:** Read-only.

- [ ] **Step 1: Full solution build.**

Run:
```
dotnet build Kartova.slnx -warnaserror
```
Expected: 0 warnings, 0 errors.

- [ ] **Step 2: Full xUnit test suite.**

Run:
```
dotnet test Kartova.slnx --no-build
```
Expected: all tests green. Capture the test count for cross-checks in later phases (e.g., `Total tests: 358 — Passed: 358`).

- [ ] **Step 3: Spot-check the new ADR.**

Read `docs/architecture/decisions/ADR-0097-*.md` end-to-end. Verify it cross-references ADR-0083 correctly and the body matches the decisions in spec §6.

- [ ] **Step 4: No commit needed; this is verification only.** Phase 0 is complete and ready for PR review.

---

## Phase 1 — `tests/Kartova.SharedKernel.Tests` (canonical pattern)

This phase is documented in full detail because it establishes the patterns reused in every later phase. Subsequent phases reference back here for "the canonical translation".

**Project:** `tests/Kartova.SharedKernel.Tests/Kartova.SharedKernel.Tests.csproj`
**Files in scope (~7):**
- `KartovaConnectionStringsTests.cs`
- `TenantContextAccessorTests.cs`
- `TenantScopeWolverineMiddlewareTests.cs`
- `Pagination/CursorCodecTests.cs`
- `Pagination/CursorFilterMismatchExceptionTests.cs`
- `Pagination/QueryablePagingExtensionsTests.cs`
- `Pagination/SortSpecTests.cs`

### Task 1.1: Add MSTest packages to the project

**Files:**
- Modify: `tests/Kartova.SharedKernel.Tests/Kartova.SharedKernel.Tests.csproj`

- [ ] **Step 1: Add the MSTest references alongside the existing xUnit ones.**

In `Kartova.SharedKernel.Tests.csproj`, in the existing `<ItemGroup>` that lists test packages, add three lines (xUnit references stay for now):

```xml
    <PackageReference Include="MSTest.TestFramework" />
    <PackageReference Include="MSTest.TestAdapter" />
    <PackageReference Include="MSTest.Analyzers" />
```

The full ItemGroup will look like:
```xml
  <ItemGroup>
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="MSTest.TestFramework" />
    <PackageReference Include="MSTest.TestAdapter" />
    <PackageReference Include="MSTest.Analyzers" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="FluentAssertions" />
  </ItemGroup>
```

- [ ] **Step 2: Verify build.**

Run:
```
dotnet build tests/Kartova.SharedKernel.Tests/Kartova.SharedKernel.Tests.csproj -warnaserror
```
Expected: success, 0 warnings.

- [ ] **Step 3: Verify tests still discover and pass.**

Run:
```
dotnet test tests/Kartova.SharedKernel.Tests/Kartova.SharedKernel.Tests.csproj --no-build
```
Expected: same xUnit test count as before, all green. (Both runners scan the assembly; MSTest finds nothing yet because no `[TestMethod]` exists.)

- [ ] **Step 4: Commit.**

```
git add tests/Kartova.SharedKernel.Tests/Kartova.SharedKernel.Tests.csproj
git commit -m "chore(test): add MSTest v4 packages to Kartova.SharedKernel.Tests"
```

### Task 1.2: Translate `KartovaConnectionStringsTests.cs` (full canonical example)

**Files:**
- Modify: `tests/Kartova.SharedKernel.Tests/KartovaConnectionStringsTests.cs`

This file is shown fully translated as the canonical exhibit. Subsequent files in this phase apply the same pattern.

- [ ] **Step 1: Replace the file content end-to-end.**

Read the original file (it has 5 tests covering `Require`, `RequireMain`, `RequireBypass`). Replace with:

```csharp
using System.Text.RegularExpressions;
using Kartova.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.SharedKernel.Tests;

[TestClass]
public class KartovaConnectionStringsTests
{
    [TestMethod]
    public void Require_returns_value_when_connection_string_is_present()
    {
        var config = BuildConfig(("ConnectionStrings:Kartova", "Host=db;Database=k"));

        var cs = KartovaConnectionStrings.Require(config, KartovaConnectionStrings.Main);

        Assert.AreEqual("Host=db;Database=k", cs);
    }

    [TestMethod]
    public void Require_throws_with_canonical_message_when_missing()
    {
        var config = BuildConfig();

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => KartovaConnectionStrings.Require(config, KartovaConnectionStrings.Main));

        // Stable diagnostic shape — Program.cs and module RegisterForMigrator
        // calls all surface this message; CI logs scrape it on bootstrap failures.
        Assert.AreEqual(
            "Connection string 'Kartova' is required. Set it via ConnectionStrings__Kartova env var.",
            ex.Message);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Require_throws_when_connection_string_is_blank(string blank)
    {
        var config = BuildConfig(("ConnectionStrings:Kartova", blank));

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => KartovaConnectionStrings.Require(config, KartovaConnectionStrings.Main));

        StringAssert.Matches(ex.Message, new Regex("Kartova.*required"));
    }

    [TestMethod]
    public void RequireMain_resolves_against_Kartova_key()
    {
        var config = BuildConfig(("ConnectionStrings:Kartova", "main-cs"));

        Assert.AreEqual("main-cs", KartovaConnectionStrings.RequireMain(config));
    }

    [TestMethod]
    public void RequireBypass_resolves_against_KartovaBypass_key()
    {
        var config = BuildConfig(("ConnectionStrings:KartovaBypass", "bypass-cs"));

        Assert.AreEqual("bypass-cs", KartovaConnectionStrings.RequireBypass(config));
    }

    private static IConfiguration BuildConfig(params (string Key, string Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)))
            .Build();
}
```

Translation rules applied (per spec §4):
- `using Xunit;` + `using FluentAssertions;` → `using Microsoft.VisualStudio.TestTools.UnitTesting;` (+ `using System.Text.RegularExpressions;` for the regex match).
- Class is `[TestClass]`-decorated.
- `[Fact]` → `[TestMethod]`.
- `[Theory] [InlineData("")] [InlineData("   ")]` → `[TestMethod] [DataRow("")] [DataRow("   ")]`.
- `cs.Should().Be("...")` → `Assert.AreEqual("...", cs)` (note: MSTest puts expected first).
- `act.Should().Throw<T>().WithMessage("exact")` → `var ex = Assert.ThrowsExactly<T>(act); Assert.AreEqual("exact", ex.Message);`.
- `act.Should().Throw<T>().WithMessage("*Kartova*required*")` (FA wildcard) → `var ex = Assert.ThrowsExactly<T>(act); StringAssert.Matches(ex.Message, new Regex("Kartova.*required"));` (FA `*` becomes regex `.*`).

- [ ] **Step 2: Build the project.**

Run:
```
dotnet build tests/Kartova.SharedKernel.Tests/Kartova.SharedKernel.Tests.csproj -warnaserror
```
Expected: success.

- [ ] **Step 3: Run tests for this project.**

Run:
```
dotnet test tests/Kartova.SharedKernel.Tests/Kartova.SharedKernel.Tests.csproj --no-build
```
Expected: same total test count as before. All MSTest tests in this file run via MSTest adapter; other files still on xUnit.

- [ ] **Step 4: Commit.**

```
git add tests/Kartova.SharedKernel.Tests/KartovaConnectionStringsTests.cs
git commit -m "test(sharedkernel): migrate KartovaConnectionStringsTests to MSTest v4"
```

### Task 1.3: Translate `Pagination/SortSpecTests.cs`

**Files:**
- Modify: `tests/Kartova.SharedKernel.Tests/Pagination/SortSpecTests.cs`

- [ ] **Step 1: Replace the file content.**

Apply spec §4 translation rules to the existing file. The rewritten content is:

```csharp
using Kartova.SharedKernel.Pagination;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.SharedKernel.Tests.Pagination;

[TestClass]
public sealed class SortSpecTests
{
    private sealed record SampleEntity(string Name, DateTimeOffset CreatedAt, Guid Id);

    [TestMethod]
    public void Construction_captures_field_name_and_key_selector()
    {
        var spec = new SortSpec<SampleEntity>("name", x => x.Name);

        Assert.AreEqual("name", spec.FieldName);
        Assert.AreEqual(
            "x",
            spec.KeySelector.Compile().Invoke(new SampleEntity("x", DateTimeOffset.UtcNow, Guid.NewGuid())));
    }

    [TestMethod]
    public void Two_specs_with_different_lambda_instances_are_not_value_equal()
    {
        var a = new SortSpec<SampleEntity>("name", x => x.Name);
        var b = new SortSpec<SampleEntity>("name", x => x.Name);

        // SortSpec is a record, but its KeySelector is an Expression<> with reference-only equality.
        // Two specs with the same field name but distinct lambda literals are NOT value-equal.
        // Callers MUST treat SortSpec by FieldName, not by record equality. ADR-0095 §5.
        Assert.AreNotEqual(b, a);
        Assert.AreEqual(b.FieldName, a.FieldName);
    }

    [TestMethod]
    public void CompiledKeySelector_caches_the_delegate_across_accesses()
    {
        // Kills mutant at line 24: `_compiled ??= KeySelector.Compile()` mutated to `_compiled = KeySelector.Compile()`.
        // With original ??= the first access compiles once and caches; subsequent accesses return the same instance.
        // With mutated =, every access recompiles, producing a new delegate instance → AreSame fails.
        var spec = new SortSpec<SampleEntity>("name", x => x.Name);

        var first = spec.CompiledKeySelector;
        var second = spec.CompiledKeySelector;
        var third = spec.CompiledKeySelector;

        Assert.AreSame(second, first);
        Assert.AreSame(third, second);
    }
}
```

Translation rules applied:
- `using Xunit;` + `using FluentAssertions;` → `using Microsoft.VisualStudio.TestTools.UnitTesting;`.
- `[Fact]` → `[TestMethod]`.
- `x.Should().Be(y)` → `Assert.AreEqual(y, x)`.
- `x.Should().NotBe(y, "reason")` → `Assert.AreNotEqual(y, x)` (FA "reason" string has no MSTest equivalent — drop it; the comment line above the assert preserves the rationale).
- `x.Should().BeSameAs(y)` → `Assert.AreSame(y, x)`.

- [ ] **Step 2: Build.**

```
dotnet build tests/Kartova.SharedKernel.Tests/Kartova.SharedKernel.Tests.csproj -warnaserror
```

- [ ] **Step 3: Test.**

```
dotnet test tests/Kartova.SharedKernel.Tests/Kartova.SharedKernel.Tests.csproj --no-build
```

- [ ] **Step 4: Commit.**

```
git add tests/Kartova.SharedKernel.Tests/Pagination/SortSpecTests.cs
git commit -m "test(sharedkernel): migrate SortSpecTests to MSTest v4"
```

### Task 1.4: Translate remaining files in `Kartova.SharedKernel.Tests`

Each of the following files is its own micro-task. Apply spec §4 translation rules. Build + test + commit per file.

**Files to translate (apply pattern from Tasks 1.2 and 1.3):**

- [ ] **Step 1: Translate `tests/Kartova.SharedKernel.Tests/TenantContextAccessorTests.cs`.**

  Apply spec §4 rules. Build, test, commit:
  ```
  dotnet build tests/Kartova.SharedKernel.Tests/Kartova.SharedKernel.Tests.csproj -warnaserror
  dotnet test  tests/Kartova.SharedKernel.Tests/Kartova.SharedKernel.Tests.csproj --no-build
  git add tests/Kartova.SharedKernel.Tests/TenantContextAccessorTests.cs
  git commit -m "test(sharedkernel): migrate TenantContextAccessorTests to MSTest v4"
  ```

- [ ] **Step 2: Translate `tests/Kartova.SharedKernel.Tests/TenantScopeWolverineMiddlewareTests.cs`.**

  Same build/test/commit rhythm.

- [ ] **Step 3: Translate `tests/Kartova.SharedKernel.Tests/Pagination/CursorCodecTests.cs`.**

  This file uses `[Theory] [InlineData(...)]` heavily — apply `[TestMethod] [DataRow(...)]` per spec §4.2.

- [ ] **Step 4: Translate `tests/Kartova.SharedKernel.Tests/Pagination/CursorFilterMismatchExceptionTests.cs`.**

  Same rhythm.

- [ ] **Step 5: Translate `tests/Kartova.SharedKernel.Tests/Pagination/QueryablePagingExtensionsTests.cs`.**

  This file uses `IAsyncLifetime` (per spec §4.1, audit point). Determine whether the lifetime is per-test or per-class:
  - If the lifetime is per-test (xUnit's default with `IAsyncLifetime` on the test class itself), translate to `[TestInitialize] public async Task TestInit()` + `[TestCleanup] public async Task TestCleanup()`.
  - If per-class (rare; only when the type is a fixture pulled in via `IClassFixture<T>`), use `[ClassInitialize] public static async Task ClassInit(TestContext _)` + `[ClassCleanup] public static async Task ClassCleanup()`.

  Same build/test/commit rhythm.

### Task 1.5: Drop xUnit references from the project

**Files:**
- Modify: `tests/Kartova.SharedKernel.Tests/Kartova.SharedKernel.Tests.csproj`

- [ ] **Step 1: Verify no xUnit attributes remain.**

Run:
```
git grep -nE "\[Fact\]|\[Theory\]|using Xunit|FluentAssertions" -- tests/Kartova.SharedKernel.Tests
```
Expected: no matches. If anything is found, fix the offending file before continuing.

- [ ] **Step 2: Remove xUnit and FluentAssertions package references from the csproj.**

In `Kartova.SharedKernel.Tests.csproj`, delete:

```xml
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="FluentAssertions" />
```

The remaining ItemGroup references just `coverlet.collector`, EF.Sqlite, Microsoft.NET.Test.Sdk, and the three MSTest packages.

- [ ] **Step 3: Build + test.**

```
dotnet build tests/Kartova.SharedKernel.Tests/Kartova.SharedKernel.Tests.csproj -warnaserror
dotnet test tests/Kartova.SharedKernel.Tests/Kartova.SharedKernel.Tests.csproj --no-build
```
Expected: 0 warnings, all tests green, total count matches Phase 0 baseline for this project.

- [ ] **Step 4: Commit.**

```
git add tests/Kartova.SharedKernel.Tests/Kartova.SharedKernel.Tests.csproj
git commit -m "chore(test): remove xUnit + FluentAssertions from Kartova.SharedKernel.Tests"
```

### Task 1.6: Phase 1 verification

- [ ] **Step 1: Full solution build.**

```
dotnet build Kartova.slnx -warnaserror
```
Expected: 0 warnings, 0 errors. (xUnit references in *other* projects still resolve via CPM.)

- [ ] **Step 2: Full solution test run.**

```
dotnet test Kartova.slnx --no-build
```
Expected: same total test count as Phase 0 baseline; all green.

- [ ] **Step 3: Spot-check a translated file in Visual Studio / Rider Test Explorer.**

Open one of the translated test classes in IDE Test Explorer; confirm tests appear under the MSTest discovery path with green check marks after running.

- [ ] **Step 4: Mutation regression — `Kartova.SharedKernel`.**

Run per-project Stryker against `Kartova.SharedKernel`:
```
dotnet stryker -f src/Kartova.SharedKernel/stryker-config.json
```
Expected: mutation score within ±1pt of baseline (75.00% per `docs/superpowers/specs/baselines/2026-05-08-mstest-migration-mutation-baseline.md`). If outside ±1pt, see the merge-gate language in that doc and the per-phase ownership table.

(Per-project config used per the §Stryker invocation note at the top of this plan.)

Phase 1 is complete and ready for PR review.

---

## Phase 2 — `tests/Kartova.SharedKernel.AspNetCore.Tests`

**Project:** `tests/Kartova.SharedKernel.AspNetCore.Tests/Kartova.SharedKernel.AspNetCore.Tests.csproj`
**Files in scope (~12):** All `*.cs` files directly under the project (test files only — no infra files exist here).

### Task 2.1: Add MSTest packages to the project

**Files:**
- Modify: `tests/Kartova.SharedKernel.AspNetCore.Tests/Kartova.SharedKernel.AspNetCore.Tests.csproj`

- [ ] **Step 1: Add the three MSTest references alongside existing xUnit ones.**

Same shape as Task 1.1. Add inside the existing test-package `<ItemGroup>`:

```xml
    <PackageReference Include="MSTest.TestFramework" />
    <PackageReference Include="MSTest.TestAdapter" />
    <PackageReference Include="MSTest.Analyzers" />
```

- [ ] **Step 2: Verify build + tests.**

```
dotnet build tests/Kartova.SharedKernel.AspNetCore.Tests/Kartova.SharedKernel.AspNetCore.Tests.csproj -warnaserror
dotnet test tests/Kartova.SharedKernel.AspNetCore.Tests/Kartova.SharedKernel.AspNetCore.Tests.csproj --no-build
```

- [ ] **Step 3: Commit.**

```
git add tests/Kartova.SharedKernel.AspNetCore.Tests/Kartova.SharedKernel.AspNetCore.Tests.csproj
git commit -m "chore(test): add MSTest v4 packages to Kartova.SharedKernel.AspNetCore.Tests"
```

### Task 2.2: Translate every test file in `Kartova.SharedKernel.AspNetCore.Tests`

Apply spec §4 rules to each. Heavy NSubstitute usage — leave NSubstitute calls untouched (`Substitute.For<T>()`, `.Received()`, etc. are framework-agnostic). Watch for `IAsyncLifetime` sites and `ITestOutputHelper` (translate to `TestContext`).

For each file: edit, build, test, commit individually.

- [ ] **Step 1: `ConcurrencyConflictExceptionHandlerTests.cs`** — translate, build, test, commit.
- [ ] **Step 2: `DomainValidationExceptionHandlerTests.cs`** — translate, build, test, commit.
- [ ] **Step 3: `HttpContextCurrentUserTests.cs`** — translate, build, test, commit.
- [ ] **Step 4: `IfMatchEndpointFilterTests.cs`** — translate, build, test, commit.
- [ ] **Step 5: `JwtAuthenticationExtensionsTests.cs`** — translate, build, test, commit.
- [ ] **Step 6: `LifecycleConflictExceptionHandlerTests.cs`** — translate, build, test, commit.
- [ ] **Step 7: `ModuleRouteExtensionsTests.cs`** — translate, build, test, commit.
- [ ] **Step 8: `PagingExceptionHandlerTests.cs`** — translate, build, test, commit.
- [ ] **Step 9: `PreconditionRequiredExceptionHandlerTests.cs`** — translate, build, test, commit.
- [ ] **Step 10: `TenantClaimsTransformationTests.cs`** — translate, build, test, commit.
- [ ] **Step 11: `TenantScopeCommitEndpointFilterTests.cs`** — translate, build, test, commit.
- [ ] **Step 12: `VersionEncodingTests.cs`** — translate, build, test, commit.

For each: build/test commands are identical to Phase 1:
```
dotnet build tests/Kartova.SharedKernel.AspNetCore.Tests/Kartova.SharedKernel.AspNetCore.Tests.csproj -warnaserror
dotnet test tests/Kartova.SharedKernel.AspNetCore.Tests/Kartova.SharedKernel.AspNetCore.Tests.csproj --no-build
git add tests/Kartova.SharedKernel.AspNetCore.Tests/<File>.cs
git commit -m "test(aspnetcore): migrate <File> to MSTest v4"
```

### Task 2.3: Drop xUnit references from the project

**Files:**
- Modify: `tests/Kartova.SharedKernel.AspNetCore.Tests/Kartova.SharedKernel.AspNetCore.Tests.csproj`

- [ ] **Step 1: Verify no xUnit attributes remain.**

```
git grep -nE "\[Fact\]|\[Theory\]|using Xunit|FluentAssertions" -- tests/Kartova.SharedKernel.AspNetCore.Tests
```

- [ ] **Step 2: Remove the three xUnit/FA references.** Same as Task 1.5 Step 2.

- [ ] **Step 3: Build + test, full solution.**

```
dotnet build Kartova.slnx -warnaserror
dotnet test Kartova.slnx --no-build
```

- [ ] **Step 4: Commit.**

```
git add tests/Kartova.SharedKernel.AspNetCore.Tests/Kartova.SharedKernel.AspNetCore.Tests.csproj
git commit -m "chore(test): remove xUnit + FluentAssertions from Kartova.SharedKernel.AspNetCore.Tests"
```

- [ ] **Step 5: Mutation regression — `Kartova.SharedKernel.AspNetCore` (interim score; Phase 11 is the official gate).**

Run per-project Stryker against `Kartova.SharedKernel.AspNetCore`:
```
dotnet stryker -f src/Kartova.SharedKernel.AspNetCore/stryker-config.json
```
Phase 2 is the **primary owner** of this mutation target but **co-driven with Phase 11** (the AspNetCore Stryker config also feeds `tests/Kartova.Api.IntegrationTests`, which is still on xUnit at this point). The score captured here is an **interim diagnostic** — a >1pt drift vs the 100.00% baseline (per `docs/superpowers/specs/baselines/2026-05-08-mstest-migration-mutation-baseline.md`) flags a translation defect in this phase to investigate before Phase 11. The official gate runs at Phase 11 once both driving test suites are on MSTest.

(Per-project config used per the §Stryker invocation note at the top of this plan.)

Phase 2 complete.

---

## Phase 3 — `tests/Kartova.ArchitectureTests`

**Project:** `tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj`
**Files in scope (~13):** All `*.cs` test files. Heavy `[Theory] + [InlineData]` and `[MemberData]` usage.

**Special: this project has `<Using Include="Xunit" />` in the csproj.** The global using makes xUnit attributes available without explicit `using Xunit;` in source files. After translation, this global using becomes harmful (clashes with MSTest's `[TestClass]`). Approach: change the global using to `Microsoft.VisualStudio.TestTools.UnitTesting` after all files migrated.

### Task 3.1: Add MSTest packages to the project

**Files:**
- Modify: `tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj`

- [ ] **Step 1: Add MSTest references alongside xUnit.**

Same as Task 1.1. Three new `<PackageReference>` lines.

- [ ] **Step 2: Add a global `using` for MSTest namespace alongside the existing `using Xunit;` global.**

In the `<ItemGroup>` containing `<Using Include="Xunit" />`, add:

```xml
    <Using Include="Microsoft.VisualStudio.TestTools.UnitTesting" />
```

This makes both namespaces globally available; MSTest's `[TestMethod]` and xUnit's `[Fact]` both resolve. Source files don't need `using` lines for either.

- [ ] **Step 3: Build + test.**

```
dotnet build tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj -warnaserror
dotnet test tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj --no-build
```
Expected: success, all xUnit tests still green.

- [ ] **Step 4: Commit.**

```
git add tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj
git commit -m "chore(test): add MSTest v4 packages to Kartova.ArchitectureTests"
```

### Task 3.2: Translate each architecture test file

Apply spec §4 rules. Most files use `[Theory] [InlineData(...)]` — translate to `[TestMethod] [DataRow(...)]`. A few use `[MemberData]` — translate to `[DynamicData]` per spec §4.2.

**Important note for `[DynamicData]`:** the source method must be `static`. Verify after translation; if the source method is instance-level in xUnit (allowed there), promote to static.

For each file: edit, build, test, commit.

- [ ] **Step 1: `AssemblyRegistry.cs`** — this is infrastructure, not a test class. Skip if no `[Fact]`/`[Theory]` attributes.
- [ ] **Step 2: `CleanArchitectureLayerTests.cs`** — translate, build, test, commit.
- [ ] **Step 3: `ContractsCoverageRules.cs`** — translate, build, test, commit.
- [ ] **Step 4: `DiLifetimeRules.cs`** — translate, build, test, commit.
- [ ] **Step 5: `EndpointRouteRules.cs`** — translate, build, test, commit.
- [ ] **Step 6: `ForbiddenDependencyTests.cs`** — translate, build, test, commit.
- [ ] **Step 7: `IModuleRules.cs`** — translate, build, test, commit.
- [ ] **Step 8: `KeycloakRealmSeedRules.cs`** — translate, build, test, commit.
- [ ] **Step 9: `LifecycleEnumRules.cs`** — translate, build, test, commit.
- [ ] **Step 10: `ModuleBoundaryTests.cs`** — translate, build, test, commit.
- [ ] **Step 11: `PaginationConventionRules.cs`** — translate, build, test, commit.
- [ ] **Step 12: `ProblemDetailsConventionRules.cs`** — translate, build, test, commit.
- [ ] **Step 13: `RestVerbPolicyRules.cs`** — translate, build, test, commit.
- [ ] **Step 14: `TenantScopeRules.cs`** — translate, build, test, commit.
- [ ] **Step 15: `WolverinePersistenceBoundaryTests.cs`** — translate, build, test, commit.

For each: same build/test/commit rhythm as Phase 1.

### Task 3.3: Drop xUnit global using and references

**Files:**
- Modify: `tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj`

- [ ] **Step 1: Verify no xUnit attributes remain.**

```
git grep -nE "\[Fact\]|\[Theory\]|FluentAssertions" -- tests/Kartova.ArchitectureTests
```

- [ ] **Step 2: Remove `<Using Include="Xunit" />` and the three xUnit + FA package references.**

Final csproj has only the MSTest global using and MSTest packages.

- [ ] **Step 3: Build + test, full solution.**

```
dotnet build Kartova.slnx -warnaserror
dotnet test Kartova.slnx --no-build
```

- [ ] **Step 4: Commit.**

```
git add tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj
git commit -m "chore(test): remove xUnit + FluentAssertions from Kartova.ArchitectureTests"
```

Phase 3 complete.

---

## Phase 4 — `src/Modules/Catalog/Kartova.Catalog.Tests` (Stryker target)

**Project:** `src/Modules/Catalog/Kartova.Catalog.Tests/Kartova.Catalog.Tests.csproj`
**Files in scope (~7):** `ApplicationTests.cs`, `ApplicationLifecycleTests.cs`, `CatalogAssemblyLoadsTests.cs`, `EfApplicationConfigurationTests.cs`, `InvalidLifecycleTransitionExceptionTests.cs`, `ListApplicationsHandlerFilterTests.cs`, `ListApplicationsHandlerTests.cs`.

**Stryker gate:** mutation score must be within ±1pt of the Phase 0 baseline.

### Task 4.1: Add MSTest packages

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Tests/Kartova.Catalog.Tests.csproj`

- [ ] **Step 1: Add the three MSTest package references alongside xUnit.**
- [ ] **Step 2: Build + test (project-level).**
- [ ] **Step 3: Commit.**

Commands and shape identical to Task 1.1, with paths adjusted.

### Task 4.2: Translate each test file

Apply spec §4. Each file is a separate commit.

- [ ] **Step 1: `ApplicationTests.cs`** — translate, build, test, commit.
- [ ] **Step 2: `ApplicationLifecycleTests.cs`** — translate, build, test, commit.
- [ ] **Step 3: `CatalogAssemblyLoadsTests.cs`** — translate, build, test, commit.
- [ ] **Step 4: `EfApplicationConfigurationTests.cs`** — translate, build, test, commit.
- [ ] **Step 5: `InvalidLifecycleTransitionExceptionTests.cs`** — translate, build, test, commit.
- [ ] **Step 6: `ListApplicationsHandlerFilterTests.cs`** — translate, build, test, commit.
- [ ] **Step 7: `ListApplicationsHandlerTests.cs`** — translate, build, test, commit.

### Task 4.3: Drop xUnit references

Same shape as Task 1.5. Build + test verification, single commit.

### Task 4.4: Mutation regression check

**Files:** Read-only verification.

- [ ] **Step 1: Run Stryker against the Catalog target only.**

Run from repo root:
```
dotnet stryker -tp src/Modules/Catalog/Kartova.Catalog.Tests/Kartova.Catalog.Tests.csproj -m "src/Modules/Catalog/**/*.cs"
```
Or use the existing `src/Modules/Catalog/stryker-config.json` if it scopes to Catalog only:
```
cd src/Modules/Catalog
dotnet stryker -f stryker-config.json
```

Expected: mutation score within ±1pt of the baseline recorded in `docs/superpowers/specs/baselines/2026-05-08-mstest-migration-mutation-baseline.md` for `Kartova.Catalog.Tests`.

- [ ] **Step 2: Append result to baseline doc.**

Edit `docs/superpowers/specs/baselines/2026-05-08-mstest-migration-mutation-baseline.md`. Add a new section:

```markdown
## Phase 4 verification

| Project | Baseline | Phase 4 | Δ |
|---|---|---|---|
| Kartova.Catalog.Tests | TBD% | TBD% | +/- TBDpt |
```

If Δ > 1pt: **stop** and investigate before merging Phase 4. Surviving mutants likely indicate a behavioral diff (e.g., MSTest `Assert.AreEqual` argument order accidentally swapped, weakening the assertion).

- [ ] **Step 3: Commit baseline update.**

```
git add docs/superpowers/specs/baselines/2026-05-08-mstest-migration-mutation-baseline.md
git commit -m "docs(test): record Phase 4 mutation score for Kartova.Catalog.Tests"
```

Phase 4 complete.

---

## Phase 5 — `src/Modules/Organization/Kartova.Organization.Tests` (Stryker target)

**Project:** `src/Modules/Organization/Kartova.Organization.Tests/Kartova.Organization.Tests.csproj`
**Files in scope (~1):** `OrganizationAggregateTests.cs`.

**Stryker gate:** mutation score within ±1pt of baseline.

### Task 5.1: Add MSTest packages

Same shape as Task 4.1.

### Task 5.2: Translate `OrganizationAggregateTests.cs`

- [ ] **Step 1: Edit the file applying spec §4 rules.**
- [ ] **Step 2: Build + test.**
- [ ] **Step 3: Commit.**

### Task 5.3: Drop xUnit references

Same shape as Task 1.5.

### Task 5.4: Mutation regression check

Same shape as Task 4.4 but for Kartova.Organization.Tests. Append result to baseline doc, commit.

Phase 5 complete.

---

## Phase 6 — `src/Modules/Catalog/Kartova.Catalog.Infrastructure.Tests`

**Project:** `src/Modules/Catalog/Kartova.Catalog.Infrastructure.Tests/Kartova.Catalog.Infrastructure.Tests.csproj`
**Files in scope (~1):** `CatalogModuleRegisterForMigratorTests.cs`.

### Task 6.1: Add MSTest packages

Same shape as Task 4.1.

### Task 6.2: Translate `CatalogModuleRegisterForMigratorTests.cs`

- [ ] **Step 1: Edit per spec §4.**
- [ ] **Step 2: Build + test.**
- [ ] **Step 3: Commit.**

### Task 6.3: Drop xUnit references

Same as Task 1.5.

Phase 6 complete.

---

## Phase 7 — `src/Modules/Organization/Kartova.Organization.Infrastructure.Tests`

**Project:** `src/Modules/Organization/Kartova.Organization.Infrastructure.Tests/Kartova.Organization.Infrastructure.Tests.csproj`
**Files in scope (~1):** `OrganizationModuleRegisterForMigratorTests.cs`.

### Task 7.1: Add MSTest packages

Same shape as Task 4.1.

### Task 7.2: Translate `OrganizationModuleRegisterForMigratorTests.cs`

- [ ] **Step 1: Edit per spec §4.**
- [ ] **Step 2: Build + test.**
- [ ] **Step 3: Commit.**

### Task 7.3: Drop xUnit references

Same as Task 1.5.

Phase 7 complete.

---

## Phase 8 — `tests/Kartova.Testing.Auth` (additive contract change)

**Project:** `tests/Kartova.Testing.Auth/Kartova.Testing.Auth.csproj`. Not a test project — infra. No translation; only contract change.

**Goal:** make `KartovaApiFixtureBase` usable by both xUnit `IClassFixture<T>` consumers (Phases 9–10 not yet migrated) **and** MSTest `[ClassInitialize]` consumers (post-migration). Existing `IAsyncLifetime` interface remains; new `IAsyncDisposable` is added alongside.

### Task 8.1: Make `KartovaApiFixtureBase` implement `IAsyncDisposable` alongside `IAsyncLifetime`

**Files:**
- Modify: `tests/Kartova.Testing.Auth/KartovaApiFixtureBase.cs`

- [ ] **Step 1: Edit the class declaration and disposal methods.**

Read the current file first to confirm structure. Then make these edits:

In the class declaration, add `IAsyncDisposable`:

Before:
```csharp
public abstract class KartovaApiFixtureBase : WebApplicationFactory<Program>, IAsyncLifetime
```

After:
```csharp
public abstract class KartovaApiFixtureBase
    : WebApplicationFactory<Program>, IAsyncLifetime, IAsyncDisposable
```

Replace the existing disposal:

Before:
```csharp
async Task IAsyncLifetime.DisposeAsync()
{
    await _pg.DisposeAsync();
    await base.DisposeAsync();
}
```

After:
```csharp
Task IAsyncLifetime.DisposeAsync() => DisposeAsyncCore();

async ValueTask IAsyncDisposable.DisposeAsync()
{
    await DisposeAsyncCore();
    GC.SuppressFinalize(this);
}

private async Task DisposeAsyncCore()
{
    await _pg.DisposeAsync();
    await base.DisposeAsync();
}
```

This routes both interfaces through the same teardown body. xUnit consumers that call `IAsyncLifetime.DisposeAsync()` keep working unchanged. MSTest consumers in Phases 9–10 can call `await ((IAsyncDisposable)fixture).DisposeAsync()`.

- [ ] **Step 2: Add an XML doc snippet on `InitializeAsync()` documenting the MSTest consumer pattern.**

Add this XML doc above the `public async Task InitializeAsync()` method (replace any existing summary):

```csharp
/// <summary>
/// Spins up the Postgres container and applies module migrations. Call once per
/// fixture lifetime — typically from <c>[ClassInitialize]</c> in MSTest test
/// classes (semantic equivalent of xUnit's <c>IAsyncLifetime.InitializeAsync</c>).
/// </summary>
/// <remarks>
/// MSTest consumer pattern (Phases 9–10):
/// <code>
/// [TestClass]
/// public abstract class CatalogIntegrationTestBase
/// {
///     protected static KartovaApiFixture Fx { get; private set; } = null!;
///
///     [ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]
///     public static async Task ClassInit(TestContext _)
///     {
///         Fx = new KartovaApiFixture();
///         await Fx.InitializeAsync();
///     }
///
///     [ClassCleanup(InheritanceBehavior.BeforeEachDerivedClass)]
///     public static async Task ClassDone() =&gt; await ((IAsyncDisposable)Fx).DisposeAsync();
/// }
/// </code>
/// </remarks>
public async Task InitializeAsync()
{
    // existing body unchanged
    ...
}
```

- [ ] **Step 3: Build the full solution.**

```
dotnet build Kartova.slnx -warnaserror
```
Expected: success. xUnit consumer projects (Phases 9–10 still on xUnit) keep building because `IAsyncLifetime` is still implemented.

- [ ] **Step 4: Run the full test suite.**

```
dotnet test Kartova.slnx --no-build
```
Expected: all green. The contract change is additive, so behavior of existing tests is unchanged.

- [ ] **Step 5: Commit.**

```
git add tests/Kartova.Testing.Auth/KartovaApiFixtureBase.cs
git commit -m "test(auth): KartovaApiFixtureBase additionally implements IAsyncDisposable for MSTest consumers"
```

Phase 8 complete. No package or csproj changes — those happen in Phase 12.

---

## Phase 9 — `src/Modules/Catalog/Kartova.Catalog.IntegrationTests`

**Project:** `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj`
**Files in scope (~9 + fixtures):**
- Test classes: `RegisterApplicationTests.cs`, `EditApplicationTests.cs`, `DeprecateApplicationTests.cs`, `DecommissionApplicationTests.cs`, `CrossTenantWriteTests.cs`, `ListApplicationsPaginationTests.cs`, `Migrations/MigrationIntegrationTests.cs`.
- Fixtures: `KartovaApiFixture.cs`, `KartovaApiCollection.cs` (delete), `Fixtures/PostgresFixture.cs`.

**Special:** uses `KartovaApiFixtureBase` (Phase 8 contract). Consumes via `KartovaApiCollection : ICollectionFixture<KartovaApiFixture>` today.

### Task 9.1: Add MSTest packages and `[assembly: DoNotParallelize]`

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj`
- Create: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Properties/AssemblyInfo.cs`

- [ ] **Step 1: Add MSTest references alongside xUnit.** Same shape as Task 1.1.

- [ ] **Step 2: Create `Properties/AssemblyInfo.cs`.**

Path: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Properties/AssemblyInfo.cs`. Content:

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;

// Preserves the env-var-race protection that xUnit's [Collection] previously provided —
// integration tests touch ConnectionStrings__* and Authentication__* env vars that are
// process-global. Running classes in parallel would clobber each other's state.
[assembly: DoNotParallelize]
```

- [ ] **Step 3: Build + test.**

```
dotnet build src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj -warnaserror
dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --no-build
```

- [ ] **Step 4: Commit.**

```
git add src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Properties/AssemblyInfo.cs
git commit -m "chore(test): add MSTest v4 + DoNotParallelize to Kartova.Catalog.IntegrationTests"
```

### Task 9.2: Migrate `KartovaApiFixture` (drop xUnit `IAsyncLifetime` consumption)

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/KartovaApiFixture.cs` (currently a thin derivative of `KartovaApiFixtureBase`)

- [ ] **Step 1: Read the current file to understand what it overrides.**

Confirm the class derives from `KartovaApiFixtureBase` and implements `RunModuleMigrationsAsync`. No changes are needed to that override.

- [ ] **Step 2: Drop any `using Xunit;` lines.**

The fixture itself doesn't implement `IAsyncLifetime` directly — its base does. The base still implements `IAsyncLifetime` (Phase 8 made it additive). No interface change here. If the file has `using Xunit;`, remove it (no longer required since the new MSTest consumer pattern is used).

- [ ] **Step 3: Build.**

```
dotnet build src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj -warnaserror
```

- [ ] **Step 4: Commit (if anything changed).**

```
git add src/Modules/Catalog/Kartova.Catalog.IntegrationTests/KartovaApiFixture.cs
git commit -m "test(catalog): trim xUnit using from KartovaApiFixture"
```

If no changes were needed, skip the commit.

### Task 9.3: Migrate `Fixtures/PostgresFixture.cs` to plain `IAsyncDisposable`

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Fixtures/PostgresFixture.cs`

- [ ] **Step 1: Drop `IAsyncLifetime`, implement `IAsyncDisposable`.**

Before:
```csharp
public sealed class PostgresFixture : IAsyncLifetime
{
    public async Task InitializeAsync() { /* container start */ }
    public async Task DisposeAsync() { /* container dispose */ }
}
```

After:
```csharp
public sealed class PostgresFixture : IAsyncDisposable
{
    public async Task InitializeAsync() { /* container start — body unchanged */ }
    public async ValueTask DisposeAsync() { /* container dispose — body unchanged, return ValueTask */ }
}
```

If `using Xunit;` was used to source `IAsyncLifetime`, remove it.

- [ ] **Step 2: Build + test.**

```
dotnet build src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj -warnaserror
dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --no-build
```
Expected: tests still pass — xUnit's `IClassFixture<PostgresFixture>` no longer works (since `IAsyncLifetime` is gone), but `MigrationIntegrationTests` is still on xUnit at this point. **Stop here if test count drops.** This indicates `MigrationIntegrationTests` needs to migrate in the same step.

  **If migration tests fail with "fixture not initialized":** xUnit's `IClassFixture<T>` requires either an `IAsyncLifetime` or just a parameterless constructor. The fix is to migrate `MigrationIntegrationTests.cs` to MSTest in the next step (Task 9.4) atomically with this commit.

- [ ] **Step 3: Commit.**

If tests pass cleanly:
```
git add src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Fixtures/PostgresFixture.cs
git commit -m "test(catalog): PostgresFixture is now IAsyncDisposable (drops xUnit IAsyncLifetime)"
```

If migration test failure forced atomic migration of `MigrationIntegrationTests.cs`, fold both edits into one commit with message:
```
test(catalog): migrate PostgresFixture and MigrationIntegrationTests to MSTest v4
```

### Task 9.4: Translate test files using `KartovaApiCollection` / `[Collection]`

The xUnit pattern in this project: each test class carries `[Collection(KartovaApiCollection.Name)]` and ctor-injects `KartovaApiFixture`. Migrate to a shared MSTest base class using `[ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]`.

**Files (all migrate together — they share the fixture):**
- `RegisterApplicationTests.cs`
- `EditApplicationTests.cs`
- `DeprecateApplicationTests.cs`
- `DecommissionApplicationTests.cs`
- `CrossTenantWriteTests.cs`
- `ListApplicationsPaginationTests.cs`
- `Migrations/MigrationIntegrationTests.cs` (uses `IClassFixture<PostgresFixture>` instead — migrate slightly differently)

- [ ] **Step 1: Create a shared base class `CatalogIntegrationTestBase.cs`.**

Path: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CatalogIntegrationTestBase.cs`.

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public abstract class CatalogIntegrationTestBase
{
    protected static KartovaApiFixture Fx { get; private set; } = null!;

    [ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]
    public static async Task ClassInit(TestContext _)
    {
        Fx = new KartovaApiFixture();
        await Fx.InitializeAsync();
    }

    [ClassCleanup(InheritanceBehavior.BeforeEachDerivedClass)]
    public static async Task ClassDone()
    {
        if (Fx is not null) await ((IAsyncDisposable)Fx).DisposeAsync();
    }
}
```

This replaces the `KartovaApiCollection` / `[Collection]` mechanism. Each test class derives from `CatalogIntegrationTestBase` and accesses `Fx` via the inherited static.

- [ ] **Step 2: Translate each consumer test file.**

For each of the six listed `[Collection]`-bearing files: apply spec §4 rules + change the class declaration from `public class XTests` to `public class XTests : CatalogIntegrationTestBase` (drop `[Collection(KartovaApiCollection.Name)]`, drop the ctor-injected `_fx`, replace internal `_fx.X` with `Fx.X`).

For each file: edit, build, test, commit.

  Build/test commands:
  ```
  dotnet build src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj -warnaserror
  dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --no-build
  ```

  - [ ] **Step 2a:** `RegisterApplicationTests.cs` — translate, build, test, commit.
  - [ ] **Step 2b:** `EditApplicationTests.cs` — translate, build, test, commit.
  - [ ] **Step 2c:** `DeprecateApplicationTests.cs` — translate, build, test, commit.
  - [ ] **Step 2d:** `DecommissionApplicationTests.cs` — translate, build, test, commit.
  - [ ] **Step 2e:** `CrossTenantWriteTests.cs` — translate, build, test, commit.
  - [ ] **Step 2f:** `ListApplicationsPaginationTests.cs` — translate, build, test, commit.

- [ ] **Step 3: Translate `MigrationIntegrationTests.cs`** (uses `IClassFixture<PostgresFixture>`, not the API fixture).

In MSTest, this consumer pattern uses `[ClassInitialize]` directly on the class:

```csharp
[TestClass]
public class MigrationIntegrationTests
{
    private static PostgresFixture Pg { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        Pg = new PostgresFixture();
        await Pg.InitializeAsync();
    }

    [ClassCleanup]
    public static async Task ClassDone()
    {
        if (Pg is not null) await Pg.DisposeAsync();
    }

    [TestMethod]
    public async Task SomeTest() { /* uses Pg */ }
}
```

Apply spec §4 rules to assertions. Edit, build, test, commit.

### Task 9.5: Delete `KartovaApiCollection.cs`

**Files:**
- Delete: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/KartovaApiCollection.cs`

- [ ] **Step 1: Verify no test class still references `KartovaApiCollection.Name` or `[Collection(...)]`.**

```
git grep -nE "KartovaApiCollection|\[Collection\(" -- src/Modules/Catalog/Kartova.Catalog.IntegrationTests
```
Expected: no matches.

- [ ] **Step 2: Delete the file.**

```
git rm src/Modules/Catalog/Kartova.Catalog.IntegrationTests/KartovaApiCollection.cs
```

- [ ] **Step 3: Build + test.**

```
dotnet build Kartova.slnx -warnaserror
dotnet test Kartova.slnx --no-build
```

- [ ] **Step 4: Commit.**

```
git commit -m "test(catalog): remove KartovaApiCollection (replaced by CatalogIntegrationTestBase)"
```

### Task 9.6: Drop xUnit references from the project

Same shape as Task 1.5.

- [ ] **Step 1: Verify no xUnit attributes remain.**

```
git grep -nE "\[Fact\]|\[Theory\]|using Xunit|FluentAssertions" -- src/Modules/Catalog/Kartova.Catalog.IntegrationTests
```

- [ ] **Step 2: Remove `xunit`, `xunit.runner.visualstudio`, `FluentAssertions` from the csproj.**

- [ ] **Step 3: Build + test, full solution.**

```
dotnet build Kartova.slnx -warnaserror
dotnet test Kartova.slnx --no-build
```

- [ ] **Step 4: Real HTTP verification — Phase 9 has integration tests against real infra.**

Run the project's tests with Testcontainers up:
```
dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --no-build
```
Capture: total tests, all green. Spot-check at least one happy-path and one negative-path test from the diff.

- [ ] **Step 5: Commit.**

```
git add src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj
git commit -m "chore(test): remove xUnit + FluentAssertions from Kartova.Catalog.IntegrationTests"
```

- [ ] **Step 6: Mutation regression — `Kartova.SharedKernel.Postgres` (interim score; Phase 10 is the official gate).**

Run per-project Stryker against `Kartova.SharedKernel.Postgres`:
```
dotnet stryker -f src/Kartova.SharedKernel.Postgres/stryker-config.json
```
Phase 9 is **co-driver** of this mutation target with Phase 10 (the Postgres Stryker config feeds both `Kartova.Catalog.IntegrationTests` and `Kartova.Organization.IntegrationTests`). Phase 10 is the second of the two co-drivers and is the official gate; this Phase-9 run captures an **interim diagnostic** score — a >1pt drift vs the 94.74% baseline (per `docs/superpowers/specs/baselines/2026-05-08-mstest-migration-mutation-baseline.md`) flags a translation defect in this phase to investigate before Phase 10.

(Per-project config used per the §Stryker invocation note at the top of this plan.)

Phase 9 complete.

---

## Phase 10 — `src/Modules/Organization/Kartova.Organization.IntegrationTests`

**Project:** `src/Modules/Organization/Kartova.Organization.IntegrationTests/Kartova.Organization.IntegrationTests.csproj`
**Files in scope (~9 + fixtures):**
- Test classes: `OrganizationEndpointHappyPathTests.cs`, `OrganizationEndpointNegativePathTests.cs`, `OrganizationAdminOnlyEndpointTests.cs`, `AdminBypassTests.cs`, `AuthErrorTests.cs`, `EfEnlistmentProbeTests.cs`, `StreamingDurabilityTests.cs`, `TenantIsolationTests.cs`, `TenantScopeMechanismTests.cs`.
- Fixtures: `KartovaApiFixture.cs`, `KartovaApiCollection.cs` (delete), `KartovaApiFaultInjectionFixture.cs`, `KartovaApiFaultInjectionCollection.cs` (delete).

**Same pattern as Phase 9, but with TWO fixture variants** (standard + fault-injection).

### Task 10.1: Add MSTest packages and `[assembly: DoNotParallelize]`

Same shape as Task 9.1. Two new files: csproj edit + `Properties/AssemblyInfo.cs`.

### Task 10.2: Trim `KartovaApiFixture.cs` and `KartovaApiFaultInjectionFixture.cs`

Same shape as Task 9.2. Drop `using Xunit;` if present. No interface changes.

### Task 10.3: Create two shared base classes

**Files:**
- Create: `src/Modules/Organization/Kartova.Organization.IntegrationTests/OrganizationIntegrationTestBase.cs`
- Create: `src/Modules/Organization/Kartova.Organization.IntegrationTests/OrganizationFaultInjectionTestBase.cs`

- [ ] **Step 1: Create `OrganizationIntegrationTestBase.cs`** — pattern identical to `CatalogIntegrationTestBase` (Task 9.4 Step 1) but referencing `KartovaApiFixture` from the Organization namespace.

- [ ] **Step 2: Create `OrganizationFaultInjectionTestBase.cs`** — same pattern but referencing `KartovaApiFaultInjectionFixture`.

- [ ] **Step 3: Build.**

```
dotnet build src/Modules/Organization/Kartova.Organization.IntegrationTests/Kartova.Organization.IntegrationTests.csproj -warnaserror
```

- [ ] **Step 4: Commit.**

```
git add src/Modules/Organization/Kartova.Organization.IntegrationTests/OrganizationIntegrationTestBase.cs src/Modules/Organization/Kartova.Organization.IntegrationTests/OrganizationFaultInjectionTestBase.cs
git commit -m "test(organization): add MSTest base classes for integration fixtures"
```

### Task 10.4: Translate consumer test files

For each of the nine listed test files:
- Determine which fixture it consumes (check the `[Collection(...)]` value: `KartovaApiCollection.Name` → standard; `KartovaApiFaultInjectionCollection.Name` → fault injection).
- Apply spec §4 translation rules.
- Make the class derive from `OrganizationIntegrationTestBase` or `OrganizationFaultInjectionTestBase` (drop `[Collection(...)]`, drop ctor-injected fixture, replace `_fx.X` with `Fx.X`).

For each file: edit, build, test, commit.

- [ ] **Step 1:** `OrganizationEndpointHappyPathTests.cs`
- [ ] **Step 2:** `OrganizationEndpointNegativePathTests.cs`
- [ ] **Step 3:** `OrganizationAdminOnlyEndpointTests.cs`
- [ ] **Step 4:** `AdminBypassTests.cs`
- [ ] **Step 5:** `AuthErrorTests.cs`
- [ ] **Step 6:** `EfEnlistmentProbeTests.cs`
- [ ] **Step 7:** `StreamingDurabilityTests.cs`
- [ ] **Step 8:** `TenantIsolationTests.cs`
- [ ] **Step 9:** `TenantScopeMechanismTests.cs`

### Task 10.5: Delete `KartovaApiCollection.cs` + `KartovaApiFaultInjectionCollection.cs`

Same shape as Task 9.5 but two file deletions.

### Task 10.6: Drop xUnit references and verify

Same shape as Task 9.6. Real HTTP verification step required.

### Task 10.7: Mutation regression — `Kartova.SharedKernel.Postgres` (official gate)

- [ ] **Step 1: Run per-project Stryker against `Kartova.SharedKernel.Postgres`.**

```
dotnet stryker -f src/Kartova.SharedKernel.Postgres/stryker-config.json
```
Phase 10 is the **second of the two co-drivers** for this mutation target (Phase 9 captured an interim diagnostic score). At this point both `Kartova.Catalog.IntegrationTests` and `Kartova.Organization.IntegrationTests` are on MSTest, so this run is the **official gate** for `Kartova.SharedKernel.Postgres`. Expected: mutation score within ±1pt of the 94.74% baseline (per `docs/superpowers/specs/baselines/2026-05-08-mstest-migration-mutation-baseline.md`). If outside ±1pt, see the merge-gate language in that doc and the per-phase ownership table.

(Per-project config used per the §Stryker invocation note at the top of this plan.)

Phase 10 complete.

---

## Phase 11 — `tests/Kartova.Api.IntegrationTests`

**Project:** `tests/Kartova.Api.IntegrationTests/Kartova.Api.IntegrationTests.csproj`
**Files in scope (4 tests + 2 fixtures):**
- Test classes: `AuthSmokeTests.cs`, `CorsTests.cs`, `OpenApiTests.cs`, plus any other `*Tests.cs` files.
- Fixtures: `KeycloakContainerFixture.cs` (rewrite), `KeycloakTestCollection.cs` (delete).

**Special:** Migrate the assembly-shared Keycloak/Postgres container to a `[AssemblyInitialize]` singleton per spec §5.1.

### Task 11.1: Add MSTest packages and `[assembly: DoNotParallelize]`

**Files:**
- Modify: `tests/Kartova.Api.IntegrationTests/Kartova.Api.IntegrationTests.csproj`
- Create: `tests/Kartova.Api.IntegrationTests/Properties/AssemblyInfo.cs`

Same shape as Task 9.1.

### Task 11.2: Convert `KeycloakContainerFixture` to a plain class

**Files:**
- Modify: `tests/Kartova.Api.IntegrationTests/KeycloakContainerFixture.cs`

- [ ] **Step 1: Drop `IAsyncLifetime`, implement `IAsyncDisposable`.**

Read the current file. Make these edits:

Before:
```csharp
public sealed class KeycloakContainerFixture : IAsyncLifetime
{
    public PostgreSqlContainer Postgres { get; } = ...
    public KeycloakContainer Keycloak { get; } = ...

    public async Task InitializeAsync()
    {
        await Task.WhenAll(Postgres.StartAsync(), Keycloak.StartAsync());
    }

    public async Task DisposeAsync()
    {
        await Task.WhenAll(Postgres.DisposeAsync().AsTask(), Keycloak.DisposeAsync().AsTask());
    }

    public string KeycloakAuthority => $"{Keycloak.GetBaseAddress()}realms/kartova";
}
```

After:
```csharp
public sealed class KeycloakContainerFixture : IAsyncDisposable
{
    public PostgreSqlContainer Postgres { get; } = ...  // unchanged
    public KeycloakContainer Keycloak { get; } = ...    // unchanged

    public async Task InitializeAsync()
    {
        await Task.WhenAll(Postgres.StartAsync(), Keycloak.StartAsync());
    }

    public async ValueTask DisposeAsync()
    {
        await Task.WhenAll(Postgres.DisposeAsync().AsTask(), Keycloak.DisposeAsync().AsTask());
    }

    public string KeycloakAuthority => $"{Keycloak.GetBaseAddress()}realms/kartova";
}
```

Drop `using Xunit;`.

- [ ] **Step 2: Build.**

```
dotnet build tests/Kartova.Api.IntegrationTests/Kartova.Api.IntegrationTests.csproj -warnaserror
```
Expected: this *will fail* because xUnit `[Collection(KeycloakTestCollection.Name)]`-bearing test classes can't satisfy `ICollectionFixture<KeycloakContainerFixture>` anymore (the type no longer implements `IAsyncLifetime`). **Don't commit yet.** The fix is the next task: migrate the test classes and delete the collection fixture in the same coherent change.

### Task 11.3: Create `IntegrationTestAssemblySetup.cs`

**Files:**
- Create: `tests/Kartova.Api.IntegrationTests/IntegrationTestAssemblySetup.cs`

- [ ] **Step 1: Create the file.**

Path: `tests/Kartova.Api.IntegrationTests/IntegrationTestAssemblySetup.cs`. Per spec §5.1:

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.Api.IntegrationTests;

[TestClass]
public sealed class IntegrationTestAssemblySetup
{
    public static KeycloakContainerFixture Containers { get; private set; } = null!;

    [AssemblyInitialize]
    public static async Task InitAsync(TestContext _)
    {
        Containers = new KeycloakContainerFixture();
        await Containers.InitializeAsync();
    }

    [AssemblyCleanup]
    public static async Task CleanupAsync()
    {
        if (Containers is not null) await Containers.DisposeAsync();
    }
}
```

### Task 11.4: Translate `AuthSmokeTests.cs`

**Files:**
- Modify: `tests/Kartova.Api.IntegrationTests/AuthSmokeTests.cs`

This is the canonical example from spec §5.2. Apply spec §4 + §5.2.

- [ ] **Step 1: Replace the class.**

Per spec §5.2:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Kartova.Organization.Infrastructure;
using Kartova.SharedKernel;
using Kartova.SharedKernel.AspNetCore;
using Kartova.Testing.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace Kartova.Api.IntegrationTests;

[TestClass]
public sealed class AuthSmokeTests
{
    private static KeycloakContainerFixture Fx => IntegrationTestAssemblySetup.Containers;
    private WebApplicationFactory<Program>? _app;

    [TestInitialize]
    public async Task TestInit()
    {
        await PostgresTestBootstrap.SeedRolesAndSchemaAsync(Fx.Postgres.GetConnectionString());

        // Env vars must be set BEFORE the WebApplicationFactory boots the host.
        // Program.Main reads ConnectionStrings:* and Authentication:* before the
        // WithWebHostBuilder callback runs, so env vars are the only vehicle that reaches that code.
        Environment.SetEnvironmentVariable($"ConnectionStrings__{KartovaConnectionStrings.Main}",
            PostgresTestBootstrap.ConnectionStringFor(Fx.Postgres.GetConnectionString(), PostgresTestBootstrap.AppRole));
        Environment.SetEnvironmentVariable($"ConnectionStrings__{KartovaConnectionStrings.Bypass}",
            PostgresTestBootstrap.ConnectionStringFor(Fx.Postgres.GetConnectionString(), PostgresTestBootstrap.BypassRole));
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.Authority), Fx.KeycloakAuthority);
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.MetadataAddress),
            $"{Fx.KeycloakAuthority}/.well-known/openid-configuration");
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.Audience), "kartova-api");
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.RequireHttpsMetadata), "false");

        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
        });

        await PostgresTestBootstrap.RunMigrationsAsync<OrganizationDbContext>(
            PostgresTestBootstrap.ConnectionStringFor(Fx.Postgres.GetConnectionString(), PostgresTestBootstrap.MigratorRole),
            opts => new OrganizationDbContext(opts));
        await SeedOrgA();
    }

    [TestCleanup]
    public void TestCleanup()
    {
        _app?.Dispose();
    }

    [TestMethod]
    public async Task Full_KeyCloak_realm_issues_token_and_API_accepts_it()
    {
        using var oidc = new HttpClient();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "kartova-api",
            ["username"] = "admin@orga.kartova.local",
            ["password"] = "dev_pass",
            ["scope"] = "openid",
        });
        var tokenResp = await oidc.PostAsync($"{Fx.KeycloakAuthority}/protocol/openid-connect/token", form);
        tokenResp.EnsureSuccessStatusCode();
        var payload = await tokenResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var accessToken = payload!["access_token"].ToString()!;

        var client = _app!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var resp = await client.GetAsync("/api/v1/organizations/me");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
    }

    private async Task SeedOrgA()
    {
        var bypassConnectionString = PostgresTestBootstrap.ConnectionStringFor(
            Fx.Postgres.GetConnectionString(), PostgresTestBootstrap.BypassRole);
        await using var conn = new NpgsqlConnection(bypassConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO organizations (id, tenant_id, name, created_at) VALUES ($1, $2, 'Org A', now())";
        cmd.Parameters.AddWithValue(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        cmd.Parameters.AddWithValue(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        await cmd.ExecuteNonQueryAsync();
    }

    private static string EnvKey(string configKey) => configKey.Replace(":", "__");
}
```

Note the diff vs original:
- `IAsyncLifetime` → `[TestInitialize]`/`[TestCleanup]`.
- Constructor injection of `KeycloakContainerFixture` → static `Fx` accessor.
- `[Collection(KeycloakTestCollection.Name)]` and `[Fact]` → `[TestClass]` and `[TestMethod]`.
- `resp.StatusCode.Should().Be(HttpStatusCode.OK)` → `Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode)`.
- `using FluentAssertions; using Xunit;` removed; `using Microsoft.VisualStudio.TestTools.UnitTesting;` added.

### Task 11.5: Translate remaining test files in this project

For each remaining `*.cs` test file in `tests/Kartova.Api.IntegrationTests`: apply the same pattern as Task 11.4. List them via `git ls-files tests/Kartova.Api.IntegrationTests/*.cs` and translate each (skip `KeycloakContainerFixture.cs`, `IntegrationTestAssemblySetup.cs`, `KeycloakTestCollection.cs`).

For each file: edit, build, test, commit.

- [ ] **Step 1:** `CorsTests.cs` — translate, build, test, commit.
- [ ] **Step 2:** `OpenApiTests.cs` — translate, build, test, commit.

If additional test files exist beyond these (verify with `git ls-files`), translate them too.

### Task 11.6: Delete `KeycloakTestCollection.cs`

**Files:**
- Delete: `tests/Kartova.Api.IntegrationTests/KeycloakTestCollection.cs`

- [ ] **Step 1: Verify no remaining references.**

```
git grep -nE "KeycloakTestCollection|\[Collection\(" -- tests/Kartova.Api.IntegrationTests
```
Expected: no matches.

- [ ] **Step 2: Delete and commit.**

```
git rm tests/Kartova.Api.IntegrationTests/KeycloakTestCollection.cs
git commit -m "test(api): remove KeycloakTestCollection (replaced by IntegrationTestAssemblySetup)"
```

### Task 11.7: Drop xUnit references and verify

Same shape as Task 9.6. Real HTTP happy + negative path mandatory:

- [ ] **Step 1: Verify no xUnit attributes remain.**

```
git grep -nE "\[Fact\]|\[Theory\]|using Xunit|FluentAssertions" -- tests/Kartova.Api.IntegrationTests
```

- [ ] **Step 2: Remove xUnit + FA + xunit.extensibility.core from the csproj.**

- [ ] **Step 3: Build + test + real HTTP verification.**

```
dotnet build Kartova.slnx -warnaserror
dotnet test tests/Kartova.Api.IntegrationTests/Kartova.Api.IntegrationTests.csproj --no-build
```

Capture: at minimum the `Full_KeyCloak_realm_issues_token_and_API_accepts_it` test must pass (the canonical happy-path) and one negative-path test from `CorsTests` or `OpenApiTests`.

- [ ] **Step 4: Commit.**

```
git add tests/Kartova.Api.IntegrationTests/Kartova.Api.IntegrationTests.csproj tests/Kartova.Api.IntegrationTests/KeycloakContainerFixture.cs tests/Kartova.Api.IntegrationTests/IntegrationTestAssemblySetup.cs
git commit -m "chore(test): remove xUnit + FA from Kartova.Api.IntegrationTests"
```

- [ ] **Step 5: Mutation regression — `Kartova.SharedKernel.AspNetCore` (official gate).**

Run per-project Stryker against `Kartova.SharedKernel.AspNetCore`:
```
dotnet stryker -f src/Kartova.SharedKernel.AspNetCore/stryker-config.json
```
Phase 11 is the **second of the two co-drivers** for this mutation target (Phase 2 captured an interim diagnostic score). At this point both `tests/Kartova.SharedKernel.AspNetCore.Tests` and `tests/Kartova.Api.IntegrationTests` are on MSTest, so this run is the **official gate** for `Kartova.SharedKernel.AspNetCore`. Expected: mutation score within ±1pt of the 100.00% baseline (per `docs/superpowers/specs/baselines/2026-05-08-mstest-migration-mutation-baseline.md`). If outside ±1pt, see the merge-gate language in that doc and the per-phase ownership table.

(Per-project config used per the §Stryker invocation note at the top of this plan.)

Phase 11 complete.

---

## Phase 12 — Final cleanup

**Goal:** drop `IAsyncLifetime` from `KartovaApiFixtureBase`, drop xUnit/FA package versions from CPM, drop the lingering xUnit reference from `Kartova.Testing.Auth.csproj`, and run the final mutation regression check. Project SDK (`Microsoft.NET.Sdk`), runner (VSTest), and `coverlet.collector` all stay unchanged — MTP is out of scope (see plan header).

### Task 12.1: Remove `IAsyncLifetime` from `KartovaApiFixtureBase`

**Files:**
- Modify: `tests/Kartova.Testing.Auth/KartovaApiFixtureBase.cs`

- [ ] **Step 1: Drop the interface from the class declaration.**

Before:
```csharp
public abstract class KartovaApiFixtureBase
    : WebApplicationFactory<Program>, IAsyncLifetime, IAsyncDisposable
```

After:
```csharp
public abstract class KartovaApiFixtureBase
    : WebApplicationFactory<Program>, IAsyncDisposable
```

- [ ] **Step 2: Drop the `IAsyncLifetime.DisposeAsync()` explicit implementation line.**

Before:
```csharp
Task IAsyncLifetime.DisposeAsync() => DisposeAsyncCore();

async ValueTask IAsyncDisposable.DisposeAsync() { ... }
```

After:
```csharp
async ValueTask IAsyncDisposable.DisposeAsync() { ... }
```

- [ ] **Step 3: Drop `using Xunit;` from the file.**

- [ ] **Step 4: Build + test.**

```
dotnet build Kartova.slnx -warnaserror
dotnet test Kartova.slnx --no-build
```
Expected: success — by Phase 12, all consumers (Phases 9, 10) are on MSTest and use `IAsyncDisposable`. No remaining consumer of `IAsyncLifetime`.

- [ ] **Step 5: Commit.**

```
git add tests/Kartova.Testing.Auth/KartovaApiFixtureBase.cs
git commit -m "test(auth): drop IAsyncLifetime from KartovaApiFixtureBase (no consumers left)"
```

### Task 12.2: Remove `xunit.extensibility.core` from `Kartova.Testing.Auth.csproj`

**Files:**
- Modify: `tests/Kartova.Testing.Auth/Kartova.Testing.Auth.csproj`

- [ ] **Step 1: Delete the line.**

Remove:
```xml
    <PackageReference Include="xunit.extensibility.core" />
```

- [ ] **Step 2: Build + test.**

```
dotnet build Kartova.slnx -warnaserror
dotnet test Kartova.slnx --no-build
```

- [ ] **Step 3: Commit.**

```
git add tests/Kartova.Testing.Auth/Kartova.Testing.Auth.csproj
git commit -m "chore(test): remove xunit.extensibility.core from Kartova.Testing.Auth"
```

### Task 12.3: (Removed) — `MSTest.Sdk` flip deferred

Originally this task switched every migrated test project from `Microsoft.NET.Sdk` to `MSTest.Sdk` to enable Microsoft.Testing.Platform. **Removed from this migration's scope.** The Phase 0 Stryker × MTP probe (recorded in `docs/superpowers/specs/baselines/2026-05-08-mstest-migration-mutation-baseline.md` §"Stryker × MTP compatibility probe", which records the exact Stryker version probed) confirmed Stryker.NET cannot drive MTP-only test projects at that version (stryker-net#3094). All test projects stay on `Microsoft.NET.Sdk` + VSTest. Revisit in a future migration once Stryker support lands.

**Numbering:** Task 12.4 follows directly. The 12.3 gap is intentional — re-numbering downstream tasks would reflow many cross-references for no value.

### Task 12.4: Remove xUnit and FluentAssertions from CPM

**Files:**
- Modify: `Directory.Packages.props`

- [ ] **Step 1: Verify no project references xUnit packages anymore.**

```
git grep -nE "xunit|FluentAssertions" -- "**/*.csproj"
```
Expected: no matches.

- [ ] **Step 2: Delete the package version entries.**

In `Directory.Packages.props`, remove:
```xml
    <PackageVersion Include="xunit" Version="..." />
    <PackageVersion Include="xunit.extensibility.core" Version="..." />
    <PackageVersion Include="xunit.runner.visualstudio" Version="..." />
    <PackageVersion Include="FluentAssertions" Version="..." />
```

- [ ] **Step 3: Trim the migration-era comment in `Directory.Packages.props`.**

The MSTest entries currently sit under `<!-- MSTest v4 — added during xUnit→MSTest migration; xUnit lines are removed in Phase 12 -->`. After this task removes the xUnit lines, the trailing clause becomes stale.

Replace with `<!-- MSTest v4 -->` (or delete the comment entirely — git blame answers the "when added" question).

- [ ] **Step 4: Build + test.**

```
dotnet restore Kartova.slnx
dotnet build Kartova.slnx -warnaserror
dotnet test Kartova.slnx --no-build
```

- [ ] **Step 5: Commit.**

```
git add Directory.Packages.props
git commit -m "chore(test): drop xUnit + FluentAssertions package versions from CPM"
```

### Task 12.5: (Removed) — coverage tool replacement deferred

Originally this task replaced `coverlet.collector` with `Microsoft.Testing.Extensions.CodeCoverage`. **Removed from scope** along with Task 12.3 — that replacement is part of the MTP runner switch, which is deferred (see Task 12.3 note). `coverlet.collector` stays as the coverage collector.

**Numbering:** Task 12.6 follows directly. The 12.5 gap is intentional — same rationale as 12.3.

### Task 12.6: Final mutation regression check

**Files:** Read-only.

- [ ] **Step 1: Run Stryker against the whole repo using the root config.**

```
dotnet stryker -f stryker-config.json
```
Expected: each Stryker target (`Kartova.Catalog.Tests`, `Kartova.Organization.Tests`) reports a mutation score within ±1pt of the Phase 0 baseline.

- [ ] **Step 2: Append final result to baseline doc.**

Edit `docs/superpowers/specs/baselines/2026-05-08-mstest-migration-mutation-baseline.md`. Add:

```markdown
## Phase 12 final verification

| Project | Baseline | Phase 12 | Δ |
|---|---|---|---|
| Kartova.Catalog.Tests | TBD% | TBD% | +/- TBDpt |
| Kartova.Organization.Tests | TBD% | TBD% | +/- TBDpt |

**Migration accepted:** mutation scores remained within tolerance.
```

If any Δ > 1pt: **stop**. Investigate before merging Phase 12. Likely cause: an `Assert.AreEqual(actual, expected)` argument-order swap weakening an assertion.

- [ ] **Step 3: Commit.**

```
git add docs/superpowers/specs/baselines/2026-05-08-mstest-migration-mutation-baseline.md
git commit -m "docs(test): final mutation regression check passes after MSTest migration"
```

### Task 12.7: Migration complete

- [ ] **Step 1: Final solution build.**

```
dotnet build Kartova.slnx -warnaserror
```

- [ ] **Step 2: Full test suite (VSTest runner, unchanged).**

```
dotnet test Kartova.slnx --no-build
```

- [ ] **Step 3: Verify zero xUnit / FluentAssertions usage.**

```
git grep -nE "xunit|FluentAssertions|using Xunit|\[Fact\]|\[Theory\]|IClassFixture|ICollectionFixture|IAsyncLifetime|ITestOutputHelper" -- "*.cs" "*.csproj"
```
Expected: 0 matches across all source and project files. (Spec doc references in `docs/` are allowed and don't match the patterns above.)

- [ ] **Step 4: No commit needed.** Phase 12 is complete and ready for PR review.

---

## Final verification (post-Phase 12)

After all 13 phases have merged:

- [ ] **Step 1: Run the project's standard DoD verification once across the migrated state.**

Per `CLAUDE.md` "Definition of Done" — run all 9 checks against the Phase 12 PR diff (full migration). The same checks were already run incrementally per phase; this is the slice-boundary safety net.

- [ ] **Step 2: Tick the spec checkbox.**

Edit `docs/superpowers/specs/2026-05-08-xunit-to-mstest-migration-design.md` — mark `**Status:** Draft (awaiting plan)` as `**Status:** Implemented (2026-05-08 → completion-date)`.

- [ ] **Step 3: Update `docs/product/CHECKLIST.md` if the migration was scoped as a slice item there.** (Otherwise skip.)

- [ ] **Step 4: Final commit.**

```
git add docs/superpowers/specs/2026-05-08-xunit-to-mstest-migration-design.md
git commit -m "docs(spec): mark MSTest migration spec as Implemented"
```

Migration complete.
