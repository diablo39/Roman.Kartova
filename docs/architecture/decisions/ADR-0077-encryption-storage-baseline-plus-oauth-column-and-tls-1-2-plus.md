# ADR-0077: Encryption at Rest (Storage Baseline + OAuth Token Column Encryption) and TLS 1.2+ in Transit

**Status:** Accepted
**Date:** 2026-04-21
**Deciders:** Roman Głogowski (solo developer)
**Category:** Non-Functional / Cross-Cutting
**Related:** ADR-0001 (PostgreSQL), ADR-0002 (Elasticsearch), ADR-0003 (Kafka), ADR-0004 (MinIO), ADR-0012 (RLS tenant isolation), ADR-0015 (GDPR compliance), ADR-0016 (MiFID II compliance), ADR-0018 (audit log), ADR-0022 (K8s cloud-agnostic), ADR-0033 (HMAC webhooks), ADR-0042 (agent HTTPS polling), ADR-0057 (OAuth Git provider), ADR-0078 (no secrets stored)

## Context

Kartova needs a defensible data-protection posture for compliance (GDPR per ADR-0015, MiFID II per ADR-0016, SOC 2 Type II for enterprise sales) and for basic secure-by-default engineering hygiene. The original PRD §7.3 blanketly stated "All data encrypted at rest and in transit; mTLS for agents" — this was both under-specified and over-reaching:

- **Over-reaching on at-rest scope** — encrypting every field at the application layer imposes huge complexity on search, indexing, EF Core value conversion, and key management, without proportional security benefit. Work emails, IP addresses, and similar "pseudo-PII" are already effectively public (email signatures, LinkedIn, git commits) and do not warrant column-level encryption.
- **Wrong on mTLS for agents** — ADR-0042 already rejected mTLS in favor of TLS + bearer tokens for operational simplicity and proxy compatibility.

Kartova's real secrets are narrower than initially assumed:
- Credit card data → never stored locally, goes to Stripe (ADR-0062)
- Infrastructure secrets (connection strings, env var values) → never stored per ADR-0078; only names/presence are tracked
- Webhook secret hashes, agent token hashes → already Argon2id-hashed (ADRs 0033/0042); column encryption over a hash adds no security
- **OAuth access/refresh tokens for Git provider integrations (ADR-0057)** — GitHub and Azure DevOps tokens **are** real secrets; a leak would grant attackers read access to private tenant repositories

The realistic threat model for Kartova data at rest is:
1. Lost disk / stolen backup → storage-level encryption defeats this
2. Compromised database access (attacker with read access to PostgreSQL) → RLS (ADR-0012) blocks cross-tenant reads; audit log (ADR-0018) detects abuse; storage-level encryption does not help here
3. Compromised OAuth token → would expose tenant's source code; requires application-level encryption to mitigate

A narrow, layered approach matches the threat model without over-engineering.

## Decision

### Encryption at Rest — Layered

**Layer 1 — Storage-level encryption (baseline, mandatory, applies to all data):**

All persistent volumes and object stores use encryption at rest provided by the Kubernetes storage layer / object storage service:
- **PostgreSQL** PersistentVolumes encrypted (cloud-native — EBS encryption, Azure Disk encryption, GCS PV encryption, per deployment target)
- **Elasticsearch** PersistentVolumes encrypted (same pattern)
- **MinIO** (ADR-0004) configured with server-side encryption using KMS-managed keys where available, otherwise SSE-S3
- **Kafka** (ADR-0003) log volumes encrypted
- **Backups and snapshots** encrypted (same storage layer)

Zero application code. Configuration in Helm values / Terraform. Keys managed by the cloud provider or K8s CSI driver.

**Layer 2 — Application-level encryption for Git provider OAuth tokens (narrow scope):**

GitHub and Azure DevOps OAuth access tokens and refresh tokens are encrypted at the application level before storage:
- Algorithm: **AES-256-GCM**
- Storage: `git_provider_connections` table, columns `access_token_ciphertext`, `refresh_token_ciphertext`, plus `dek_id` reference
- Per-tenant Data Encryption Key (DEK), wrapped by a platform master key

```sql
CREATE TABLE tenant_encryption_keys (
  tenant_id UUID PRIMARY KEY,
  dek_wrapped BYTEA NOT NULL,
  master_key_version INT NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  rotated_at TIMESTAMPTZ
);

CREATE TABLE git_provider_connections (
  id UUID PRIMARY KEY,
  tenant_id UUID NOT NULL,
  provider TEXT NOT NULL,
  access_token_ciphertext BYTEA NOT NULL,
  access_token_nonce BYTEA NOT NULL,
  refresh_token_ciphertext BYTEA,
  refresh_token_nonce BYTEA,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
```

