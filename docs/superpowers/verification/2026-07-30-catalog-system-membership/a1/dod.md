# DoD Ledger — Catalog System Membership A1

**Slice:** `2026-07-30-catalog-system-membership-a1` · **Branch:** `feat/catalog-system-membership-a1` · **HEAD:** `283eac2f` (Task 11 docs commit; Task 12 docs+E2E fix commit follows this ledger's own commit)
**PR:** not yet opened
**Last updated:** 2026-07-31
**Spec:** `docs/superpowers/specs/2026-07-30-catalog-system-membership-a1-design.md`
**Plan:** `docs/superpowers/plans/2026-07-30-catalog-system-membership-a1.md` (local scratch, gitignored)
**Controller ledger (full task-by-task history):** `.superpowers/sdd/2026-07-30-catalog-system-membership-a1/progress.md`
**Migration collapse evidence:** `./migration-collapse-verification.md` (+ `.sql`)
**Findings telemetry:** `./gate-findings.yaml`

> Records the Definition of Done from `CLAUDE.md`. Update each row the moment its gate runs.
> Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A — FAIL and N/A require a one-line reason.
> This table records each gate's **status**; what each gate **found** (and whether it was real) goes in `gate-findings.yaml`.

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ✅ PASS | 2026-07-30 |
| 2 Per-task subagent reviews | ✅ PASS | 2026-07-30 |
| 3 Full suite (+ real-seam if wiring) | ✅ PASS (1 unrelated pre-existing flake) | 2026-07-30 |
| 4 Container build (images CI) | ✅ PASS | 2026-07-30 |
| 5 `/simplify` | ⏳ PENDING | — |
| 6 Mutation (blocking — Domain/Application changes) | ⏳ PENDING | — |
| 7 `requesting-code-review` | ⏳ PENDING | — |
| 8 `review-pr` | ⏳ PENDING | — |
| 9 `deep-review` | ⏳ PENDING | — |
| Terminal re-verify (build + suite) | ⏳ PENDING | — |
| 10 Visual / API verification (ADR-0084) | ⏳ PENDING | — |
| 11 CI green on PR (`ci-local.sh` = pre-push mirror) | ⏳ PENDING | — |

**E2E impact audit (Task 12, part of the "touched a flow an E2E covers" trigger, not a numbered DoD gate):** ✅ done — see "E2E impact audit" section below. 5/5 specs green, one fixture bug this slice introduced was found and fixed.

## Gate detail

### 1 — Build (`TreatWarningsAsErrors=true`)
**Status:** ✅ PASS
**Evidence:** `Directory.Packages.props:50` confirms `TreatWarningsAsErrors` is repo-wide config (Task 1 review verified this from the pre-existing project config, not this diff). Task 10's verification sweep and the per-task subagent commits (Tasks 1–9, `progress.md`) all built clean under this setting; no task reported a warning. 0 warnings, 0 errors across the branch.
**At:** commits bc55263a..283eac2f (progress.md, Tasks 0–11)

### 2 — Per-task subagent reviews (spec + quality)
**Status:** ✅ PASS
**Evidence:** Every one of the 10 implementation tasks (0–9) went through a subagent spec-compliance + quality review, recorded in `progress.md`. Several needed fix rounds before going clean:
- Task 1: review clean (spec OK, quality Approved).
- Task 2: DONE_WITH_CONCERNS → review 1 (2 important/2 minor) → fix round 1/5 → re-review ADDRESSED → complete.
- Task 3: review 1 FAILED (not for its own deliverables — forwarded finding to Task 4) / quality Needs fixes (2 important, 9 minor) → fix round 1/5 (7 addressed) → re-review ADDRESSED → complete. Plus controller-only extra: hand-verified migration `Up()` against a real Postgres.
- Task 4: review 1 clean (0 critical, 0 important, 3 minor; both implementer deviations independently confirmed correct).
- Task 4d: review 1 quality Needs fixes (weak test oracles) → fix round 1/5 → re-review clean.
- Task 5: review 1 clean (0 critical, 0 important, 2 minor).
- Task 6: review 1 quality Needs fixes (1 important, 2 minor — untested `useSetComponentSystem`) → fix round 1/5 → re-review ADDRESSED.
- Task 7: review 1 quality Needs fixes (weak dialog-contract tests) → fix round 1/5 (4 new tests) → re-review clean. Plus a user-requested `postgresql-expert` review of the migration (1 blocking, fixed 9e14813e; 1 should-fix; 2 invariants confirmed).
- Task 8: review 1 quality Needs fixes (3 important — real functional bug: row ignored `isLoading`/`isError`) → fix round 1/5 → re-review clean.
- Task 9: review 1 quality Needs fixes (2 important — 4 untested branches) → fix round 1/5 → re-review clean.
No review was skipped as "trivial."
**At:** commits bc55263a..bf7f5d81 (Tasks 0–9)

### 3 — Full test suite (unit + arch + integration; real-seam if wiring)
**Status:** ✅ PASS — with one unrelated pre-existing flake noted below
**Evidence:**
- Backend unit: 270/270.
- Architecture (NetArchTest): 69/69.
- Backend integration (real Postgres/RLS, Testcontainers): `SetComponentSystemTests` 20/20 + `Relationship*Tests` 63/63 (final count after Task 4d's `type` filter work; controller re-ran the two membership suites together at 78/78 with `CreatePartOfRelationshipTests`, 0 skipped, per Task 4/progress.md).
- Frontend: 911/911 across 126 files (Task 10's full-suite run).
- **Known pre-existing flake:** `web RegisterServiceDialog.test.tsx` fails under full-suite load (502/503, ~5.1s timeout observed at Task 9) but passes 9/9 in isolation. Last touched in PRs #36/#39 — untouched by this branch. It did NOT fire on Task 10's run (911/911 clean), confirming it's load-dependent, not a real regression. Any future full-suite run that shows this file red for the wrong reason should re-run it in isolation before calling gate 3 red (per the project's "Full-suite Docker flake" pattern).
**At:** commits bc55263a..bf7f5d81 (Tasks 1–9), full sweep at Task 10 (commit 2777adc5 + verification)

### 4 — Container build (images CI job)
**Status:** ✅ PASS
**Evidence:** All three images built green: `docker compose build api` → `kartova/api:dev`; `docker compose build migrator` → `kartova/migrator:dev` (needed explicitly — building `api` alone left a stale migrator that reported "already up to date" against an unmigrated DB, a real gotcha found at Task 6); `docker compose build web` → `kartova/web:dev`. Real-DB migration also verified live (not just Testcontainers): `20260730160400_AddOneSystemPerComponentIndex` applied, index confirmed with the correct partial predicate `WHERE type = 'PartOf'`, RLS (`ENABLE`+`FORCE`) confirmed restored on the live table.
**At:** Task 6 (api build/migrate) + Task 10 (migrator/web build), commits a1052293..2777adc5

### 5 — `/simplify` against branch diff
**Status:** ⏳ PENDING
**Evidence:** Not yet run. Candidates already flagged for it rather than churned early: possible modal/mutate-toast duplication between `AddSystemMemberDialog` and `AssignSystemDialog` (Task 9 minor); the 5-line "find current PartOf target" query duplicated between the endpoint pre-check and the 409 catch filter (Task 4 minor); `ENTITY_KIND_LABEL` still `Record<string,string>` rather than `Record<EntityKind,string>` (Task 5 minor).
**At:** —

### 6 — Mutation loop (blocking for this slice — diff touches Domain/Application logic)
**Status:** ⏳ PENDING
**Evidence:** Not yet run. Per the plan's Task 12 Step 4, scope is `SystemMembership.cs`, `SetComponentSystemHandler.cs`, and the changed `CatalogEndpointDelegates.cs` region; target ≥80%. **Predicted survivors — accept, do not re-litigate** (from the plan's pre-implementation gap analysis):
| Predicted survivor | Why acceptable |
|---|---|
| Handler's `r.Source.Kind == cmd.Component.Kind` filter | Only killable by two entities of different kinds sharing one Guid — not constructible through any endpoint |
| `r.Type == PartOf` in the authorization projection (not the handler's) | Mutating it can only make authz stricter; block is skipped for OrgAdmin/component-team callers, so no legitimate 200 turns into 403 |
| `?? Guid.Empty` on a dangling System reference | Requires an edge pointing at a purged System — no seeding path once the index exists |
| `23505` → 409 mapping | Needs a real write-write race; not reproducible in a single-process suite. Structurally covered by `Two_partOf_edges_for_one_component_are_refused_by_the_database` (DB refuses); the mapping itself is the untested inch |
| `SetComponentSystemHandler`'s `if (removed.Count > 0 \|\| added is not null)` save guard → unconditional `SaveChangesAsync` | Equivalent mutant: an empty EF changeset issues no SQL, so no assertion at any tier can distinguish the two. ADR-0090 still wants the explicit skip written; ruled acceptable at Task 2 review, not by Stryker |
If a survivor outside this table appears, it is a real gap and needs a test.
**At:** —

### 7 — `requesting-code-review` at slice boundary
**Status:** ⏳ PENDING
**Evidence:** Not yet run (must run against the full branch diff, spec + plan as context).
**At:** —

### 8 — `review-pr` (pr-review-toolkit)
**Status:** ⏳ PENDING
**Evidence:** Not yet run. Per CLAUDE.md, never fold into 7/9 — run for real.
**At:** —

### 9 — `deep-review`
**Status:** ⏳ PENDING
**Evidence:** Not yet run (against spec/plan/ADRs/tests).
**At:** —

### Terminal re-verify (build + full suite after gates 5–9)
**Status:** ⏳ PENDING
**Evidence:** Gates 5–9 may apply fixes; re-run build + full suite on the final commit once they've landed.
**At:** —

### 10 — Visual / API verification (observe the running system)
**Status:** ⏳ PENDING
**Evidence:** Per ADR-0084: cold-start dev server, authenticate, in-SPA navigate, assign → change → remove a System membership, screenshot the changed surface, confirm 0 console errors. Not yet run.
**At:** —

### 11 — CI green on the PR (terminal; `scripts/ci-local.sh` = required pre-push mirror)
**Status:** ⏳ PENDING
**Evidence:** No PR opened yet. `scripts/ci-local.sh` must be run and green before push.
**At:** —

## Deployment note (recorded per Task 3 / Task 12 instruction)

The `AddOneSystemPerComponentIndex` migration **collapses duplicate `PartOf` rows itself** (oldest survives per `(tenant_id, source_kind, source_id)` group) before creating the partial unique index — see `./migration-collapse-verification.md` for the standalone-Postgres proof (`DELETE 2`, oldest survives, other tenant/kind/type edges intact, RLS restored). The manual pre-flight query
```sql
SELECT tenant_id, source_kind, source_id, count(*)
FROM relationships WHERE type='PartOf'
GROUP BY 1,2,3 HAVING count(*) > 1;
```
is therefore an **audit, not a prerequisite** — it exists to tell an operator what will be collapsed, not to block the migration. It must run as superuser/bypass-RLS role, or RLS will hide the rows and the audit will under-report. On the dev DB (Task 6), it found 1 `PartOf` row and 0 duplicate groups, so nothing was actually collapsed there — **a real database with genuine duplicate `PartOf` groups remains the untested case** for the collapse path outside the standalone Postgres harness.

## Deferred minors (carried forward from `progress.md`, not re-litigated here)

- Task 1: `Two_non_matching_edges_are_both_deleted_before_inserting` asserts `Count==2` but not which ids (no live mutation escape today, other tests pin per-id behavior).
- Task 2: response ternary echoes `cmd.SystemId` rather than deriving from persisted state (correct today, worth a comment); no try/catch around `audit.AppendAsync` (matches existing `CreateRelationshipHandler` convention).
- Task 3: commit `21de0c6c` doesn't compile standalone (references symbols added in `e2477e5`) — harmless, repo squash-merges PRs; 409-translation path untested (needs true concurrency, accepted as a gate-6 survivor, see above).
- Task 4: catch filter lacks kind-scoping the pre-check has (provably dead today, a trap if S-02 loosens nesting); duplicated "find current PartOf target" query (5 lines, pre-check + catch) → routed to gate 5; lost-race branch has no runtime coverage (precedented elsewhere).
- Task 4d: `excludeApiEdges` is not encoded into the cursor's `expectedFilters` map (pre-existing, wider than first reported — before this change the endpoint had NO expectedFilters at all); candidate follow-up for the ADR-0095 discipline, out of this slice's scope.
- Task 5: "partOf never offerable" test is vacuous (partOf was never in `CreatableRelationshipType`); `ENTITY_KIND_LABEL` still `Record<string,string>`.
- Task 6: no dedicated unit test for `useSetComponentSystem` beyond what Tasks 7–9 exercise through the dialogs (deliberate, brief only required `useComponentSystem`); `useMutation` `TError` defaults to `Error` not `unknown`, so status-branching consumers need a cast (noted for Tasks 7–9, not fixed).
- Task 7: combobox not disabled while mutation pending (Remove is; asymmetry inherited from the mirrored `SetSuccessorDialog` idiom); Close button bypasses `isDismissable` while pending (same idiom); ~15 lines of modal chrome duplicated across the two dialogs → routed to gate 5.
- Task 9: possible modal/mutate-toast duplication between `AddSystemMemberDialog` and `AssignSystemDialog` → routed to gate 5; unlabelled actions column header matches existing `RelationshipsSection.tsx:114` convention.

None of these are re-opened by Task 12; gate 5/6 pick up the ones explicitly routed to them.

## E2E impact audit (Task 12)

Suite: `e2e/tests/` — `detail-tabs.spec.ts`, `lifecycle-override.spec.ts`, `relationship-drift.spec.ts`, `smoke.spec.ts`, `spec-render-readonly.spec.ts`.

**Audited, unaffected (confirmed, not just eyeballed):**
- `detail-tabs.spec.ts` — API detail page only, no System row on that entity kind. Ran green.
- `smoke.spec.ts` — Applications list only. Ran green.
- `lifecycle-override.spec.ts` — lifecycle/decommission dialog only, no relationships/System surface. Ran green.
- `spec-render-readonly.spec.ts` — API spec-render tab only. Ran green.
- None of the 5 specs click a `Remove` control today (`grep -rn "window.confirm\|page.on(\"dialog\"" e2e` → no matches), so the `page.on("dialog", d => d.accept())` requirement from the brief does not yet apply to any existing spec. Recorded so a future Members-tab Remove spec knows to add it.

**`relationship-drift.spec.ts` — audited AND fixed, two distinct findings:**
1. **Query-key collision (the diagnosed risk from Task 4d/6): confirmed gone.** The spec's `page.waitForResponse` on the Dependencies-tab click did not time out — it resolved 200 immediately. `useComponentSystem`'s `type=partOf&limit=1` key is provably distinct from `RelationshipsSection`'s unfiltered key; the `staleTime: 30_000` suppression from the #70 retro class does not recur.
2. **NEW break found by actually running the spec, not predicted by the brief: the fixture's "drift" premise was invalidated by this slice.** `e2e/fixtures/db.ts`'s `insertDriftEdge` inserted a `type='PartOf'` row specifically because `PartOf` was **not** a member of `Kartova.Catalog.Domain.RelationshipType` (removed in PR #58, replaced by `InstanceOf`). This slice (commit `3ebe95ba`) **re-added `PartOf`** as a real, visible `RelationshipType` for System membership — `EfRelationshipConfiguration.cs:18-19` has its own comment acknowledging "`'PartOf'` is now a valid, visible relationship type... it is no longer excluded by this filter." So the fixture's row stopped being drift: it rendered as a normal `partOf` outgoing **and** incoming relationship (self-referential edge), and the spec's final assertion (`"No outgoing relationships."` must be visible) failed — not a timeout, a real UI-content mismatch. Screenshot/error-context confirmed a `partOf` row with 1 result in both the Outgoing and Incoming grids and in the dependency graph.
   **Fix applied:** changed the fixture's inserted `type` value from `'PartOf'` to `'LegacyUnmappedType'` (a string guaranteed not to match any current `RelationshipType` member) in `e2e/fixtures/db.ts`, with an in-file comment explaining why `'PartOf'` can no longer be used for this purpose. Updated `relationship-drift.spec.ts`'s comments to stop naming `'PartOf'` as the drift value. Re-ran: green.
3. **`e2e/fixtures/db.ts`'s fixture + the new unique index — checked, no live risk.** `DevSeed.cs` seeds no `System` rows and no `PartOf`/membership relationships at all (`grep` for `System|PartOf` in `DevSeed.cs` → no matches) — `FIXTURE_APP_ID` has no real System membership today, so the fixture's single drifted-type insert cannot collide with `ux_relationships_one_system` (which only fires on `type='PartOf'`, and the fixture no longer uses that value after the fix above). Cleanup is a `finally`-block `DELETE ... WHERE id = $1` scoped to the row's own generated id, so it cannot leak between runs even under retries. Risk would only reopen if a future seed/spec gives `FIXTURE_APP_ID` a real System membership AND some other code path reintroduced `'PartOf'` as the drift literal — neither is true today.

**Commands run (real output, not assumed):**
```
$ bash e2e/run.sh relationship-drift.spec.ts     # first run — pre-fix
...
1 failed
  [chromium] › tests\relationship-drift.spec.ts:6:1 › ... (retries exhausted)
  Error: expect(locator).toBeVisible() failed
  Locator: getByText('No outgoing relationships.')
```
```
$ cd e2e && npx playwright test relationship-drift.spec.ts   # after the fixture fix
Running 1 test using 1 worker
  ✓  1 [chromium] › tests\relationship-drift.spec.ts:6:1 › drift: an unmappable relationship.type does not 500 the relationships surface (3.8s)
1 passed (6.6s)
```
```
$ cd e2e && npx playwright test   # full suite, post-fix
Running 5 tests using 1 worker
  ✓  1 [chromium] › tests\detail-tabs.spec.ts:5:1 › ...
  ✓  2 [chromium] › tests\lifecycle-override.spec.ts:5:1 › ...
  ✓  3 [chromium] › tests\relationship-drift.spec.ts:6:1 › ...
  ✓  4 [chromium] › tests\smoke.spec.ts:4:1 › ...
  ✓  5 [chromium] › tests\spec-render-readonly.spec.ts:5:1 › ...
5 passed (16.7s)
```
Environment: docker stack already up (postgres, keycloak, api healthy; `kartova/api:dev`, `kartova/migrator:dev`, `kartova/web:dev` freshly built per this branch's Task 6/10 work) — `bash e2e/run.sh` brought up `web` (not previously running) via `docker compose up -d --build`, then ran Playwright against it; subsequent runs used `npx playwright test` directly against the already-up stack.

Files touched: `e2e/fixtures/db.ts` (fixture drift-value fix), `e2e/tests/relationship-drift.spec.ts` (comment updates only, no assertion changes). No new spec added — per the plan's instruction, a membership-flow E2E spec is deferred to gate 10 follow-up, not built here.
