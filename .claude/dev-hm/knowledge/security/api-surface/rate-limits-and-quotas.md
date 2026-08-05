# API surface protection — Rate limits and quotas

Section of `knowledge/security/api-surface.md`.


Every authenticated caller has a request budget keyed to the principal — client id, user, or
tenant — not to source IP alone, which shared egress defeats in both directions. Exceeding the
budget returns 429 with `Retry-After`. The enforcing layer may be the gateway or middleware; the
handoff names which, and configuration counts only when cited. Two budget classes:

- Baseline per-principal request rate on the whole surface — protects capacity.
- Tight budgets on sensitive flows (API6): login, verification-code, password-reset, export,
  send, and purchase endpoints get their own low limits and, where the operation warrants it,
  step-up verification (`knowledge/security/authentication-sessions.md`). A flow that is cheap
  per call but harmful in volume needs a volume control, not a faster server.

Expensive asynchronous work (report generation, bulk export) gets a quota — concurrent jobs and
per-period totals per principal — so the queue cannot be filled by one caller
(`knowledge/security/resource-protection.md` covers the fairness mechanics).

Verification tests: a burst one request over the limit observes 429 with `Retry-After`; after
principal A exhausts its budget, a request from principal B still succeeds — the second
assertion is the one that catches accidental global keying.
