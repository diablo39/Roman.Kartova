# ADR-0078: No Secrets or Credentials Stored — References Only

**Status:** Accepted
**Date:** 2026-04-20
**Deciders:** Roman Głogowski (solo developer)
**Category:** Non-Functional / Security
**Related:** ADR-0054 (deep repository scan), ADR-0077 (encryption at rest & in transit, pending), ADR-0015 (GDPR), ADR-0018 (audit log), ADR-0057 (OAuth-based Git connection)

## Context

As Kartova scans repositories, ingests documentation, and catalogs infrastructure (ADR-0054), it unavoidably encounters secret-shaped things: environment-variable *names* (`DATABASE_URL`, `STRIPE_API_KEY`), connection-string *templates*, vault references, `.env.example` files. Storing actual secret *values* — even encrypted — makes the catalog itself a high-value target, dramatically expanding blast radius on any compromise. The entire security posture of a catalog rests on the premise that it is not a secrets store (PRD §7.3).

## Decision

Kartova **never stores secret or credential values**. The catalog stores only **references, names, and structural metadata** — enough to describe *that* a dependency exists without containing the secret itself.

**What IS stored:**
- Environment variable **names** (`DATABASE_URL`, `REDIS_HOST`) without values.
- Connection-string **templates** with placeholders (`postgres://{user}:{pass}@{host}/{db}`) — never filled in.
- Vault/secret-manager **references** (e.g., `vault://kv/prod/api-key` or `azurekv://my-vault/my-secret`) — the pointer, never the resolved value.
- Secret **metadata** — rotation dates, owning team, classification — where voluntarily supplied by the tenant.

**What is NEVER stored:**
- Secret values (passwords, API keys, tokens, private keys, client secrets).
- `.env` files with actual values (only `.env.example` or templates).
- Resolved/dereferenced vault contents.
- OAuth tokens from integrations are *retained only as required* for integration function (ADR-0057), stored encrypted, and explicitly scoped — not exposed to catalog consumers.

**Scanner behavior:**
- Deep scanners (ADR-0054) detect secret-shaped values via entropy/regex rules and **refuse to ingest them** — the scan result records "secret present, redacted" not the value.
- If a secret value slips through (false negative), it is purged on detection, with an audit-log entry (ADR-0018).

**UI/API behavior:**
- No endpoint exposes secret values (even to admins). Fields that cannot structurally contain a value are rendered as reference chips.
- Customer-authored free-form fields (descriptions, notes) carry a "do not paste secrets here" warning + optional automated scanning.

## Rationale

- Minimizes blast radius: a Kartova compromise cannot directly leak customer secrets.
- Simplifies compliance posture — Kartova is never in scope for PCI-DSS secret handling, SOC 2 CC6.1 key storage controls beyond its own operational secrets, etc.
- Aligns with modern secret-management best practice: a dev portal is the wrong place for secrets; vaults are the right place.
- Insurance and enterprise procurement reviews repeatedly flag "do you store customer secrets?" — a firm "no" unlocks deals.
- Removes an entire class of "catalog compromised → downstream systems compromised" attack chain.

## Alternatives Considered

- **Encrypted value storage with per-tenant KMS keys** — moves the risk, doesn't eliminate it; still a high-value target; compliance burden explodes (HSM, key rotation, BYOK).
- **Pass-through vault integration (read-through to Vault/AKV)** — useful future feature but not MVP; still requires never caching resolved values.
- **Metadata + external pointer only (what we chose), plus opt-in pass-through** — adopted as the long-term posture; pass-through deferred to v2.
- **Store everything, rely on encryption** — category-defining failure mode.

## Consequences

**Positive:**
- Radical simplification of the security threat model.
- Easier enterprise procurement / security reviews.
- Scanner false-positives are the worst-case outcome, not data breaches.
- No in-scope secret-handling compliance obligations beyond Kartova's own operational secrets.

**Negative / Trade-offs:**
- Some integrations (Git providers, cloud APIs) legitimately need credentials to function — these are handled via OAuth flows (ADR-0057) or scoped service credentials, stored encrypted, never exposed.
- Users who expect a "secrets inventory" feature must be educated — Kartova indexes secret *references and usage*, not secret values.
- Scanner must be kept current with secret-detection patterns (shared with tools like TruffleHog, gitleaks).

**Neutral:**
- An eventual v2 "vault pass-through" feature can be added without changing this ADR — it would always resolve on-demand and never persist values.

## References

- PRD §7.3 (Security)
- Phase 2: E-08.F-01.S-06 (env-var name ingestion), E-08.F-01.S-07 (secret-reference detection)
- Related ADRs: ADR-0054 (deep scan), ADR-0077 pending (encryption), ADR-0057 (OAuth Git connection), ADR-0018 (audit log), ADR-0015 (GDPR)
