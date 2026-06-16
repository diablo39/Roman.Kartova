# Deep PR Review — `feat/slice-5-applications-edit-lifecycle`

**Date:** 2026-05-07
**Branch:** `feat/slice-5-applications-edit-lifecycle` vs `master`
**Status:** OPEN (pre-merge gate)
**Spec:** [`docs/superpowers/specs/2026-05-06-slice-5-applications-edit-lifecycle-design.md`](../specs/2026-05-06-slice-5-applications-edit-lifecycle-design.md)
**Plan:** [`docs/superpowers/plans/2026-05-06-slice-5-applications-edit-lifecycle-plan.md`](../plans/2026-05-06-slice-5-applications-edit-lifecycle-plan.md)
**ADR index:** [`docs/architecture/decisions/README.md`](../../architecture/decisions/README.md)
**Test taxonomy:** ADR-0083 · **DoD:** CLAUDE.md §Definition of Done · **Mutation report:** [`mutation-report-surviving.md`](../../../mutation-report-surviving.md) (slice-4 vintage, see Blocking #1)

---

## Overview

Slice 5 ships the project's first edit endpoint and first lifecycle-transition endpoints on the Catalog `Application` aggregate: `PUT /api/v1/catalog/applications/{id}` (full-replacement edit with `xmin` row-version + `If-Match`/`ETag` optimistic concurrency), `POST /{id}/deprecate`, and `POST /{id}/decommission`. New SharedKernel infrastructure (`IfMatchEndpointFilter`, `VersionEncoding`, `PreconditionRequired`/`ConcurrencyConflict`/`LifecycleConflict` exception handlers, `ILifecycleConflict` marker) lays a reusable optimistic-concurrency + lifecycle-transition contract for the ~20 future edit endpoints. ADR-0096 (REST verb policy: PUT for replacement, POST for actions, no PATCH) lands in the same PR with an arch-test pin. Frontend gains `EditApplicationDialog`, `LifecycleMenu`, `LifecycleBadge`, and two confirm dialogs; the list view gains a Lifecycle column.

---

## Blocking-class issues

### 1. Mutation-sentinel not run on slice-5 surfaces — DoD #8 not satisfied

**Evidence:** [`mutation-report-surviving.md:3`](../../../mutation-report-surviving.md) — "Generated: 2026-05-05T12:38:54Z" — predates the slice-5 spec (2026-05-06) and surveys only `QueryablePagingExtensions.cs:168` (slice-4 cursor-pagination work). No mutation evidence exists for the new surfaces named in spec §9.6: `Application.cs`, `Lifecycle.cs`, `InvalidLifecycleTransitionException.cs`, `EditApplicationCommand.cs`/`DeprecateApplicationCommand.cs`/`DecommissionApplicationCommand.cs` + handlers, `IfMatchEndpointFilter.cs`, `ConcurrencyConflictExceptionHandler.cs`, `LifecycleConflictExceptionHandler.cs`, `CatalogEndpointDelegates.cs`.

**Impact:** CLAUDE.md DoD #8 ("Mutation feedback loop run on changed files: `mutation-sentinel` → `test-generator` until mutants are killed; mutation score ≥80%") cannot be cited as green. Per the same DoD: "Until all nine are green, the honest status is *implementation staged, verification pending* — never *slice N complete*." Without running, surviving mutants in (e.g.) `Application.Deprecate`'s `<=` boundary or `Decommission`'s `<` boundary, or in `IfMatchEndpointFilter`'s base64 decode path, are unknown.

**Fix:** Run `mutation-sentinel` against the slice-5 changed files; commit the new `mutation-report-surviving.md` (or per-project equivalent) at the project root. If survivors appear, drive `test-generator` to kill them or accept with a documented near-equivalence note (mirroring the existing `QueryablePagingExtensions:168` acceptance block).

---

## Should-fix issues

### 2. 412 ConcurrencyConflict UX silently deviates from spec §8.3

**Evidence:** [`web/src/features/catalog/components/EditApplicationDialog.tsx:74-78`](../../../web/src/features/catalog/components/EditApplicationDialog.tsx) — on 412 the handler only emits `toast.error("Someone else edited this. Reload to see the latest values.")` and leaves the dialog open. No `qc.invalidateQueries(applicationKeys.detail(id))` is called.

Spec §8.3 says: *"412 (concurrency-conflict) | toast 'Someone else edited this. Reloaded latest values.'; **invalidate `['application', id]`**; dialog stays open with **refreshed pre-fill** (form reset() to new values)"*.

**Impact:** User must manually leave and reopen the dialog (or refresh the page) to see the latest values. The spec's promise — "Reloaded latest values" — is not honored. Once Service / API / Component edit slices copy this dialog as the reference exemplar, the deviation propagates.

**Fix:** In the 412 branch, call `qc.invalidateQueries({ queryKey: applicationKeys.detail(application.id) })`. After the simplify pass, the dialog already uses RHF's `values` (controlled defaults — see `EditApplicationDialog.tsx:50-58`), so when the parent `ApplicationDetailPage` re-renders with the refreshed `application` prop the form auto-resets. Total change: one extra line + the toast wording can return to spec verbatim. (`useQueryClient` is already imported transitively via `useEditApplication`; pull it directly in the dialog.)

### 3. Stale narration comment in `Program.cs` after `ILifecycleConflict` introduction

**Evidence:** [`src/Kartova.Api/Program.cs:123-128`](../../../src/Kartova.Api/Program.cs):
```csharp
// Lifecycle-conflict → 409 mapping — slice 5 (ADR-0073).
// Maps any module's InvalidLifecycleTransitionException (matched by
// type name to avoid SharedKernel → Catalog coupling) to RFC 7807 409
```
The simplify pass replaced reflection-by-type-name with the `ILifecycleConflict` marker interface. Compare [`src/Kartova.SharedKernel.AspNetCore/LifecycleConflictExceptionHandler.cs:31`](../../../src/Kartova.SharedKernel.AspNetCore/LifecycleConflictExceptionHandler.cs): `if (exception is not ILifecycleConflict conflict) return false;`.

**Impact:** Narration drift — the comment now lies about the implementation. Future contributors who refactor on the basis of "we use type-name matching to avoid coupling" will reach wrong conclusions about what the constraints are.

**Fix:** Update the comment to: *"Maps any exception implementing `ILifecycleConflict` (defined in `Kartova.SharedKernel`) to RFC 7807 409. The marker interface decouples SharedKernel.AspNetCore from module domains while keeping property reads compile-time-checked."*

### 4. Wire enum casing inconsistent across body vs. ProblemDetails extension

**Evidence:**
- [`src/Kartova.Api/Program.cs:75-79`](../../../src/Kartova.Api/Program.cs) — global `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)` per ADR-0095. `ApplicationResponse.Lifecycle` therefore wires as `"active"` / `"deprecated"` / `"decommissioned"`.
- [`src/Kartova.SharedKernel.AspNetCore/LifecycleConflictExceptionHandler.cs:43`](../../../src/Kartova.SharedKernel.AspNetCore/LifecycleConflictExceptionHandler.cs) — emits `problem.Extensions["currentLifecycle"] = conflict.CurrentLifecycleName` where `CurrentLifecycleName` is `enum.ToString()` → **PascalCase** ("Decommissioned").
- [`web/src/features/catalog/components/DeprecateConfirmDialog.tsx:110`](../../../web/src/features/catalog/components/DeprecateConfirmDialog.tsx) — frontend renders `Cannot deprecate — current state is ${problem.currentLifecycle}` → user sees "current state is Deprecated" with a capital D.
- Spec §3 Decision #13 says wire shape is `"Active"` / `"Deprecated"` / `"Decommissioned"` (PascalCase). ADR-0095 + Program.cs say camelCase. **The spec is internally inconsistent with ADR-0095.**

**Impact:** Two surfaces, two casings. The body and the 409 extension disagree; the SPA can't programmatically compare `application.lifecycle === problem.currentLifecycle`. `LifecycleBadge.tsx:16-26` is built around lowercase camelCase; the toast string ends up with PascalCase from the server. This is the kind of inconsistency that quietly costs an afternoon when the next entity copies the pattern.

**Fix:** Unify on camelCase (the ADR-0095 mandate, already realized in the body). Easiest: in `LifecycleConflictExceptionHandler.cs`, emit `JsonNamingPolicy.CamelCase.ConvertName(conflict.CurrentLifecycleName)` (or hard-code the three lowercase strings). Update the spec §3 Decision #13 to match. Adjust the 3 integration tests that assert `"Decommissioned"` / `"Deprecated"` / `"Active"` strings in extensions (`EditApplicationTests.cs:168-169`, `DeprecateApplicationTests.cs:82-83`, `DecommissionApplicationTests.cs:77-78, 102-103, 134-135`).

### 5. Sunset-date input has no `min` attribute — UI doesn't prevent submission as spec requires

**Evidence:** [`web/src/features/catalog/components/DeprecateConfirmDialog.tsx:131-148`](../../../web/src/features/catalog/components/DeprecateConfirmDialog.tsx) renders `<Input type="date">` with no `min` prop. The schema (`web/src/features/catalog/schemas/deprecateApplication.ts:7-13`) only rejects past dates *at submit time*. Spec §8.4 says: *"Min selectable: tomorrow (server validates > now; UI prevents submission)."* The inline comment at lines 65-67 acknowledges the wrapper limitation.

**Impact:** Users can pick today (or any date) in the picker, then click Submit, then see a field error — instead of being blocked at picker level. Minor UX deviation; doesn't break correctness (server is the authority).

**Fix:** Either (a) extend `web/src/components/base/input/input` to forward `min` for `type="date"` (broader, helps every future date input); or (b) update spec §8.4 to acknowledge the schema-level guard is the practical equivalent and accept the divergence; or (c) hand-roll a `<input type="date" min={...}>` here (escape hatch from the design system).

---

## Nits

### N1. Plan-narration comment in integration test ages poorly

[`src/Modules/Catalog/Kartova.Catalog.IntegrationTests/EditApplicationTests.cs:138-142`](../../../src/Modules/Catalog/Kartova.Catalog.IntegrationTests/EditApplicationTests.cs) — "*(Deprecate/Decommission endpoints land in Tasks 12 and 13 — this test is added to the PUT suite for proximity but uses those endpoints once they exist. If running this task alone, the test fails with 'endpoint not found' — that's expected; it'll go green at end of Task 13.)*" Tasks 12/13 are the plan's task numbering, not durable. Once merged, this comment is meaningless to a reader without the plan in hand. Trim to the *what*: "Sets up a Decommissioned application via the deprecate + decommission endpoints, then asserts the PUT 409."

### N2. `ProblemPayload` triplicated across the three integration test classes

[`EditApplicationTests.cs:201-228`](../../../src/Modules/Catalog/Kartova.Catalog.IntegrationTests/EditApplicationTests.cs), [`DeprecateApplicationTests.cs:128-155`](../../../src/Modules/Catalog/Kartova.Catalog.IntegrationTests/DeprecateApplicationTests.cs), [`DecommissionApplicationTests.cs:176-191`](../../../src/Modules/Catalog/Kartova.Catalog.IntegrationTests/DecommissionApplicationTests.cs) all carry their own nested `ProblemPayload`. Two of the three have a comment promising "extracted later if a fourth caller appears" — but with three callers in *one* slice, the threshold is comfortably crossed. Move to a single internal helper (e.g. `Kartova.Catalog.IntegrationTests/Support/ProblemPayload.cs`).

### N3. Lifecycle column header is non-sortable but visually homogeneous with sortable columns

[`web/src/features/catalog/components/ApplicationsTable.tsx:76`](../../../web/src/features/catalog/components/ApplicationsTable.tsx) uses `Table.Head id="lifecycle"` (correct — list endpoint doesn't accept lifecycle as a sort field per spec §3 Decision #13 and ADR-0095). Adjacent `name` and `createdAt` use `SortableHead`. To a user, the header row looks consistent yet only some columns respond to clicks. Either add a hover hint that lifecycle isn't sortable, or accept the divergence as cheap. Lifecycle filtering is explicitly deferred to E-05.F-01.S-02 (spec §13.6).

### N4. Decommission integration tests rely on `Task.Delay(2000)`

[`DecommissionApplicationTests.cs:31-37`](../../../src/Modules/Catalog/Kartova.Catalog.IntegrationTests/DecommissionApplicationTests.cs) (and the `EditApplicationTests.PUT_on_Decommissioned` flow) sets `sunsetDate = UtcNow.AddSeconds(1)` then `Task.Delay(2000)` to cross the boundary. Acknowledged as a known flake risk in the inline comment. On a slow CI agent (cold container, GC pause) the boundary can still be missed. Future improvement: register `FakeTimeProvider` in the test host and advance it deterministically — the production code already takes `TimeProvider`, so this is one DI swap away in `KartovaApiFixtureBase`.

### N5. `CHECKLIST.md` self-claims "PR #21" before merge

[`docs/product/CHECKLIST.md:96-97`](../../../docs/product/CHECKLIST.md) marks E-02.F-01.S-03 / S-04 as `[x]` with "(slice 5 — PR #21, 2026-05-06)". CLAUDE.md DoD: *"Until all nine are green, the honest status is **implementation staged, verification pending** — never **slice N complete**."* If PR #21 has not been merged with green CI and Copilot review (DoD #9), the tick is premature. Easy revert if `gh pr view 21` shows it open or unmerged.

---

## Missing tests

### MT1. Spec §9.6 — mutation-sentinel scope

See Blocking #1. Acceptance criterion: ≥80% mutation score on `Application.cs`, the three handlers, `IfMatchEndpointFilter.cs`, the three exception handlers, `VersionEncoding.cs`, and `CatalogEndpointDelegates.cs` (slice §9.6).

**Test that should exist (post-run):** new survivors driven to ≤target by `test-generator`. Per CLAUDE.md DoD #8, "document the score and any surviving mutants accepted as low-value."

### MT2. `ILifecycleConflict` interface path not directly tested

`tests/Kartova.SharedKernel.AspNetCore.Tests/LifecycleConflictExceptionHandlerTests.cs` covers the handler via the concrete `InvalidLifecycleTransitionException`. After the simplify pass introduced explicit-interface implementation (`InvalidLifecycleTransitionException.cs:18` — `string ILifecycleConflict.CurrentLifecycleName => CurrentLifecycle.ToString()`), there is no test that pins the explicit implementation. If a future refactor accidentally drops the `: ILifecycleConflict` declaration or the explicit member, all three existing tests still pass — they go through the concrete subclass — but production code (handler) breaks.

**Test to add:** `LifecycleConflictExceptionHandlerTests.Reads_currentLifecycleName_via_interface_path` — instantiate `InvalidLifecycleTransitionException(Decommissioned, ...)`, **upcast to `ILifecycleConflict`**, assert `((ILifecycleConflict)ex).CurrentLifecycleName == "Decommissioned"`. Pins the explicit-interface contract.

### MT3. 412 body `currentVersion` value not asserted, only key presence

[`EditApplicationTests.cs:75-78`](../../../src/Modules/Catalog/Kartova.Catalog.IntegrationTests/EditApplicationTests.cs):
```csharp
problem.Extensions.Should().ContainKey("currentVersion");
```
The key existing isn't enough — a server bug that returned the *stale* version (or a constant) would still make this test green. The whole point of the extension is to let the client resync without a separate GET. The handler-side capture in `EditApplicationHandler.TryCaptureCurrentVersionAsync` is doing real work; assert that it produces the correct value.

**Test to strengthen:** in `PUT_with_stale_If_Match_returns_412_with_currentVersion`, capture the body of the *first* (successful) PUT — its `version` field — and assert `problem.CurrentVersion == firstResponseBody.Version`.

### MT4. UI 412 invalidate-and-refresh path

If Should-fix #2 is applied, `EditApplicationDialog.test.tsx:109-125` should be extended: assert `qc.invalidateQueries` is called with the detail key on 412, and that a subsequent prop change re-seeds the form (RHF `values` reset). Currently the 412 test only asserts the toast text and that the dialog stays open.

---

## What looks good

1. **Domain invariants live on the aggregate, not in handlers.** [`src/Modules/Catalog/Kartova.Catalog.Domain/Application.cs:82-127`](../../../src/Modules/Catalog/Kartova.Catalog.Domain/Application.cs) — every lifecycle rule (terminal-Decommissioned, source-state guard, sunset-date strict-greater-than, before-sunset-date discriminator) is enforced inside `EditMetadata`/`Deprecate`/`Decommission`. The handler trio is a thin "load → invoke → save" wrapper. Idiomatic DDD; the `reason: "before-sunset-date"` discriminator threading from domain through `ILifecycleConflict.Reason` to the 409 extension is a clean boundary.

2. **`xmin` shadow mapping ships zero schema cost.** [`src/Modules/Catalog/Kartova.Catalog.Infrastructure/EfApplicationConfiguration.cs:66-71`](../../../src/Modules/Catalog/Kartova.Catalog.Infrastructure/EfApplicationConfiguration.cs) maps `Version` to Postgres `xmin` with `IsRowVersion + IsConcurrencyToken`; the migration [`20260506181927_AddApplicationLifecycle.cs:32-35`](../../../src/Modules/Catalog/Kartova.Catalog.Infrastructure/Migrations/20260506181927_AddApplicationLifecycle.cs) explicitly does *not* add a column. The whole optimistic-concurrency story for slice 5 (and the ~20 future edit endpoints per spec §1) costs zero schema change beyond what already exists.

3. **Architecture tests pin both ADR-0096 and ADR-0073.** [`tests/Kartova.ArchitectureTests/RestVerbPolicyRules.cs:27-41`](../../../tests/Kartova.ArchitectureTests/RestVerbPolicyRules.cs) walks the live `EndpointDataSource` to forbid PATCH (cheap, regression-only). [`tests/Kartova.ArchitectureTests/LifecycleEnumRules.cs:17-32`](../../../tests/Kartova.ArchitectureTests/LifecycleEnumRules.cs) pins three explicit numeric values + linear ordering — exactly the load-bearing properties spec §3 Decision #13 calls out. Both tests are RED if anyone violates the contract; both cost essentially nothing to maintain.

4. **`IfMatchEndpointFilter` is a reusable composition unit.** [`src/Kartova.SharedKernel.AspNetCore/IfMatchEndpointFilter.cs`](../../../src/Kartova.SharedKernel.AspNetCore/IfMatchEndpointFilter.cs) + [`CatalogModule.cs:49`](../../../src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs) (`.AddEndpointFilter<IfMatchEndpointFilter>()`). The filter doesn't know about Catalog; the route adds it declaratively. The next ~20 edit endpoints attach with one line each. `VersionEncoding` ([`VersionEncoding.cs`](../../../src/Kartova.SharedKernel.AspNetCore/VersionEncoding.cs)) keeps the base64-of-uint detail in one place, with `stackalloc` for both encode and decode.

5. **`ILifecycleConflict` interface in `Kartova.SharedKernel` cleanly decouples handler from module domains.** [`src/Kartova.SharedKernel/ILifecycleConflict.cs`](../../../src/Kartova.SharedKernel/ILifecycleConflict.cs) is a 4-property marker; [`InvalidLifecycleTransitionException.cs:9`](../../../src/Modules/Catalog/Kartova.Catalog.Domain/InvalidLifecycleTransitionException.cs) implements it. The handler ([`LifecycleConflictExceptionHandler.cs:31`](../../../src/Kartova.SharedKernel.AspNetCore/LifecycleConflictExceptionHandler.cs)) matches by typed contract — no reflection, no string-match-by-type-name, no SharedKernel→module reference. Future Service / Component / API entities each get a free 409 mapping by implementing the interface on their own exception.

---

## Notes on Definition of Done

| DoD bullet | Status | Evidence |
|---|---|---|
| 1. Solution build clean (warnings-as-errors) | ✅ Verified | post-simplify, all referenced projects build clean ([recent terminal output](#)) |
| 2. Per-task subagent reviews | ⚠ Verify in PR description | not visible from branch state |
| 3. `superpowers:requesting-code-review` at slice boundary | ⚠ Verify in PR description | not visible from branch state |
| 4. Full test suite green (unit + arch + integration) | ✅ Partial — unit + arch + Vitest verified; integration relies on Postgres testcontainer | re-confirm in CI |
| 5. `docker compose up` HTTP smoke per spec §9.8 | ⚠ Verify in PR description | not visible from branch state |
| 6. `/simplify` against branch diff | ✅ Just executed (this conversation) | -165 lines, simplify summary |
| 7. `/deep-review` against branch diff | ✅ This document | |
| 8. `mutation-sentinel` ≥80% on changed files | ❌ **Blocking #1** — slice-4-vintage report only | re-run required |
| 9. Copilot review requested + addressed | ⚠ Verify in PR description | not visible from branch state |

**Bottom line:** slice 5 is *implementation staged, verification pending* until Blocking #1 is resolved and DoD bullets 2/3/5/9 are cited in the PR description.
