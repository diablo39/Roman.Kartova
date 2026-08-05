# Quality oracle — Error handling

Section of `oracles/quality-oracle.md`. Verdict grammar: `knowledge/shared/defense-in-depth.md`. Severities and waivers: `knowledge/shared/severity-tiers.md`.


| ID | Check | Pass criterion | Sev | Refs | Remediation |
|---|---|---|---|---|---|
| QUA-030 | No swallowed errors | Every catch/except/error-check in the diff either retries, falls back, or propagates with context; logging alone does not count as handling; zero empty catch blocks | S1 | ISO reliability (fault tolerance) | knowledge/quality/review-method/error-handling-review.md |
| QUA-031 | Cause preserved | Errors crossing module boundaries are wrapped with context; zero rethrows that discard the original cause or stack | S2 | ISO reliability | knowledge/quality/review-method/error-handling-review.md |
| QUA-032 | Error returns handled | Every error or status return value produced in the diff is handled or explicitly discarded with a stated reason | S1 | ISO reliability | knowledge/quality/review-method/error-handling-review.md |
| QUA-033 | Preconditions fail fast | New public API functions check arguments at entry; errors raised for invalid arguments are typed or subclassed, not a bare Exception/Error | S2 | ISO reliability | knowledge/quality/review-method/error-handling-review.md |
| QUA-034 | Client and server errors distinguished | New request handlers return client-error status (4xx) for validation and authorization failures and server-error status (5xx) for internal faults; zero endpoints returning success statuses with an error payload | S2 | ISO functional suitability (functional correctness), compatibility (interoperability) | knowledge/quality/review-method/error-handling-review.md |
