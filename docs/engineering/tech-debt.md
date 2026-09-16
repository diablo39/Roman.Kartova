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

---

### TD-004 — Cursor does not bind `sortBy`; a mid-pagination sort-field change mis-pages silently

**Status:** done (2026-09-10) — `sf` discriminator folded into the cursor (`CursorCodec` `{ s?, i, d, f?, n?, sf? }`); `ToCursorPagedAsync` encodes `sort.FieldName` and rejects a decoded `sf` ≠ request `sortBy` with `CursorSortFieldMismatchException` → 400 `cursor-sort-field-mismatch` (ADR-0095 amended 2026-09-10). Backward compatible: absent `sf` (old cursor) = "no field recorded", no check. **No per-handler change** — the guard lives in the shared extension, which already receives the `SortSpec<T>`; the TD note's `CursorListBinding` + per-module edits were unnecessary. Covered by extension tests (mismatch 400 / matching passes / old-cursor accepted / next-cursor round-trips `sf`), codec round-trip tests, and a `PagingExceptionHandler` mapping test.
**Origin:** TD-001 slice (2026-09-10) — review-pr (gate 7) silent-failure finding, noted as pre-existing and out of TD-001 scope. See `docs/superpowers/verification/2026-09-10-tech-debt-td-001-002-003/deep-review.md` (should-fix) and `gate-9-verification.md`.

**Problem.** The ADR-0095 cursor payload `{ s, i, d, f?, n? }` binds the sort **order** (`d`) and the filter state (`f`) but NOT the sort **field** (`sortBy`). `ToCursorPagedAsync` validates `decoded.Direction != order` and the filter map, but nothing checks that the request's `sortBy` matches the field the cursor was issued under. A client that changes `sortBy` mid-pagination while keeping `sortOrder` and reusing the cursor passes both guards, then the new field's keyset predicate is applied against the **previous** field's boundary value. When the two fields share a comparable CLR type (e.g. two strings, two timestamps) this does not error — it silently skips or repeats rows. Same failure class as the `f`-map mismatch that ADR-0095's 2026-06-01 amendment already guards, but for the sort key rather than the filters.

**Affected files.**
- `src/Kartova.SharedKernel/Pagination/CursorCodec.cs` — `CursorPayload` / `DecodedCursor` (would carry a sort-field discriminator).
- `src/Kartova.SharedKernel.Postgres/Pagination/QueryablePagingExtensions.cs` — `ToCursorPagedAsync` (adds the `sortBy` equality check next to the existing `decoded.Direction != order` check).
- `src/Kartova.SharedKernel.Postgres/Pagination/CursorListBinding.cs` + the per-module list handlers that resolve `sortBy` → `SortSpec` (supply the field name to encode).
- ADR-0095 (owns the cursor wire format) — a further backward-compatible amendment.

**Proposed fix.** Fold a sort-field discriminator into the cursor (e.g. a `sf` field carrying the resolved `SortSpec.FieldName`), encoded on issue and required to equal the request's `sortBy` on decode — mismatch → 400, mirroring `CursorFilterMismatchException`. Backward compatible: an absent `sf` (old cursor) is treated as "no field recorded" per the codec's forward-compat convention. Prefer routing it through the existing `f`-map machinery vs. a bespoke field if that keeps the codec simpler.

