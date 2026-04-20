# ADR-0052: Custom Domains with Auto-Provisioned SSL for Status Page

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Status Page Architecture
**Related:** ADR-0023 (separate status cluster), ADR-0053 (99.99% SLA)

## Context

Customers expect status pages to appear under their own brand, e.g., `status.customer.com`, not `customer.status.kartova.io` (PRD §4.4.1). Branded domains require TLS certificates that must be auto-provisioned and auto-renewed at scale — 1000+ tenants, each potentially with a custom domain. Manual certificate operations are not viable.

## Decision

Each tenant may configure a custom domain for their status page. The platform:

1. Validates domain ownership via DNS (TXT/CNAME challenge).
2. Provisions a TLS certificate automatically via Let's Encrypt (ACME) — default path.
3. Auto-renews certificates ≥ 30 days before expiry.
4. Serves the status page through an ingress layer (cert-manager on Kubernetes) that supports SNI at scale.

The fallback domain `tenant.status.kartova.io` is always available with a platform-owned wildcard certificate.

## Rationale

- Branded status pages are a standard enterprise expectation.
- Let's Encrypt + cert-manager is well-trodden on Kubernetes and fits the cloud-agnostic posture (ADR-0022).
- Auto-renewal removes a common incident source (expired certs during an outage — exactly when the status page must work).
- Falls within the separate status-page cluster boundary (ADR-0023), so certificate-management failures don't affect the main platform.

## Alternatives Considered

- **Subdomain-only (`customer.status.kartova.io`)** — misses the branding requirement; blocks enterprise deals.
- **Customer-provided certificates** — higher operational burden on customers; manual renewal = outage risk.
- **Cloudflare for SaaS** — elegant, but ties us to Cloudflare; conflicts with cloud-agnostic strategy.
- **Commercial CA** — per-cert cost does not scale to 1000+ tenants; ACME/Let's Encrypt covers the need.

## Consequences

**Positive:**
- Enterprise-grade branding with zero manual cert ops.
- Auto-renewal aligns with the 99.99% status-page SLA (ADR-0053).
- Works across cloud providers.

**Negative / Trade-offs:**
- Let's Encrypt rate limits require careful batching and per-tenant throttles for new-cert issuance.
- ACME challenge requires DNS or HTTP control; customers must cooperate on the DNS validation step.
- SNI scaling considerations at very large tenant counts (hundreds of thousands of SNI entries) require monitoring.

**Neutral:**
- Customers with strict TLS policies can BYO certificate as an advanced option.

## References

- PRD §4.4.1
- Phase 4: E-12.F-01.S-02, E-12.F-01.S-03
- Related ADRs: ADR-0022, ADR-0023, ADR-0053
