# Quality oracle — Performance budgets

Section of `oracles/quality-oracle.md`. Verdict grammar: `knowledge/shared/defense-in-depth.md`. Severities and waivers: `knowledge/shared/severity-tiers.md`.


| ID | Check | Pass criterion | Sev | Refs | Remediation |
|---|---|---|---|---|---|
| QUA-070 | No unbounded queries or N+1 | Loops in the diff issue no per-iteration network or database call where a batch API exists; queries against unbounded tables paginate or limit | S1 | ISO performance efficiency | knowledge/quality/review-method/performance-review.md |
| QUA-071 | Complexity on hot paths | A stated complexity bound (code comment or handoff note) accompanies each loop over unbounded input on request-handling or per-item processing paths in the diff | S2 | ISO performance efficiency | knowledge/quality/review-method/performance-review.md |
| QUA-072 | Bounded resources | Every cache, queue, or buffer introduced has a size bound or eviction policy | S1 | ISO performance efficiency (resource utilization) | knowledge/quality/review-method/performance-review.md |
| QUA-073 | Measured claims | Performance claims in the handoff are backed by a recorded measurement (benchmark or profile); unmeasured claims are removed or labeled expectations | S3 | ISO performance efficiency | knowledge/quality/review-method/performance-review.md |
| QUA-074 | Budget regression check | When the repository declares a performance budget for a path the diff touches, a benchmark or load-check result against the budget is recorded; n/a otherwise | S2 | ISO performance efficiency (time behaviour) | knowledge/quality/performance-capacity/budgets.md |
