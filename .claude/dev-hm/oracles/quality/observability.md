# Quality oracle — Observability

Section of `oracles/quality-oracle.md`. Verdict grammar: `knowledge/shared/defense-in-depth.md`. Severities and waivers: `knowledge/shared/severity-tiers.md`.


| ID | Check | Pass criterion | Sev | Refs | Remediation |
|---|---|---|---|---|---|
| QUA-040 | Project logger used | Zero print/console.log/debug statements in new code; new logging calls go through the project logger | S2 | ISO maintainability | knowledge/quality/review-method/observability-review.md |
| QUA-041 | Timeouts on external calls | Every new outbound call (HTTP, DB, queue, RPC) sets an explicit timeout, and its failure path is logged with context | S1 | ISO reliability (fault tolerance) | knowledge/quality/reliability-resilience/timeouts.md |
| QUA-042 | Metrics and tracing conventions | New endpoints and background jobs are wired into the repository's existing metrics/tracing conventions; n/a when it has none | S3 | ISO maintainability | knowledge/quality/review-method/observability-review.md |
| QUA-043 | Context propagation | New service-to-service calls propagate the repository's correlation/trace context; n/a when it has no convention | S2 | ISO maintainability (analysability) | knowledge/quality/observability/propagation.md |
| QUA-044 | Failures logged at actionable level | New failure paths ending in an unhandled 5xx or dropped work log at error level with operation and correlation ID; degraded-but-coping paths log at warn | S2 | ISO maintainability (analysability) | knowledge/quality/observability/log-levels.md |
| QUA-045 | SLI wiring | When the repository declares SLIs/SLOs, new user-facing endpoints (endpoints serving end users or their clients, as opposed to internal-only or batch interfaces) register the latency and error metrics those SLIs read; n/a otherwise | S2 | ISO maintainability | knowledge/quality/observability/sli-slo.md |
