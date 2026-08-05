# Performance and capacity

How we make performance a stated, enforced property instead of an impression. ISO/IEC
25010:2023 splits performance efficiency into time behaviour, resource utilization, and
capacity; this file covers the controls for all three — declared budgets, honest measurement,
load and soak checks before release, and explicit capacity limits — and how each is verified.
The per-diff deterministic checks (QUA-070 – QUA-073: no N+1, complexity bounds, bounded
buffers, measured claims) are walked in
`knowledge/quality/review-method/performance-review.md`; oracle entry QUA-074 points here.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| Budgets | `knowledge/quality/performance-capacity/budgets.md` |
| Benchmarks | `knowledge/quality/performance-capacity/benchmarks.md` |
| Profiling | `knowledge/quality/performance-capacity/profiling.md` |
| Load and soak | `knowledge/quality/performance-capacity/load-and-soak.md` |
| Capacity | `knowledge/quality/performance-capacity/capacity.md` |
