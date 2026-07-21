# dev-hm A/B (2×2) — results

**Baseline:** `44fcb02` · **Arm A:** dev-hm (`armA/…`) · **Arm B:** control (`armB/…`)

## Implementer comparison (diff A vs diff B, reviewer held constant)

| Metric | Arm A (dev-hm) | Arm B (control) |
|--------|----------------|-----------------|
| Prod LOC (excl. tests/gen/migrations) | — | — |
| Build warnings (first pass) | — | — |
| Full-suite pass (first pass) | — | — |
| Mutation survivors (changed files) | — | — |
| `/simplify` items | — | — |
| Deviations from plan | — | — |
| Fairness context supplied | CLAUDE.md conventions + spec + plan + permission 5-sync rule | same |

## Reviewer comparison (diff held constant, review stack varied) — the 2×2

| | dev-hm reviewers | default gates (7/8/9) |
|---|---|---|
| **on diff A** | — real / — delusion | — real / — delusion |
| **on diff B** | — real / — delusion | — real / — delusion |

- **Unique reals caught only by dev-hm:** —
- **Unique reals caught only by default gates:** —
- **False-positive (delusion) rate:** dev-hm — / gates —
- **Cost (tokens / wall-clock):** dev-hm — / gates —

## Adjudication protocol
Blind: judge each finding real|delusion in `gate-findings.yaml` WITHOUT its `produced_by`/`found_by` tags; fill tags after.

## Bottom line
_(n=1 qualitative — do the dev-hm agents add real defect-catch beyond existing gates, at acceptable cost? Filled at close.)_
