# Resource protection — Per-principal fairness

Section of `knowledge/security/resource-protection.md`.


The mechanics behind the quota policy in `knowledge/security/api-surface.md`: global bounds
keep the service alive, per-principal bounds keep it fair. Expensive shared resources — worker
pools, job queues, export pipelines — enforce per-principal concurrency caps distinct from the
global cap, so one tenant at its limit queues behind itself, not in front of everyone else.
Where workloads differ by an order of magnitude, heavy principals are isolated structurally:
separate queues, weighted scheduling, or a dedicated pool. Admission control happens before
expensive work is dispatched — refusing at the door costs microseconds, refusing after
dispatch costs the work itself.

Verification tests: with principal A holding its full concurrency allowance, principal B's
request is admitted and completes within its normal latency budget — the two-principal
assertion from the rate-limit tests, applied at the work layer; principal A's excess submission
is the one refused.
