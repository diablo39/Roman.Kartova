# Deep Review — `feat/e2e-spec-render-tabs`

**Reviewed:** `git diff master...HEAD` (branch `feat/e2e-spec-render-tabs` vs `master`)
**Spec:** `docs/superpowers/specs/2026-07-20-e2e-spec-render-tabs-design.md`
**Plan:** `docs/superpowers/plans/2026-07-20-e2e-spec-render-tabs.md`
**DoD ledger:** `docs/superpowers/verification/2026-07-20-e2e-spec-render-tabs/dod.md`
**Findings telemetry:** `docs/superpowers/verification/2026-07-20-e2e-spec-render-tabs/gate-findings.yaml`
**Status:** OPEN — pre-merge gate

---

### Overview

This slice adds a fixed-id REST API + minimal OpenAPI 3.0 spec fixture to `DevSeed.cs`, plus two new nightly Playwright specs (`spec-render-readonly.spec.ts`, `detail-tabs.spec.ts`) that lock the #69/#70 deferred follow-ups — the spec-render read-only lock and the `DetailTabs` (ADR-0114) tab-switch happy path — against regression on the real stack. No production/business component is touched; the diff is dev-fixture wiring + tier-5 E2E test code only (`src/Kartova.Migrator/DevSeed.cs`, `e2e/fixtures/nav.ts`, `e2e/tests/*.spec.ts`).

### Blocking-class issues

**Title: DoD gates 5 (`/simplify`) and 8 (`review-pr`) are not-yet-run and are not Docker-blocked, so the branch does not meet CLAUDE.md's "ten always-blocking gates green" bar**
- Evidence: `docs/superpowers/verification/2026-07-20-e2e-spec-render-tabs/dod.md:20` (`5 /simplify | ⏳ not yet run`) and `dod.md:23` (`8 review-pr | ⏳ not yet run`). Both gates are pure static/code-review passes with no Testcontainers/Docker/compose dependency — unlike gates 3/4/10/11, which the ledger correctly defers to CI because Docker is unavailable on this host (`dod.md:9`).
- Impact: CLAUDE.md §Definition of Done states completion/merge-ready claims require all ten gates green, run for real, never folded ("never mark a gate covered by another... an owner-waived conditional gate is recorded as a waiver, not green"). Gates 5 and 8 sitting at "not yet run" with no Docker excuse means this PR is honestly not yet at "ready to merge," independent of code correctness.
- Fix: run `/simplify` against the full branch diff and `pr-review-toolkit:review-pr`, then update `dod.md` rows 5 and 8 with commit SHAs / findings (mirroring how gates 2 and 7 already cite commits). Note: some cleanup consistent with a `/simplify` pass is already visible in the diff (see Should-fix below) — if that was in fact produced by an ad-hoc fix wave rather than a formal `/simplify` run, running it for real may surface nothing new, but the gate still needs to be executed and the ledger row must reflect it with evidence, not stay "not yet run."

### Should-fix issues

