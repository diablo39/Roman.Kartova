# Quality characteristics in review (ISO/IEC 25010:2023) — Maintainability thresholds

Section of `knowledge/quality/quality-characteristics.md`.


Deterministic limits backing QUA-021 – QUA-024 and QUA-026. The linter enforces what it can
(QUA-025); these are the review-checked residue:

| Threshold | Oracle | Rationale |
|---|---|---|
| No duplicated block of 10+ lines within the repo | QUA-021 | Duplication forks bug fixes; extraction is cheap at review time |
| Function ≤ 60 lines and cyclomatic complexity ≤ 10, or a justifying comment | QUA-022 | Analysability: beyond this, correctness review degrades measurably |
| No commented-out code; no unused imports/symbols | QUA-023 | Dead code misleads readers and hides in searches |
| Repeated numeric literals (beyond -1, 0, 1, 2) become named constants | QUA-024 | The name carries the why; the literal does not |
| No new import/package cycles | QUA-026 | Cycles make units untestable in isolation and freeze module boundaries |

The escape hatch is deliberate: a justified exception (inline comment or handoff note) converts
a mechanical fail into a reviewed decision — the goal is legibility of decisions, not uniform
smallness. Duplication has one legitimate exception: test code may repeat setup for readability
when extraction would hide the scenario.
