# Performance and capacity — Budgets

Section of `knowledge/quality/performance-capacity.md`.


The control: performance expectations are written numbers living in the repository, not
recollections living in someone's head. A budget names a path, a metric, and a limit:

| Budget type | Example shape |
|---|---|
| Latency | p95 and p99 per endpoint or operation — percentiles, because averages hide the tail |
| Throughput | sustained requests or messages per second per instance at the latency budget |
| Resources | memory ceiling per instance, allocation per request, connection count |
| Startup | time to ready — matters for autoscaling and deploy speed |
| Payload / frontend | response size caps; for web UIs, the web-vitals metric set with its published thresholds |

Two kinds of numbers, kept distinct: target budgets derive from user need and the SLO (set the
budget tighter than the SLO so normal variance doesn't eat the error budget —
`knowledge/quality/observability.md#sli-slo`); regression baselines derive from current measured
behavior and exist to catch accidental slowdowns. Both are legitimate; confusing them ratchets
yesterday's accident into tomorrow's requirement.

Budgets are executable or they are decoration: encode them where a runner can fail on them —
load-tool thresholds (k6 exits non-zero when a threshold like a p95 bound is breached),
benchmark assertions against a stored baseline, frontend budget files consumed by the CI
auditor. A budget in a wiki gates nothing.

Budget regression (QUA-074): when a declared budget covers a path a diff touches, the
benchmark or load check runs and the measured value is recorded against the budget in the
handoff. Loosening a budget is its own reviewed change with a stated reason — never slipped into
the same diff that would breach it, exactly as lint configuration is handled in
`knowledge/quality/review-method.md#lint-and-type-gates`.

Verification: the budget exists as a checked-in executable check; the check runs on the paths it
names in CI; the handoff cites the measured number and the budget it was compared against.