**Why deferred.** Pre-existing (not introduced by TD-001); touches the shared cursor codec + every cursor list + ADR-0095 — too broad to fold into the VM null-safety slice. Low real-world incidence today (clients don't normally flip `sortBy` mid-scroll), but it's a latent skip/repeat identical in class to the already-guarded filter-mismatch case.

**Acceptance.** A cursor issued under one `sortBy` and replayed under a different `sortBy` (same `sortOrder`) returns a 400, not silently mis-paged rows; existing single-field pagination is unchanged; covered by a shared-mechanism test seeding a sort-field switch across a page boundary.

---

### TD-005 — Infrastructure absent from the Catalog Hierarchy read model

**Status:** done (branch `chore/tech-debt-td-005-006`) — `GetCatalogHierarchyHandler` sources `db.Infrastructure` as components; `HierarchyAssembler.KindWire` maps `EntityKind.Infrastructure → "infrastructure"`; hierarchy page renders infra members (HardDrive icon, `entityDetailPath` link). Verified live (gate 9): an assigned VM appears under its System in `GET /catalog/hierarchy` + the Hierarchy page. See `docs/superpowers/verification/2026-09-16-hierarchy-infra-members/`.
**Origin:** E-02.F-04.S-01 slice 2b (VM linking) — review-pr (gate 7) code-reviewer Important finding. Ruled a deferred follow-up (design scoped VM System membership to the VM side + `/graph`; infrastructure is render-only in the FE relationship model).

**Problem.** This slice makes `PartOf: Infrastructure → System` a real writable edge (via `POST /relationships` and `PUT /infrastructure/{id}/system`). But `GET /catalog/hierarchy` builds its component set only from Applications + Services, so a VM assigned to a System is silently dropped from the hierarchy tree — visible on the System detail page (generic `/relationships` read) and `/graph`, but missing from the hierarchy read model. No crash; a silent omission / read-model inconsistency.

**Affected files.**
- `src/Modules/Catalog/Kartova.Catalog.Infrastructure/GetCatalogHierarchyHandler.cs` — builds `ComponentRow`s from `db.Applications` + `db.Services` only.
- `src/Modules/Catalog/Kartova.Catalog.Application/HierarchyAssembler.cs` — `KindWire` throws for anything but Application/Service.
- Frontend Catalog Hierarchy page (renders the hierarchy DTO).

**Proposed fix.** Add an Infrastructure `ComponentRow` source in the handler + an `"infrastructure"` case in `KindWire` (wire-contract addition), and render infra members in the hierarchy page. Decide the surface treatment (icon/label) consistently with the `/graph` infra node.

**Why deferred.** A read model the slice never intended to touch; requires a hierarchy wire-contract change + FE rendering decision. Membership is still manageable + visible elsewhere.

**Acceptance.** A VM assigned to a System appears under that System in `GET /catalog/hierarchy` and the Hierarchy page, covered by a test; existing hierarchy behavior unchanged.

---

### TD-006 — SystemMembersSection doesn't handle Infrastructure members

**Status:** done (branch `chore/tech-debt-td-005-006`) — `asComponentKind` uses the shared `isPartOfSourceKind` (accepts infrastructure → Remove works); member link gated on `isEntityKind` so infra links via `entityDetailPath`. Single FE source of truth `PART_OF_SOURCE_KINDS` mirrors backend `RelationshipTypeRules.IsPartOfSourceKind`. Also fixed a latent dead-link bug: `entityDetailPath("infrastructure")` now routes to `/catalog/infrastructure/vms/{id}` (was 404). Verified live (gate 9). See `docs/superpowers/verification/2026-09-16-hierarchy-infra-members/`.
**Origin:** E-02.F-04.S-01 slice 2b (VM linking) — review-pr (gate 7) type-design Important finding. Ruled a deferred follow-up (same scope boundary as TD-005).

**Problem.** A VM assigned to a System (now possible) appears in that System's Members table (read path applies no type filter), but `SystemMembersSection.asComponentKind` returns `null` for `"infrastructure"`, and `isRelationshipKind` excludes it — so the VM member renders as a plain unlinked `<span>` with no Remove button. The membership is only removable from the VM's own detail page. `asComponentKind` is a third stale copy of the "which kinds can be PartOf" list (the backend unified this via `RelationshipTypeRules.IsPartOfSourceKind`).

**Affected files.**
- `web/src/features/catalog/components/SystemMembersSection.tsx` — `asComponentKind` (line ~27), the member row rendering (link via `isRelationshipKind`, Remove gated on `asComponentKind`).
- `web/src/features/catalog/relationships/relationshipTypeRules.ts` — `isRelationshipKind` (render-only exclusion of infrastructure is deliberate; revisit if infra links are wanted here).

**Proposed fix.** Include `"infrastructure"` in `asComponentKind` (so Remove works — `useSetComponentSystem` already supports it), render infra members with a link via `entityDetailPath` (which supports infrastructure), and add a test. Consider a single shared source for the PartOf-eligible kind list across `ComponentKind`/`asComponentKind`/`IsPartOfSourceKind`.

**Why deferred.** Touches a pre-existing shared component outside the slice diff; the design kept infrastructure render-only in the relationship model. Not a defect — a UX-parity limitation; membership is manageable from VM detail.

**Acceptance.** A VM member in a System's Members table renders as a link and has a working Remove; covered by a test; the PartOf-eligible kind list has one source of truth.

---

### TD-007 — VM entity-search is not text-filtered (ListVms lacks displayNameContains)

**Status:** done (branch `feat/catalog-vm-system-assign-search`) — `DisplayNameContains` added to `ListVmsQuery`/`ListVmsHandler` (case-insensitive substring `ILIKE` over `DisplayName`, `LikeEscaping.EscapeLike` wildcards, encoded into the cursor f-map), `[FromQuery] displayNameContains` on `ListVmsAsync`, snapshot + client regenerated, `useEntitySearch` infra branch now passes the term. Mirrors `ListServices` exactly. Real-seam integ (case-insensitive match + wildcard-escaping) + `BuildFilterMap` unit test. Registry VM row updated.
**Origin:** E-02.F-04.S-01 slice 2b (VM linking) — controller ruling during SDD (Task 6).

**Problem.** `useEntitySearch("infrastructure", q)` (used by the DeployOnVm VM picker) hits `GET /catalog/infrastructure/vms`, which has no `displayNameContains` parameter, so the typeahead shows the first N VMs by displayName regardless of typed text. Fine at small VM counts; poor UX at scale.

**Affected files.**
- `src/Modules/Catalog/Kartova.Catalog.Infrastructure/` — `ListVmsQuery` / VM list endpoint (add `displayNameContains`).
- `web/src/features/catalog/api/relationships.ts` — `useEntitySearch` infrastructure branch (pass the param once available).

**Proposed fix.** Add `displayNameContains` to the VM list query (mirroring Applications/Services/Apis/Systems list endpoints) and pass it from the infra entity-search branch.

**Why deferred.** Belongs to the VM list surface (E-02.F-04), not the linking slice; the picker works without it.

**Acceptance.** Typing in the DeployOnVm VM picker narrows results server-side; existing VM list behavior unchanged.

---

### TD-008 — Frontend vitest suite flakes on timeout under full-suite load

**Status:** open
**Origin:** Observed during E-02.F-04.S-01 slice 2b (VM linking) gate-8 + terminal re-verify (2026-09-15). Not caused by the slice — the flaking tests are in untouched files.

**Problem.** Running the full web suite (`npx vitest run`, ~1126 tests) intermittently fails 1-3 tests with 5000-10000ms timeouts — seen in `ServiceDetailPage.test.tsx` ("renders a not-found card on error") and `ApplicationDetailPage.test.tsx`. Each passes green + fast (~5s) when the file is run in isolation. The full run reports very high cumulative import/setup/environment time (import ~980s, setup ~107s, environment ~820s across workers), pointing at heavy per-file module-import/setup cost that starves individual tests of their timeout budget under parallel load. This is a CI-stability risk for the Frontend job (gate 10).

**Affected files.** Suite-wide (jsdom env + import cost), surfacing in `web/src/features/catalog/pages/__tests__/{ServiceDetailPage,ApplicationDetailPage}.test.tsx`. Root cause is shared test setup/import weight, not these tests specifically.

**Proposed fix.** Investigate the import/setup cost (heavy barrel imports? per-file MSW/provider setup?); options: raise `testTimeout` for the affected suites, reduce shared-setup cost, cap vitest worker concurrency (`--poolOptions`/`maxWorkers`) to trade wall-clock for stability, or split the heaviest files. Confirm against a CI run.

**Why deferred.** Pre-existing, suite-wide infrastructure concern unrelated to the VM-linking feature; fixing it inside the slice would over-broaden scope.

**Acceptance.** The full web suite passes deterministically under CI load without per-test timeout flakes; isolated and full-run results agree.

---

### TD-009 — System-side "Assign component" dialog can't assign an Infrastructure member

**Status:** done (branch `feat/catalog-vm-system-assign-search`) — added an "Infrastructure" radio to `AddSystemMemberDialog.KINDS` (combobox + `useSetComponentSystem` were already generic over `ComponentKind`/`PartOfSourceKind`); infra placeholder reads "Search VMs…". Picker narrows server-side via TD-007. FE tests: Infrastructure radio searches `infrastructure`; selecting a VM drives `useSetComponentSystem({ componentKind: "infrastructure", ... })`. Verified live at gate 9.
**Origin:** Discovered during TD-005/006 slice gate-9 visual verification (2026-09-16). Out of that slice's render scope.

**Problem.** `AddSystemMemberDialog` (the System detail → Members → "Assign component" dialog) offers only **Application / Service** as component-kind radios, so a VM cannot be assigned to a System from the System side. Infrastructure membership is only settable from the VM detail → *Assign* system dialog (which works). Now that infra members render + are removable in the System Members table (TD-006) and appear in the hierarchy (TD-005), the missing System-side assign path is a UX-parity gap: a steward viewing a System cannot add a VM to it in place.

**Affected files.**
- `web/src/features/catalog/components/AddSystemMemberDialog.tsx` — the component-kind radio group (add `infrastructure`) + its entity-search branch.
- `web/src/features/catalog/api/relationships.ts` — `useEntitySearch("infrastructure", …)` (already exists; note TD-007 — infra search is not text-filtered server-side).
- Backend already supports it: `useSetComponentSystem` routes infrastructure to `PUT /catalog/infrastructure/{id}/system` (used by the VM-side dialog).

**Proposed fix.** Add an "Infrastructure" radio to `AddSystemMemberDialog`, wire the infra entity-search branch, and drive the existing `useSetComponentSystem` mutation with `componentKind: "infrastructure"`. Add a test. Consider pairing with TD-007 (server-side VM text search) so the picker narrows at scale.

**Why deferred.** TD-005/006 scoped to *surfacing* already-assigned infrastructure (read model + members render). The System-side assign path is a distinct write-UI capability; folding it in would broaden the slice.

**Acceptance.** A steward can assign a VM to a System from the System's "Assign component" dialog; the member then appears in the Members table + hierarchy; covered by a test.
