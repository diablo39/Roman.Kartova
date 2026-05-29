# ADR-0100: Identity Scope — Strict One-Email-Per-Tenant in a Single KeyCloak Realm

**Status:** Accepted
**Date:** 2026-05-29
**Deciders:** Roman Głogowski (solo developer)
**Category:** Authentication & Authorization
**Related:** ADR-0006 (KeyCloak as IdP), ADR-0011 (one Org = one tenant)

## Context

Kartova runs a single KeyCloak realm (`kartova`) for all tenants; per-user `tenantId` attribute scopes membership. The realm setting `duplicateEmailsAllowed` defaults to `false`, which means a given email exists at most once in the realm — across all tenants. Slice 9's invitation flow needs a clear product decision on this.

## Decision

Keep the strict model: **one email = one tenant**. The realm setting `duplicateEmailsAllowed: false` is preserved. Cross-tenant duplicate invitations surface as a 409 `email-already-on-platform` with a soft message ("This email already has a Kartova account in another organization") — accepting that the existence of the user across tenants is leaked, consistent with Atlassian/GitHub behavior.

## Consequences

### Positive

- OIDC login is unambiguous (no org-picker).
- Invitation UX is simple.
- KeyCloak default preserved — no realm-config drift.

### Negative / trade-offs

- Users who genuinely need access to multiple Kartova organizations must use separate email addresses (e.g. `alice@company.com` + `alice+orgb@company.com`). Industry pattern; acceptable.

### Upgrade path

If multi-tenant-per-user ever becomes a real product requirement, the correct response is **realm-per-tenant** (full isolation), not `duplicateEmailsAllowed: true` (which breaks OIDC login). That would be a new ADR superseding this one.
