# Quality oracle — Reliability and resilience

Section of `oracles/quality-oracle.md`. Verdict grammar and severity tiers come from your own prompt.


| ID | Check | Pass criterion | Sev | Refs | Remediation |
|---|---|---|---|---|---|
| QUA-080 | Bounded idempotent retries | New retry logic retries only idempotent operations or uses idempotency keys, with backoff and a maximum attempt count; all three visible in code or config | S1 | ISO reliability (fault tolerance) | knowledge/quality/reliability-resilience/retries.md |
| QUA-081 | Failure mode stated and tested | Every new hard dependency on an external service (one called synchronously on a request or startup path with no stated fallback) states its failure behavior (fail fast, fallback, queue-and-retry) at the call site or in config, and a test exercises the dependency-unavailable path | S1 | ISO reliability (fault tolerance) · P1 | knowledge/quality/reliability-resilience/failure-modes.md |
| QUA-082 | Resilience conventions joined | When the repository has a circuit-breaker or bulkhead convention, new outbound integrations join it; n/a when it has none | S2 | ISO reliability (fault tolerance) | knowledge/quality/reliability-resilience/breakers.md |
