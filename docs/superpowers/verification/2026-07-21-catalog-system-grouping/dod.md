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
| 1 Build (`TreatWarningsAsErrors`) | ⏳ | ⏳ |
| 2 Per-task reviews | ⏳ | ⏳ |
| 3 Full suite (+ real-seam) | ⏳ | ⏳ |
| 4 Container build | ⏳ | ⏳ |
| 5 `/simplify` | ⏳ | ⏳ |
| 6 Mutation (blocking — Domain/App) | ⏳ | ⏳ |
| 7 requesting-code-review | ⏳ | ⏳ |
| 8 review-pr | ⏳ | ⏳ |
| 9 deep-review | ⏳ | ⏳ |
| Terminal re-verify | ⏳ | ⏳ |
| 10 Visual/API (ADR-0084) | ⏳ | ⏳ |
| 11 CI green | ⏳ | ⏳ |

Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A (+reason).

## Implementation progress (Arm A — dev-hm)

| Task | Implemented | Committed (sha) | Per-task review |
|------|-------------|-----------------|-----------------|
| 1 Domain: System + EntityKind | ⏳ | — | — |
| 2 RelationshipType.PartOf + rules | ⏳ | — | — |
| 3 Impact remediation (filter + hardening test) | ⏳ | — | — |
| 4 Persistence (EF + migration) | ⏳ | — | — |
| 5 Contracts | ⏳ | — | — |
| 6 Application | ⏳ | — | — |
| 7 SystemSortSpecs | ⏳ | — | — |
| 8 Handlers | ⏳ | — | — |
| 9 CatalogEntityLookup arm | ⏳ | — | — |
| 10 Permission 5-sync | ⏳ | — | — |
| 11 Endpoints + routes | ⏳ | — | — |
| 12 Integration (System endpoints) | ⏳ | — | — |
| 13 Integration (PartOf) | ⏳ | — | — |
| 14 Docs (ADR/registry/checklist) | ⏳ | — | — |

## Implementation progress (Arm B — control)

_(mirrors the same 14 tasks; populated when Arm B runs)_

## Status

**implementation staged, verification pending** — worktrees created, Arm A starting. No completion claims until the ten blocking gates are green on the merged arm.
