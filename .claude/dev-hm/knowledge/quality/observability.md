# Observability

Operable code answers, at 3 a.m., what it did and why — without a debugger and without a
deploy. Logs, metrics, and traces are one design fed by the same events, not three retrofits:
the trace shows where a request went, the log says why the interesting hop did what it did, the
metric says how many users it happened to. OpenTelemetry is the vendor-neutral standard for
producing all three (one API and SDK per stack, the OTLP wire protocol, shared semantic
conventions), so instrument once and keep the backend swappable; a profiling signal is joining
the same standard (see `knowledge/quality/performance-capacity/profiling.md`). The per-diff
checks QUA-040 and QUA-042 are walked in
`knowledge/quality/review-method/observability-review.md`; QUA-041's method lives in
`knowledge/quality/reliability-resilience/timeouts.md`; QUA-043 – QUA-045 point here. What
telemetry may contain is the security oracle's territory: no secrets, no personal data in any
signal (SEC-021/081/082, `knowledge/security/data-classification.md`).

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| Structured logs | `knowledge/quality/observability/structured-logs.md` |
| Log levels | `knowledge/quality/observability/log-levels.md` |
| Metrics | `knowledge/quality/observability/metrics.md` |
| Traces | `knowledge/quality/observability/traces.md` |
| Propagation | `knowledge/quality/observability/propagation.md` |
| SLI-SLO | `knowledge/quality/observability/sli-slo.md` |
| The 3 a.m. test | `knowledge/quality/observability/the-3-am-test.md` |
