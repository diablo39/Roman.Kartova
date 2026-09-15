# DoD Ledger — VM Linking (E-02.F-04.S-01 slice 2b)

**Slice:** VM linking — DeployedOn ({App,Service}→VM) + PartOf (Infrastructure→System) edges + graph wiring
**Spec:** `docs/superpowers/specs/2026-09-15-catalog-vm-linking-design.md`
**Plan:** `docs/superpowers/plans/2026-09-15-catalog-vm-linking.md` (gitignored scratch)
**SDD ledger:** `.superpowers/sdd/2026-09-15-catalog-vm-linking/progress.md`
**Branch:** `feat/catalog-vm-linking` (from `master` @ 53578e8)
**Terminal commit:** 454a5f8

## Status: implementation staged, verification partial (gates 1-8 green; terminal re-verify running; 9/10 pending)

## Summary table

| # | Gate | Status | Evidence |
|---|------|--------|----------|
| 1 | Full build, `TreatWarningsAsErrors` | ✅ green | `dotnet build Kartova.slnx -warnaserror` → 0 warnings / 0 errors on 9c7f344 |
| 2 | Per-task subagent reviews | ✅ green | 11 tasks each reviewed (spec+quality); T3, T4, final-fix each had one fix round, re-reviewed clean. See SDD ledger. |
| 3 | Full test suite (unit + arch + integration, real seam) | ✅ green | `dotnet test Kartova.slnx` on 9c7f344 → 0 failures across 15 assemblies; Catalog integration 472 (incl. new real-seam DeployedOn/PartOf/cross-tenant/409 tests, KartovaApiFixtureBase real Postgres+RLS+JWT). Log: `final-fullsuite.log` |
| 4 | Container build (`docker compose build`) | ✅ green | `docker compose build` exit 0 — migrator + web + api images built (web codegen fell back to committed snapshot with the infra/system path). Log: `gate4-compose-build.log` |
| 5 | `/simplify` on branch diff | ✅ green | 4 cleanup agents; 5 fixes applied (single infra lookup, DeployOnVmAction extract, ownership helper, memoized filters, using-aliases), commit c4bb4f5, behavior-preserving; build 0-warn, integ 5/5, unit 361/361, web 684/684, web build green. 5 items skipped as logged follow-ups. |
| 6 | `/superpowers:requesting-code-review` (whole-branch) | ✅ green | opus whole-branch review on 53578e8..e66d889 → 1 Important (infra PartOf POST 500→409) found, fixed (9c7f344), scoped re-review Approve; minors triaged as follow-ups |
| 7 | `/pr-review-toolkit:review-pr` | ✅ green | 5 agents (code/tests/silent-failures/type-design/comments). No Critical. Fixed C/D/E/F (commit e6259a1): cross-tenant DeployedOn-target test, ownership tests, EntityLookupResult.Type doc, 2 stale comments. 2 Important read-surface gaps (hierarchy, System-members UI) ruled deferred → TD-005/006; minors → TD-007/follow-ups. |
| 8 | `/deep-review` on branch diff | ✅ green | opus deep-review: 0 blocking, 1 should-fix + 2 missing-tests + 2 nits fixed (commit 454a5f8 — server-side hosted-components filter, graph-endpoint + non-owning-team-403 integration tests, icon/limit nits). Report: `deep-review.md`. |
| 9 | Visual / API verification (running system) | ⛔ blocked → pending user | Playwright + chrome-devtools MCP failed to connect this session. Needs browser drive of `/graph` (infra node + DeployedOn/PartOf edges render), VM detail (System membership + Hosted components), Deploy-on-VM dialog. **E2E-impact:** Deploy-on-VM action lives on the Dependencies tab — check `e2e/` specs that traverse detail tabs/relationships. |
| 10 | CI green on PR | ⏳ pending | needs push + PR (outward-facing — awaiting consent). Run `scripts/ci-local.sh` pre-push. |

## Rulings made (controller, during SDD)

1. **Wire shape** — POST /relationships is FLAT `{sourceKind,sourceId,type,targetKind,targetId}`, not the nested form in the plan snippets (confirmed against CreateRelationshipTests). Applied in Tasks 3/7. Cost if wrong: fast-failing tests.
2. **Codegen regen stays at Task 11** (revised an earlier fold-into-T6 ruling) — codegen.mjs fetches the live API; bringing the stack up mid-task is heavy. Task 6's gate is vitest (esbuild, ignores types); the one new untyped path typechecks after T11 regen; no gate runs tsc between T6 and T11. Task 11 used the live path successfully. Cost if wrong: tsc-red until T11 — did not occur.
3. **ListVms has no `displayNameContains`** (real backend gap) — VM entity-search shows first-N VMs unfiltered; accepted for this slice. Follow-up logged. Cost if wrong: poor UX at large VM counts.

## Follow-ups (out of scope, logged)

- Add `displayNameContains` to `ListVms` + pass it from `useEntitySearch` infra branch (E-02.F-04 list surface / tech-debt).
- VM "Hosted components" reads one unfiltered page then filters `deployedOn` client-side (VmDetailPage) — server-side `type=deployedOn` filter is the durable fix at scale (final-review minor 2).
- `relationships.ts` `limit: String(10)` hardcoded vs sibling param style (final-review minor 3).

## Deferred minors (from per-task reviews — none block merge)

See SDD ledger `minor (deferred)` lines: T6 hardcoded limit; T8 client-side filter; T9 gate-formula restatement + mocked dialog in page tests; T10 `data-kind` on all node roots.
