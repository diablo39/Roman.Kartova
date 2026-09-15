# DoD Ledger — VM Linking (E-02.F-04.S-01 slice 2b)

**Slice:** VM linking — DeployedOn ({App,Service}→VM) + PartOf (Infrastructure→System) edges + graph wiring
**Spec:** `docs/superpowers/specs/2026-09-15-catalog-vm-linking-design.md`
**Plan:** `docs/superpowers/plans/2026-09-15-catalog-vm-linking.md` (gitignored scratch)
**SDD ledger:** `.superpowers/sdd/2026-09-15-catalog-vm-linking/progress.md`
**Branch:** `feat/catalog-vm-linking` (from `master` @ 53578e8)
**Terminal commit:** 9c7f344

## Status: implementation staged, verification partial (gates 1/2/3/6 green; 4/5/7/8/9/10 pending)

## Summary table

| # | Gate | Status | Evidence |
|---|------|--------|----------|
| 1 | Full build, `TreatWarningsAsErrors` | ✅ green | `dotnet build Kartova.slnx -warnaserror` → 0 warnings / 0 errors on 9c7f344 |
| 2 | Per-task subagent reviews | ✅ green | 11 tasks each reviewed (spec+quality); T3, T4, final-fix each had one fix round, re-reviewed clean. See SDD ledger. |
| 3 | Full test suite (unit + arch + integration, real seam) | ✅ green | `dotnet test Kartova.slnx` on 9c7f344 → 0 failures across 15 assemblies; Catalog integration 472 (incl. new real-seam DeployedOn/PartOf/cross-tenant/409 tests, KartovaApiFixtureBase real Postgres+RLS+JWT). Log: `final-fullsuite.log` |
| 4 | Container build (`docker compose build`) | ⏳ partial/pending | API image rebuilt in Task 11 (`docker compose build api` for live codegen). Full compose build (incl. web image) not yet run. |
| 5 | `/simplify` on branch diff | ⏳ pending | not run |
| 6 | `/superpowers:requesting-code-review` (whole-branch) | ✅ green | opus whole-branch review on 53578e8..e66d889 → 1 Important (infra PartOf POST 500→409) found, fixed (9c7f344), scoped re-review Approve; minors triaged as follow-ups |
| 7 | `/pr-review-toolkit:review-pr` | ⏳ pending | not run (no-fold: distinct from gate 6) |
| 8 | `/deep-review` on branch diff | ⏳ pending | not run |
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