**Title: `dod.md` gate 5 row is stale — it says "not yet run" while the shipped diff already shows the `/simplify`-style DRY cleanup it would have produced**
- Evidence: `docs/superpowers/verification/2026-07-20-e2e-spec-render-tabs/dod.md:20` vs. `e2e/fixtures/nav.ts` (diff) — the shipped file has no `API_DETAIL_URL` export (the plan's `docs/superpowers/plans/2026-07-20-e2e-spec-render-tabs.md:156` template included it), and both new specs consistently use the `FIXTURE_API_NAME` constant instead of the literal `"E2E Spec Render Fixture"` the plan's spec templates hard-coded (plan lines 214, 282).
- Impact: the ledger is the queryable source of truth CLAUDE.md mandates ("a 'what's the DoD status?' question is answered by reading that file's summary table") — a row that undersells completed work is as much a ledger-accuracy defect as one that oversells it, and it's what let gate 5 sit unresolved into a pre-merge review instead of being closed out at authoring time.
- Fix: attribute the DRY changes to the commit/gate that actually produced them (`gate-findings.yaml` currently only logs the Task-3/7 fix-wave findings, not this cleanup) and flip `dod.md:20` to ✅ with that citation, or explicitly run `/simplify` now and record its (possibly empty) diff.

### Nits

**Title: No test proves client-side (non-reload) unmount when navigating back from Definition to Overview**
- Evidence: `e2e/tests/detail-tabs.spec.ts:9-24` — the "only active panel mounts" property is proven Overview→Dependencies (line 18, `Description` heading gone) and Overview→Definition (line 21 `Spec view` group absent by default), but the reverse (`Definition` → `Overview` via a `role=tab` click, proving `.scalar-render`/Scalar unmounts client-side rather than just on a fresh `page.goto`) is never exercised — the only return-to-Overview path in the spec is a full navigation (`page.goto(...?tab=bogus)`, line 34), which remounts everything fresh rather than testing in-place unmount.
- Impact: low — `DetailTabs`' conditional-render mechanism (`web/src/components/application/tabs/detail-tabs.tsx:88-92`) is symmetric by construction and already unit-tested (`detail-tabs.test.tsx`), so this is a coverage gap, not a live regression risk.
- Fix: add one more assertion block in `detail-tabs.spec.ts` after the Definition-tab checks: click `getByRole("tab", { name: "Overview" })`, then assert `page.getByRole("group", { name: "Spec view" })` has count 0 and `Description` heading is visible again — proving the unmount is symmetric in both directions without a page reload.

### Missing tests

- **Client-side Definition→Overview unmount** (see Nit above) — `e2e/tests/detail-tabs.spec.ts`, scenario: click Overview tab after Definition is active, assert `.scalar-render`/`Spec view` group is gone and `Description` heading reappears, without a `page.goto` reload.
- No other acceptance criterion in the spec (§3.3, §3.4) or plan (Tasks 1–4) lacks a corresponding assertion — the read-only-lock spec covers all 5 numbered points in spec §3.3 (rendered-vs-raw distinguishing token, absent degrade banner, `.scalar-client`/`data-addressbar-action="send"` hidden, no accessible Send/Test button, console-error hygiene), and the tab-switch spec covers all 5 points in §3.4.

### What looks good

- `src/Kartova.Migrator/DevSeed.cs:217-268` — the raw-SQL column lists for `catalog_apis` and `catalog_api_specs` match the EF migrations column-for-column and in order (`20260703161759_AddApis.cs:16-29`, `20260707121905_AddApiSpec.cs:16-26`), including nullability (`spec_url` nullable, everything else required) — a common raw-SQL-seed failure mode (typo'd or reordered columns) that isn't present here.
- `src/Kartova.Migrator/DevSeed.cs:228` — `style` is written via `(short)Kartova.Catalog.Domain.ApiStyle.Rest`, not a magic literal, consistent with the existing `(short)Lifecycle.Deprecated` convention the plan called out (plan line 65-68) and with the project's "pinned enum" rule.
- `e2e/tests/spec-render-readonly.spec.ts:41-43` — the read-only-lock assertions correctly use `:visible`-scoped locators (`.scalar-client:visible`, `[data-addressbar-action="send"]:visible`) rather than bare `toHaveCount(0)`, which is the real Critical a per-task reviewer caught (`gate-findings.yaml:9-13`) and which this diff has already fixed — verified against `specRender.css:12-15`'s `display:none !important` mechanism, so the `:visible` filter is the only assertion shape that actually discriminates a regression.
- `web/src/features/catalog/pages/ApiDetailPage.tsx:52-107` and `web/src/components/application/tabs/detail-tabs.tsx` — every selector the new specs rely on (`role=heading` name `E2E Spec Render Fixture`, `role=tab` Overview/Dependencies/Definition, `role=region` name "Relationships" via `<section aria-label="Relationships">` in `RelationshipsSection.tsx:194`, `role=group` name "Spec view" in `ApiSpecSection.tsx:95`) was verified directly against current source, not guessed — all match.
- `web/src/features/catalog/components/spec/SpecRender.tsx:41` vs `e2e/tests/spec-render-readonly.spec.ts` — the degrade-banner text ("Couldn't render this spec — showing source.") was byte-compared (straight apostrophe, em-dash) between component and spec; they match exactly, avoiding a silent-typo false-negative.
- ADR alignment is clean: ADR-0114 tab order (Overview · Dependencies · Definition) matches `ApiDetailPage.tsx`; ADR-0112's `catalog_api_specs` text-storage 1:1 shape matches the seeded row; ADR-0111's unified-entity/`Style`-keyed model matches the `ApiStyle.Rest` seed; ADR-0097/ADR-0113 tier-5 placement is correct (no Domain/Application logic changed → gate 6 mutation N/A is properly justified, and no backend unit-test seam exists for `DevSeed`, consistent with the sunset-app-fixture precedent already in the file).
