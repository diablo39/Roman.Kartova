# Deep Review — E2E Test Infrastructure (E-01.F-02.S-03)

**Target:** `feat/e2e-test-infrastructure` vs master (9e0195c..8258a49) · **Reviewer:** opus, spec/plan/ADR cross-reference · **Date:** 2026-07-09
**Counts:** Blocking 0 · Should-fix 3 · Nits 2 · Missing-tests 2 · Good 5

## Overview
Stands up the repo's first checked-in Playwright E2E suite (3 journeys, compose-orchestrated against the real rootless `web` container), plus a backend `HasQueryFilter` hardening that excludes unmappable `Relationship.type` rows from all reads, realm/CORS wiring for :4173, a DevSeed fixture app, a nightly `e2e.yml`, and the gate-10 retarget + ADR-0113. Health is good: the backend change is additive and tested, the E2E→separate-workflow deviation is documented in the ADR, and the DoD ledger is honest about partial verification. Findings are documentation-completeness and test-breadth gaps, not correctness or merge-breakers.

## Blocking
None.

## Should-fix  (all RESOLVED)
1. **Seam test proves 1 of 3 read paths the spec claims.** `RelationshipTypeHardeningTests.cs` exercises only the list endpoint; the "list / graph / api-surface" claim in spec §5 / ADR was asserted, not tested. **Resolved:** ADR-0113 narrowed to "list proven at the seam; graph/api-surface inherit the entity-level filter"; dedicated seam tests logged as a low-value follow-up.
2. **CHECKLIST marked the story `[x]` while DoD said partial.** **Resolved:** CHECKLIST annotated with true gate status (1–5,7–10 green; 6 waived; 11 pending push).
3. **ADR-0113 omitted the load-bearing :4173 realm + CORS wiring.** **Resolved:** added a "Realm + CORS wiring for the :4173 origin" subsection to the Decision.

## Nits
1. dod.md/gate-findings `head` pinned `3f08131` while HEAD advanced (`b66e2f6`, `8258a49`). **Resolved:** head bumped to `8258a49`.
2. Spec §2 + Plan Task 10 carry pre-correction framing (bug #47 active; add e2e to ci.yml). **Left as historical** point-in-time records (prior-slice practice); authoritative ADR + shipped code corrected.

## Missing tests
1. Graph-traversal + api-surface drift exclusion at the seam — two `[TestMethod]`s in `RelationshipTypeHardeningTests.cs` (global filter covers them mechanically; follow-up).
2. Gate 6 (mutation) owed + unrun — **owner-WAIVED** this slice; the `Contains` predicate's mutations are killed by the existing list-path integration test + the E2E drift tripwire (argued in dod.md gate 6).

## What looks good
- CORS append correct + minimally invasive: appsettings holds index 0 (5173), `Cors__AllowedOrigins__1` adds 4173 without clobbering `CorsTests` (`docker-compose.yml`).
- Realm pinning claim accurate: `admin@orga` → `601eecd8-…`, `team-admin@orga` already pinned — matches ADR's "only admin + team-admin pinned today."
- Gate-8 strengthened drift assertion is sound: `RelationshipsSection.tsx:179,184` renders the "Outgoing" heading + "No outgoing relationships." empty-state; `ApplicationDetailPage.tsx:148` mounts the non-readOnly variant — so the spec's heading + empty-state checks catch the "unknown type leaks into UI without 500" class.
- Query filter placement clean: `Enum.GetValues<RelationshipType>()` (self-maintaining) at entity-config scope, composes with RLS; also hardens E2E isolation (a leaked drift row can't 500 a later spec).
- `e2e.yml` vs `ci.yml` split is a documented deviation (ADR-0113 CI-cadence section), not silent.
