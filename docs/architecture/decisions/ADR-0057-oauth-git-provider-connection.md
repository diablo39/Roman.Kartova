# ADR-0057: OAuth-Based Git Provider Connection (GitHub & Azure DevOps)

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Scan / Import Architecture / Integrations
**Related:** ADR-0035 (git first-class), ADR-0054 (deep scan), ADR-0055 (rate limits), ADR-0078 (no secrets, pending)

## Context

To perform repository scans (ADR-0054), Kartova must connect to tenants' Git providers. The authentication mechanism has significant implications: permissions granularity, revocation story, per-user vs per-org semantics, webhook installation, and operational security. GitHub and Azure DevOps are the two Git providers in MVP scope (PRD §4.3.1).

## Decision

Kartova connects to Git providers via **OAuth authorization flows**:

- **GitHub**: OAuth App with org-scoped authorization; scopes limited to `repo:read`, `read:org`, and webhook admin for connected repos.
- **Azure DevOps**: OAuth 2.0 flow against Azure DevOps; scopes `vso.code`, `vso.identity`, `vso.serviceendpoint_manage` (minimum viable).
- On successful authorization, Kartova installs webhooks/service hooks for push and repo events so that re-scans are event-driven rather than purely polled.
- Tokens stored encrypted at rest; refresh tokens rotated; per-tenant token isolation.
- Tenant admins can revoke the connection from Kartova UI or from the provider side; revocation is detected and handled gracefully.

## Rationale

- OAuth is the user-familiar, revocable, delegated-auth pattern across both providers.
- Installing webhooks on connect enables real-time updates without burning scan quota on polling.
- Per-tenant tokens align with tenant isolation (ADR-0011, ADR-0014) and minimize blast radius on compromise.
- Limited scopes reduce the damage of any token leak and make security review easier for customers.

## Alternatives Considered

- **GitHub App** — more granular permissions model and higher rate limits, and *is* a likely future upgrade; deferred because MVP needs both GitHub and Azure DevOps with a uniform pattern, and GitHub App requires additional install UX that slows launch.
- **Azure DevOps PAT** — simple but per-user, expires, manually rotated; poor tenant-admin UX.
- **Deploy keys / SSH** — works for clone only; no API access, no webhooks, no metadata.
- **Read-only service account token** — similar downsides to PAT; not scalable across many repos.

## Consequences

**Positive:**
- Familiar, revocable auth model — tenant admins retain full control.
- Webhook install on connect gives near-real-time update without polling waste.
- Same UX across GitHub and Azure DevOps.

**Negative / Trade-offs:**
- GitHub OAuth apps have lower rate limits than GitHub Apps — we lean on ADR-0055's rate-limit awareness.
- Two provider integrations mean two distinct OAuth review/registration paths.
- Token rotation and refresh failure handling must be robust; a silent expiry stops scans.

**Neutral:**
- GitHub App upgrade remains open as a future enhancement without breaking the model.

## References

- PRD §4.3.1
- Phase 2: E-07.F-01.S-01, E-07.F-02.S-01
- Related ADRs: ADR-0011, ADR-0014, ADR-0035, ADR-0054, ADR-0055
