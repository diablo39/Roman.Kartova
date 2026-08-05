# Resource protection

The availability controls our code provides: every unit of work an external request can trigger
is bounded before the work starts, so one caller — hostile or merely misconfigured — cannot make
the service slow or unavailable for everyone else (CWE-400, CWE-770; API4 of the OWASP API
Security Top 10, edition per `knowledge/shared/versions.md`). Surface-level budgets — pagination
caps, per-principal rate limits, async-job quotas — live in `knowledge/security/api-surface.md`;
this file covers the bounds inside the service and the fairness mechanics that file points to.
Retries, circuit breakers, and failure-mode design are the resilience half of the same coin:
`knowledge/quality/reliability-resilience.md`.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| Resource consumption | `knowledge/security/resource-protection/resource-consumption.md` |
| Input size limits | `knowledge/security/resource-protection/input-size-limits.md` |
| Parser and query bounds | `knowledge/security/resource-protection/parser-and-query-bounds.md` |
| Timeouts | `knowledge/security/resource-protection/timeouts.md` |
| Bounded queues and concurrency | `knowledge/security/resource-protection/bounded-queues-and-concurrency.md` |
| Per-principal fairness | `knowledge/security/resource-protection/per-principal-fairness.md` |
| Regex safety | `knowledge/security/resource-protection/regex-safety.md` |
| Load shedding | `knowledge/security/resource-protection/load-shedding.md` |