**Key management hierarchy:**
- **Platform master key** — AES-256, stored in Kubernetes Secret, K8s etcd encryption enabled (cloud-native). Rotated manually by ops team (yearly target). Master key version tracked for re-wrapping.
- **Per-tenant DEK** — AES-256, generated at tenant creation, wrapped by master key, stored in `tenant_encryption_keys`, unwrapped in memory on tenant context switch, never logged.
- **DEK rotation** — on-demand per tenant via admin job; re-encrypts affected rows under new DEK.

### Encryption at Rest — NOT Applied

The following fields intentionally **do not** use application-level encryption; they rely on storage-level encryption (Layer 1) plus RLS (ADR-0012), audit logging (ADR-0018), and access controls:

- `users.email`, `tenants.billing_email` — work emails, effectively public (email signatures, LinkedIn, git commits); GDPR PII classification does not mandate encryption
- IP addresses in audit logs (`audit_logs.actor_ip`) and agent tokens (`agent_tokens.last_used_ip`)
- Webhook endpoint URLs (validation at creation prohibits embedding tokens in query strings — documented in integration guide)
- Entity metadata (names, descriptions, tags, custom_attributes) — not secret
- Scorecard rules, maturity data, Risk/DX Scores — not secret
- Secret/connection-string *names* detected by deep scan (ADR-0054) — by policy, values are never stored (ADR-0078)

### Encryption in Transit

**TLS 1.2+ mandatory everywhere.** TLS 1.3 preferred where available.

