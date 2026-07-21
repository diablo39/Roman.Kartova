# DoD Ledger — Catalog System grouping (E-03.F-03.S-01) + dev-hm A/B experiment

**Slice:** `2026-07-21-catalog-system-grouping` · **Baseline:** `44fcb02` (spec `86fde45` + plan)
**Arms:** A = dev-hm agents (`armA/catalog-system-grouping`) · B = default flow (`armB/catalog-system-grouping`)
**Last updated:** 2026-07-21
**Spec:** `docs/superpowers/specs/2026-07-21-catalog-system-grouping-design.md`
**Plan:** `docs/superpowers/plans/2026-07-21-catalog-system-grouping.md`
**Findings telemetry:** `./gate-findings.yaml` (tagged `produced_by` × `found_by` for the 2×2) · **Comparison:** `./comparison.md`

> Two implementation arms build the SAME frozen plan in isolated worktrees. The experiment measures implementer quality (diff A vs diff B) and reviewer value (dev-hm reviewers vs default gates), cross-reviewed 2×2. Design/plan are single-authored on main — NOT part of the comparison.

## Per-arm gate status

| Gate | Arm A (dev-hm) | Arm B (control) |
|------|----------------|-----------------|
| 1 Build (`TreatWarningsAsErrors`) | ✅ (`3b3066b`, 0 warn) | ✅ (`0b4dbccb`, 0 warn) |
| 2 Per-task reviews | 🟢 2×2 cross-review running | 🟢 2×2 cross-review running |
| 3 Full suite (+ real-seam) | ✅ (unit 257 + new integ 32 / assembly 330) | ✅ (unit 256 + new integ 34) |
| 4 Container build | ✅ (CI #78) | — |
| 5 `/simplify` | ✅ clean | — |
| 6 Mutation (blocking — Domain/App) | ✅ 80.43% (≥80); RelationshipTypeRules 100%; boundary mutants killed; 6 String survivors policy-ignored + 1 low-value | — |
| 7 requesting-code-review | ✅ (2×2 + critique) | — |
| 8 review-pr | ✅ clean (0 blk/should) | — |
| 9 deep-review | ✅ conforms (1 doc should-fix, applied) | — |
| Terminal re-verify | ✅ (post-fix build+suite green; CI #78) | — |
| 10 Visual/API (ADR-0084) | ✅ live smoke PASS (`gate-10-live-smoke.md`) | — |
| 11 CI green | ✅ #78 all jobs green | — |

**Ship arm = A** → PR #78 (draft). Critique fixes + ADR acceptance applied. Only gate 6 (mutation, running) + gate 10 (live smoke) remain before ready-for-merge.

Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A (+reason).

## Implementation progress (Arm A — dev-hm)

| Task | Implemented | Committed (sha) | Per-task review |
|------|-------------|-----------------|-----------------|
| 1 Domain: CatalogSystem + EntityKind | ✅ | `703a64c` | pending (chunk review) |
| 2 RelationshipType.PartOf + rules | ✅ | `703a64c` | pending |
| 3 Impact remediation (filter + hardening test) | ✅ (src; integ run deferred) | `703a64c` | pending |
| 4 Persistence (EF + migration) | ✅ | `22ddb81` | pending |
| 5 Contracts | ✅ | `22ddb81` | pending |
| 6 Application | ✅ | `22ddb81` | pending |
| 7 SystemSortSpecs | ✅ | `22ddb81` | pending |
| 8 Handlers | ✅ | `22ddb81` | pending |
| 9 CatalogEntityLookup arm | ✅ | `3b23a33` | pending |
| 10 Permission 5-sync | ✅ | `3b23a33` | pending |
| 11 Endpoints + routes | ✅ | `3b23a33` | pending |
| 12 Integration (System endpoints) | ✅ | `3b3066b` | pending |
| 13 Integration (PartOf) | ✅ | `3b3066b` | pending |
| 14 Docs (ADR draft/registry/checklist) | ✅ (ADR pending human preview) | `3b3066b` | pending |

## Implementation progress (Arm B — control)

_(mirrors the same 14 tasks; populated when Arm B runs)_

## Status

**implementation staged, verification pending** — worktrees created, Arm A starting. No completion claims until the ten blocking gates are green on the merged arm.
