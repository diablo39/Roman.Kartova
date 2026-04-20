# ADR-0009: Service Account JWT Model for CI/CD

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Authentication & Authorization
**Related:** ADR-0007 (JWT), ADR-0008 (RBAC roles)

## Context

CI/CD pipelines, scripts, and the CLI need non-interactive access to the platform API (PRD §5.2). Using the same JWT infrastructure as human users keeps auth code unified. Service accounts must be scoped, revocable, and auditable.

## Decision

Service accounts are first-class principals in KeyCloak that authenticate via the CLI command `kartova auth --token <token>`. The token is exchanged for a JWT access token and stored locally (e.g., under `$HOME/.kartova/credentials`). The JWT carries the same claim shape as user tokens with the `service_account` role.

## Rationale

- Reuses the KeyCloak/JWT infrastructure from ADR-0006/ADR-0007 — no second auth path.
- Tokens are revocable by disabling the service account in KeyCloak.
- Audit trail is uniform with user actions.

## Alternatives Considered

- **OIDC client-credentials flow** — viable long-term; deferred because it needs a client-secret management story the CLI doesn't yet have. May supersede this decision in v2.
- **Workload identity federation (GitHub OIDC, Azure AD)** — preferred long-term for CI, added post-MVP.
- **Static API keys** — no expiry, poor revocation story.
- **Device flow** — interactive, unsuitable for CI.

## Consequences

**Positive:**
- Single auth model across human and machine principals
- Works in any CI provider (GitHub Actions, Azure Pipelines, GitLab CI, Jenkins)

**Negative / Trade-offs:**
- Long-lived bearer tokens require careful local-storage discipline
- Rotation is a manual process until workload-identity federation is added
- CLI must handle refresh and token expiry cleanly

**Neutral:**
- Service accounts are excluded from per-user billing (ADR-0063)

## References

- PRD §5.2
- Phase 5: E-13.F-01.S-02
