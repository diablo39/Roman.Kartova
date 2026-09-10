# Engineering Tech-Debt / Follow-Ups

Cross-cutting engineering follow-ups that were deliberately deferred out of a slice's scope (they touch shared code beyond the slice, so fixing them inside the slice would over-broaden scope or risk regressions elsewhere). Each item is self-contained: a new session should be able to pick one up, read the problem + affected files + proposed fix, and implement it.

Convention: one `### TD-NNN` heading per item. Keep `Status: open` until done; on completion set `Status: done (<commit/PR>)` rather than deleting (preserves the rationale trail). Add new items at the bottom with the next number.

---

### TD-001 — Generic null-safe keyset for nullable sort columns

**Status:** done (branch `chore/tech-debt-td-001-002-003`) — `SortSpec<T>.IsNullable` opt-in; null-safe `ORDER BY` (NULLS LAST asc / FIRST desc via portable null-flag) + null-aware keyset predicate + cursor `n`-flag null boundary (ADR-0095 amended 2026-09-10); `InfrastructureSortSpecs.Provider` `?? ""` workaround removed. JSONB selectors stay `IsNullable = false` (ingest-invariant), slice-4 decision unchanged. The unsafe-default footgun (review finding) is guarded by an arch test — `PaginationConventionRules.SortSpecs_over_a_nullable_key_member_must_set_IsNullable` fails the build for a plain-member nullable sort with `IsNullable=false`.
**Origin:** E-02.F-04.S-01 slice 2a — deep-review (gate 8) / review-pr (gate 7) altitude finding. See `docs/superpowers/verification/2026-09-09-infrastructure-vm-slice2a/gate-findings.yaml` (gate `deep-review`, "6 JSONB sort selectors lack null/missing-key COALESCE") and the `Provider` COALESCE workaround.

**Problem.** Keyset (cursor) pagination builds the predicate `sortKey > @v OR (sortKey = @v AND id > @v)`. Under SQL three-valued logic, if a page-boundary row's sort key is `NULL`, both comparisons evaluate to `UNKNOWN` (→ false in `WHERE`), so the next-page query returns **zero rows** and pagination silently truncates at the first NULL. This is a latent trap for **any** nullable sortable column.

**Current state (workaround, not the real fix).** Only patched per-column, string-specific: `VmSortSpecs.Provider` and `InfrastructureSortSpecs.Provider` sort on `x.Provider ?? ""` (COALESCE to empty string). It is NOT reusable — a future nullable `DateTime?`/`int?` sort field cannot reuse `?? ""` and would silently reintroduce the bug. Additionally, the 6 VM JSONB sort selectors (powerState/os/vcpu/memoryGb/hostname/region) are NOT guarded at all — safe today only because `VmAttributes.Validate` guarantees every attribute key is present + non-null on the write path (see the invariant comment in `VmSortSpecs.cs` and spec §11 of the slice-2a design doc). Slice-4 auto-import writes rows bypassing that validation → the JSONB selectors become reachable.