**External (internet-facing):**
- Kartova API — TLS 1.2+ with valid public certificate (Let's Encrypt / cloud-issued)
- Status page — TLS 1.2+ with auto-provisioned custom-domain certificate (ADR-0052)
- Webhook outbound (ADR-0033) — HTTPS mandatory; plain HTTP rejected at subscription creation
- Agent polling (ADR-0042) — HTTPS mandatory
- Git provider connections (OAuth flow, API calls, webhook receipt) — HTTPS mandatory
- KeyCloak — HTTPS mandatory

**Internal (in-cluster between components):**
- Kartova API ↔ PostgreSQL — TLS via Npgsql `SSL Mode=Require`
- Kartova API ↔ Elasticsearch — TLS (NEST client configured with CA cert)
- Kartova API ↔ Kafka — SASL_SSL (SCRAM-SHA-512 + TLS)
- Kartova API ↔ MinIO — HTTPS
- Kartova API ↔ KeyCloak — HTTPS
- Certificate management — cert-manager operator in K8s with an internal CA

**mTLS explicitly NOT used:**
- Not for in-cluster component communication (K8s NetworkPolicy provides network-level isolation; mTLS adds PKI operational burden with marginal security benefit inside a trusted cluster)
- Not for agents (ADR-0042 chose TLS + bearer tokens)
- Not for webhook subscribers (ADR-0033 chose HMAC signing)

## Rationale

- **Threat-model-driven** — encryption strategy targets real risks (lost disk, OAuth token compromise) rather than blanket coverage that would inflate complexity without matching threat
- **Storage-level baseline is free** — cloud-native encryption for K8s volumes is a configuration flag; zero code and zero ongoing operational cost
- **Narrow application-level scope** — only Git provider OAuth tokens get column-level encryption; this matches where the actual secret lives and limits key-management complexity
- **Work email encryption not justified** — if a work email leaks, nothing catastrophic happens; such addresses are already publicly discoverable. Encrypting them adds indexing/search complexity for near-zero threat-model benefit.
- **No BYOK/HYOK in MVP** — customer-managed keys are an enterprise-tier feature with major operational complexity; added as future work when a real enterprise customer demands it
- **TLS 1.2+ everywhere is table-stakes** — uncontroversial industry baseline; TLS 1.3 preferred where libraries/infra support it
- **mTLS rejected consistently** — rejected in ADR-0042 for agents, rejected in ADR-0033 for webhooks, now explicitly rejected for in-cluster traffic; operational burden of PKI never matches the marginal security benefit for Kartova's threat model
- **Complements existing controls** — relies on RLS (ADR-0012) for cross-tenant isolation, audit log (ADR-0018) for access detection, and "no secrets stored" policy (ADR-0078) to keep the encryption scope manageable

## Alternatives Considered

- **Storage-level only, no application-level encryption:** Simplest, but leaves Git provider OAuth tokens exposed to anyone with PostgreSQL read access (e.g., compromised DB user, malicious backup consumer who can decrypt the backup using storage-level key). Rejected — OAuth tokens are high-value secrets and warrant defense in depth.
- **Full application-level encryption for all PII (emails, IPs, logs):** Major operational complexity: encrypted columns cannot be indexed normally, full-text search breaks, every EF Core query needs value converters, audit log querying becomes painful. Disproportionate to the threat model for work emails and IPs. Rejected.
- **Per-tenant application-level encryption of all tenant data:** Gold-standard isolation but massive implementation effort; breaks Elasticsearch search (ADR-0013) because encrypted values cannot be indexed for full-text search without blind-indexing schemes; hostile to solo-developer timeline. Rejected.
- **BYOK / HYOK (customer-managed keys):** Excellent for enterprise compliance but requires integration with customer HSM or cloud KMS, per-tenant key lifecycle management, and a significant enterprise-sales motion. Rejected for MVP; documented as future Enterprise-tier feature.
- **mTLS for internal components:** Would add cert-manager CA for internal certs, per-service cert rotation, mTLS libraries in every client — non-trivial operational cost for a solo developer. K8s NetworkPolicy provides the equivalent network-level isolation at zero operational cost. Rejected.
- **Storing OAuth tokens in Vault / Azure Key Vault instead of column encryption:** Cleaner separation but introduces a required external dependency; solo-developer operational cost; conflicts with ADR-0022 cloud-agnostic by requiring a new cross-cloud abstraction. Rejected for MVP; can be adopted later (token columns become thin references to Vault paths without schema changes).
- **TLS 1.3 mandatory (not just preferred):** Some internal clients (older Kafka client, some PostgreSQL tools) default to 1.2; mandating 1.3 would force upgrades without security benefit over 1.2 with strong cipher suites. Rejected in favor of "TLS 1.2+ required, 1.3 preferred."

## Consequences

**Positive:**
- Narrow encryption scope is implementable by a solo developer without blowing up schedule
- Storage-level encryption is invisible to application code — zero maintenance burden
- OAuth tokens (the real secrets) are protected against database-read-only compromise
- TLS 1.2+ mandate gives a clean, auditable compliance story for GDPR / MiFID II / SOC 2
- Key management hierarchy (master key → per-tenant DEK) is a standard pattern with plenty of reference implementations in .NET
- No mTLS operational cost — one less PKI surface to maintain
- Deliberate scope prevents "encryption sprawl" where every developer adds encrypted columns reflexively

**Negative / Trade-offs:**
- OAuth token handling requires DEK unwrap on every use — small performance cost (negligible at expected frequency: once per scan, cached in memory)
- Key rotation has operational procedure that must be documented and exercised (yearly target); if master key is ever lost, all per-tenant DEKs must be re-wrapped from backup
- Storage-level encryption relies on cloud provider's key (cloud-native CSI) — if Kartova runs on-premises for enterprise, a customer must provide equivalent disk encryption
- BYOK/HYOK enterprise demand will eventually force revisiting this; a documented path forward exists but is not implemented
- Work emails in logs are unencrypted at application level — a very determined insider with DB access could exfiltrate email lists; mitigated by audit (ADR-0018) and RLS (ADR-0012), but not eliminated
- Some auditors reflexively want "everything encrypted"; SOC 2 Type II evidence will document the threat-model rationale

**Neutral:**
- `tenant_encryption_keys` and extended `git_provider_connections` tables are new schema additions
- EF Core value converter for OAuth token columns is a small well-contained module
- Master key is one more K8s Secret to manage carefully
- Future BYOK support can layer on top: the DEK-wrap operation becomes delegated to a tenant-owned KMS without changing the application code path

## Implementation Notes

**DEK unwrap / wrap helper (C# sketch):**

```csharp
public class TenantKeyProvider
{
    private readonly byte[] _masterKey;
    private readonly IMemoryCache _dekCache;

    public async Task<byte[]> GetDekAsync(Guid tenantId)
    {
        if (_dekCache.TryGetValue(tenantId, out byte[] dek)) return dek;
        var wrapped = await _db.TenantEncryptionKeys
            .Where(k => k.TenantId == tenantId)
            .Select(k => k.DekWrapped).SingleAsync();
        dek = AesGcm.Unwrap(_masterKey, wrapped);
        _dekCache.Set(tenantId, dek, TimeSpan.FromMinutes(5));
        return dek;
    }
}
```

**OAuth token column encryption (EF Core value converter):**

```csharp
public class OAuthTokenEncryptor : ValueConverter<string, byte[]>
{
    public OAuthTokenEncryptor(TenantKeyProvider keys, ITenantContext ctx)
        : base(
            plaintext => AesGcm.Encrypt(keys.GetDekAsync(ctx.TenantId).Result, plaintext),
            ciphertext => AesGcm.Decrypt(keys.GetDekAsync(ctx.TenantId).Result, ciphertext))
    {}
}
```

## References

- PRD §7.3 (Security & Compliance — to be amended)
- Phase 2 E-07.F-01.S-01 (GitHub OAuth), E-07.F-02.S-01 (Azure DevOps OAuth)
- NIST SP 800-38D (AES-GCM): https://csrc.nist.gov/pubs/sp/800/38/d/final
- PostgreSQL TLS configuration: https://www.postgresql.org/docs/current/ssl-tcp.html
- Kubernetes etcd encryption at rest: https://kubernetes.io/docs/tasks/administer-cluster/encrypt-data/
- cert-manager: https://cert-manager.io
