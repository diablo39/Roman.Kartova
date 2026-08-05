# Resource protection — Load shedding

Section of `knowledge/security/resource-protection.md`.


When the service is saturated despite the bounds above, it sheds load deliberately instead of
degrading for everyone: excess requests receive a fast 429 or 503 with `Retry-After`, because a
quick refusal preserves capacity and lets well-behaved clients back off, while a slow failure
consumes the very resource under pressure. Shedding happens at admission — the cheapest point —
and follows a declared order: optional and expensive features shed first, core state-changing
paths last, and liveness/readiness endpoints stay cheap so the platform can still tell the
difference between overloaded and dead. Breakers that shed toward a failing dependency are
resilience territory (`knowledge/quality/reliability-resilience.md`).

Verification tests: under synthetic overload in a load-test bench, accepted requests keep their
latency budget while the excess receives fast refusals — assert both halves, since either alone
can be faked by doing nothing; the declared shed order has a test or configuration snapshot.

Every bound above is a protective control in the sense of
`knowledge/security/control-verification-tests.md`: its test must fail when the bound is
removed, and refusals emit their security event — the `excess_` family in
`knowledge/security/security-logging-detection.md#event-codes`.