**Affected files.**
- `src/Kartova.SharedKernel.Postgres/Pagination/QueryablePagingExtensions.cs` — `ApplyKeysetFilter` (the shared predicate builder; nullability-unaware).
- `src/Kartova.SharedKernel.Postgres/Pagination/SortSpec*.cs` — the sort-key abstraction.
- Consumers with the current per-column workaround: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/{VmSortSpecs,InfrastructureSortSpecs}.cs`.

**Proposed fix.** Teach the shared keyset mechanism (`ApplyKeysetFilter`, or `SortSpec<T>`) to detect a nullable underlying key type and build a null-safe comparison generically — e.g. a consistent NULLS-FIRST/LAST ordering with the `id` tiebreaker, so no caller has to remember to coalesce. Then remove the per-column `?? ""` workarounds and, before slice-4, decide whether the JSONB selectors adopt the generic guard or slice-4 enforces attribute presence on ingest.

**Why deferred.** Touches `Kartova.SharedKernel.Postgres`, which every cursor list in the app depends on — too broad and too risky to change inside a VM slice. Unreachable today for the JSONB fields (validation guarantees non-null keys).

**Acceptance.** Nullable-column keyset pagination returns all rows across a NULL boundary (add a shared-mechanism test seeding NULL boundary values); the per-column `?? ""` workarounds are gone; a new nullable sort field is null-safe without extra code.

---

### TD-002 — Shared concurrency-token capture via EF metadata

**Status:** done (branch `chore/tech-debt-td-001-002-003`) — single `ConcurrencyTokenCapture.TryCaptureCurrentVersionAsync` in `Kartova.SharedKernel.AspNetCore` resolves the token via `IProperty.IsConcurrencyToken`; `EditApplicationHandler`, `EditVmHandler`, `DeleteVmHandler` all use it; the two hard-coded copies (`"Version"`/`"Xmin"`) deleted.
**Origin:** E-02.F-04.S-01 slice 2a — review-pr (gate 7) reuse finding + deep-review. See `gate-findings.yaml`.

**Problem.** The "on optimistic-concurrency conflict, capture the current row version so the 412 response can carry a `currentVersion` hint" logic is duplicated, keyed by a hard-coded property-name string. Two near-identical copies exist, differing only by that string (`"Version"` vs `"Xmin"`). A third module would need a third copy.

**Affected files.**
- `src/Modules/Catalog/Kartova.Catalog.Infrastructure/InfrastructureConcurrency.cs` — `TryCaptureCurrentXminAsync` (reads `dbValues["Xmin"]`), shared by `EditVmHandler` + `DeleteVmHandler`.
- `src/Modules/Catalog/Kartova.Catalog.Infrastructure/EditApplicationHandler.cs` — `TryCaptureCurrentVersionAsync` (reads `dbValues["Version"]`), the pre-existing near-duplicate.
- `src/Kartova.SharedKernel.AspNetCore/ConcurrencyConflictExceptionHandler.cs` — already owns the READ side of `ex.Data["currentVersion"]`; natural home for the shared writer.

**Proposed fix.** Promote a single shared helper to `Kartova.SharedKernel.AspNetCore` (next to `ConcurrencyConflictExceptionHandler`) that resolves the concurrency-token property generically via EF metadata — `entry.Metadata.GetProperties().Single(p => p.IsConcurrencyToken())` — instead of a hard-coded name. Point both `EditApplicationHandler` and the VM handlers at it; delete the two copies.

**Why deferred.** Requires editing `EditApplicationHandler` (outside the VM slice) and adding to `SharedKernel.AspNetCore` — out of scope for slice 2a.

**Acceptance.** One shared capture helper; both Application and VM edit/delete paths use it; no hard-coded token-property strings; existing 412-with-`currentVersion` tests still pass.

---

### TD-003 — Frontend: never silently swallow an unmapped 400 error key

**Status:** done (branch `chore/tech-debt-td-001-002-003`) — `applyProblemDetailsToForm` now requires a `knownFields: ReadonlySet<string>` and counts/handles only mapped keys; unmapped keys fall through to the dialog's `toast.error`. Registered paths derived from each form's zod schema via new `zodFieldPaths` util; all 14 callers wired.
**Origin:** E-02.F-04.S-01 slice 2a — review-pr (gate 7) silent-failure finding (the deeper half of the fix; the source-side bug was already fixed in commit 361a710). See `gate-findings.yaml`.

**Problem.** `applyProblemDetailsToForm` iterates the server's `{ field: [messages] }` error map and calls `setError(field, …)`, returning `handled = true` for **any** key — even one that maps to no registered form field. Dialogs then do `if (handled) return;` and skip their toast fallback, so a 400 whose error key doesn't match a field renders **nothing** (no toast, no field highlight) — a silent failure indistinguishable from a hang.

**Current state.** The concrete slice-2a instance was fixed at the SOURCE (backend now returns keys matching the form field paths, e.g. `attributes.os`), so the specific VM path no longer hits it. But the shared FE handler still trusts any key, so a future unmapped key from any endpoint would silently vanish.

**Affected files.**
- `web/src/shared/forms/problemDetails.ts` — `applyProblemDetailsToForm` (the shared handler).
- Consumers: the catalog dialogs (`EditVmDialog`, `RegisterVmDialog`, `RegisterApplicationDialog`, …) that branch on its `handled` return.

**Proposed fix.** Make "handled" mean "actually applied to a real field": have `applyProblemDetailsToForm` count only keys that correspond to registered form fields (or return the set of unmapped keys), so a 400 with an unmapped key falls through to the dialog's generic `toast.error` fallback instead of vanishing. Add a test: a 400 body whose error key matches no field surfaces a visible toast.

**Why deferred.** `applyProblemDetailsToForm` is shared by every form dialog in the app; changing its contract risks regressions in unrelated forms — out of scope for a VM slice. Defense-in-depth (the actual VM bug is already fixed at source).

**Acceptance.** A 400 with an error key that matches no registered field produces a visible error (toast), never a silent no-op; existing per-field-highlight behavior for mapped keys is unchanged; covered by a test.
