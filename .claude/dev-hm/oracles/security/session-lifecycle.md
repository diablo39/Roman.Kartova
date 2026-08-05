# Security oracle — Session and authorization lifecycle

Section of `oracles/security-oracle.md`. Verdict grammar and severity tiers come from your own prompt.


| ID | Check | Pass criterion | Sev | Refs | Remediation |
|---|---|---|---|---|---|
| SEC-160 | Server-side revocation | New long-lived credentials the diff introduces (refresh tokens, API keys, device tokens) have a server-side revocation path (store lookup, denylist, versioned invalidation); zero bearer credentials that cannot be revoked before natural expiry | S1 | A07 · CWE-613 · V7 | knowledge/security/authentication-sessions/revocation.md |
| SEC-161 | Step-up where warranted | Operations the work package classifies as high-risk (Tier-3 export, credential change, destructive admin action) verify a recent authentication event or second factor, or the handoff names the platform layer providing it; n/a when none are classified | S2 | A07 · CWE-306 · V6 | knowledge/security/authentication-sessions/step-up.md |
| SEC-162 | Deny-by-default wiring | New routers, controllers, and message consumers register under the project's authentication/authorization middleware by default; each opt-out is explicit in code and declared in the handoff with a reason | S1 | A01 · CWE-862 · V8 | knowledge/security/authorization-design/deny-by-default.md |
| SEC-163 | Tenant isolation | In multi-tenant code, every new query or lookup against tenant-owned data carries the tenant predicate from the authenticated context, or the store enforces it (row-level security, scoped connections); zero queries where the tenant comes from the request payload alone | S0 | A01 · CWE-639 · V8 · API1 | knowledge/security/authorization-design/tenant-isolation.md |
