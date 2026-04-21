# ADR-0042: Agent Communication via HTTPS Polling with Long-Lived Bearer Tokens and Manual Rotation

**Status:** Accepted
**Date:** 2026-04-21
**Deciders:** Roman Głogowski (solo developer)
**Category:** Agent Architecture
**Related:** ADR-0006 (KeyCloak identity), ADR-0007 (JWT auth), ADR-0018 (audit log), ADR-0022 (cloud-agnostic K8s), ADR-0029 (REST API), ADR-0033 (HMAC webhooks), ADR-0041 (.NET agent), ADR-0043 (agent deployment), ADR-0044 (agent config pull)

## Context

The hybrid agent (ADR-0041) runs inside customer infrastructure and must communicate with the Kartova platform for health check forwarding, metrics streaming, service discovery, and config pull (ADR-0044). Connectivity constraints in enterprise environments are the dominant factor:

- Customer network policies typically forbid inbound connections — agent must initiate all communication outbound
- Many enterprise networks deploy **TLS-intercepting proxies** (BlueCoat/Symantec, Zscaler, Palo Alto Prisma, corporate Squid) that decrypt, inspect, and re-encrypt HTTPS traffic
- A significant fraction of such proxies do not support HTTP/2, break HTTP/2 framing, or kill long-lived connections on idle timeouts
- Industry-standard enterprise agents (Datadog, Grafana Agent/Alloy, Elastic Agent, New Relic, Splunk) use plain HTTPS request-response precisely for this reason; OpenTelemetry's own guidance recommends OTLP/HTTP over OTLP/gRPC for proxy compatibility

Authentication must:
- Bind each agent to a specific tenant (no cross-tenant access)
- Be revocable immediately if compromised
- Impose minimal customer DevOps burden (no PKI lifecycle)
- Be consistent with existing platform auth (ADR-0006 KeyCloak, ADR-0007 JWT)

At scale target 1000+ tenants with dozens of agents per tenant, the protocol must be firewall-friendly, bandwidth-efficient, and operationally simple for a solo developer.

## Decision

**Protocol:** HTTPS request-response over HTTP/1.1 + TLS with JSON payloads. No gRPC, no streaming, no WebSockets in MVP. The agent polls a single heartbeat endpoint:

```
POST /api/v1/agents/{agent_id}/heartbeat
Headers:
  Authorization: Bearer <agent_token>
  Content-Type: application/json
Body (agent → platform):
  {
    "config_version": "abc123",
    "health_results": [...],
    "metrics_batch": [...],
    "discovered_services": [...],
    "timestamp": "2026-04-21T10:30:00Z"
  }
Response (platform → agent):
  {
    "config": { ... },          // present if config_version changed
    "config_version": "def456",
    "commands": [...]           // queued commands for this agent
  }
```

**Polling cadence:** default 30 seconds; configurable per-agent (min 10s, max 300s) via admin UI. Heartbeat includes payload batching — metrics and health results accumulate locally between polls.

**Health-change fast-path:** on any health status transition detected locally by the agent, an immediate out-of-cycle heartbeat is sent to minimize status propagation latency. Other data waits for the next scheduled heartbeat.

**Authentication:** long-lived bearer tokens, **no expiry by default**. Tenant admin generates per-agent tokens through the Kartova UI; token is displayed once and copied into the agent's Kubernetes Secret, environment variable, or mounted file.

**Rotation model:** Manual rotation with dual-token acceptance. Each agent may have up to **2 active tokens simultaneously** for zero-downtime rotation:
1. Admin generates new token in UI (old remains active)
2. Admin updates Secret/env/file in cluster
3. Agent restarts (or rolling restart) with new token; platform logs token switch
4. Admin manually revokes old token in UI
5. Safety net: if admin forgets, platform auto-revokes older token 30 days after newer token first used (configurable per org)

**Optional TTL:** Admin may set expiry at token creation (e.g., 90 days, 1 year) for security-conscious or compliance-driven tenants. Default is no expiry.

**Revocation:** Immediate via UI. Platform rejects revoked tokens on next heartbeat with HTTP 401; agent logs, sleeps, and retries with exponential backoff (K8s/Docker restart loop on persistent failure).

**Agent state:** stateless. Agent reads the token from one of:
- Kubernetes Secret mounted as file (default for K8s deployment — GitOps-friendly)
- Environment variable
- Read-only file at `/etc/kartova-agent/token` (Docker/standalone deployment)

Agent never writes its token; rotation is entirely admin-driven.

**Token properties:**
- Per-agent (one token = one agent), not per-tenant — limits blast radius on compromise
- High-entropy opaque string (256-bit random, base64url encoded) — not a JWT; platform looks up `agent_id` and `tenant_id` from token hash
- Tokens stored as Argon2id hash in PostgreSQL `agent_tokens` table
- Each authentication attempt logged to audit trail (ADR-0018) with source IP, timestamp, and outcome

**TLS:** Platform endpoint uses valid public TLS certificate (Let's Encrypt or equivalent). Agent validates hostname and certificate chain. No mTLS.

**Proxy support:** Agent respects standard `HTTPS_PROXY`, `HTTP_PROXY`, `NO_PROXY` environment variables. HTTP/1.1 is supported by every HTTP proxy in common use, including TLS-intercepting proxies.

## Rationale

- **Works through every HTTP proxy** — plain HTTPS/1.1 request-response is the lowest-common-denominator protocol; no known enterprise proxy breaks it
- **Industry-standard agent pattern** — Datadog, Grafana Agent, Elastic Agent, New Relic, Splunk, OpenTelemetry Collector (in HTTP mode) all use this shape
- **Solo-dev operational simplicity** — no streaming state machine, no connection-keepalive tuning, no HTTP/2 flow-control debugging
- **Customer DevOps simplicity** — one token in a Secret or env var; no PKI; no cert rotation; GitOps-compatible; matches the mental model of API keys that every DevOps team already uses
- **Dual-token rotation** — zero-downtime, admin-paced, no coordination with platform timing
- **No expiry by default** — matches typical API key UX. Explicit TTL is available for tenants with policy requirements.
- **Per-agent tokens** — compromise is localized; revoking one agent's token does not disrupt sibling agents
- **Stateless agent** — trivial to containerize, restart, replace; no race conditions around token rotation; no data corruption from interrupted writes
- **Latency is acceptable for the use case** — 30s polling is fine for config changes, service discovery, and aggregated metrics; health-change fast-path addresses the one latency-sensitive case
- **Consistent with ADR-0029** (REST API) — agent API documentation reuses the same OpenAPI tooling and doc engine (ADR-0034)

## Alternatives Considered

- **gRPC bidirectional streaming:** Efficient and elegant on the wire but breaks under TLS-intercepting enterprise proxies that don't support HTTP/2 or that downgrade to HTTP/1.1. Idle-connection timeouts on corporate proxies force constant reconnects. Rejected as premature optimization for the enterprise target market; acknowledged as a future opt-in mode for tenants without proxy constraints.
- **WebSocket over HTTPS:** Better proxy compatibility than gRPC (uses HTTP/1.1 Upgrade handshake) and enables real-time platform→agent push. Adds protocol complexity; some strict proxies still block WebSocket upgrades; requires dual code paths (WebSocket + polling fallback) to be production-safe. Can be added later as a push-notification channel if latency becomes a demonstrated pain point.
- **mTLS:** Strongest authentication but imposes heavy PKI operational burden on both Kartova (CA management, cert issuance, rotation scheduler) and customer DevOps (client cert lifecycle, trust chains). Certificate-based debugging is a known support cost. Does not eliminate the token-to-tenant mapping. Rejected.
- **JWT with auto-rotation (refresh + access flow):** More compliant with short-lived-credential best practices but requires stateful agent (persist refresh token, rotate under load, handle rotation failures). Adds code, failure modes, and complexity. Deferred as an optional future enhancement for MiFID II tenants that require it.
- **WireGuard / Tailscale mesh:** Requires customers to install and operate VPN software; adds cognitive load; Tailscale adds vendor cost. Rejected as too heavy.
- **Workload Identity (GKE / AKS / EKS IRSA):** Cloud-native, eliminates secrets in the agent, but tightly couples to a specific cloud — violates ADR-0022 cloud-agnostic deployment.
- **JWT no-expiry (stored opaquely in DB):** JWT brings no benefit when all lookups hit the database; claims are unused. Opaque tokens are simpler and allow hashed storage.
- **gRPC with HTTPS fallback:** Most robust but requires maintaining two protocol implementations; rejected for MVP — scope creep.

## Consequences

**Positive:**
- Works through every enterprise network topology in common use today
- Minimal code on both sides — one HTTP endpoint, one agent timer loop
- Easy to debug: every request is reproducible with `curl`
- Customer onboarding is trivial: `helm install kartova-agent --set token=<...>`
- GitOps workflows (Flux, ArgoCD) can manage tokens as sealed Secrets
- Dual-token rotation is zero-downtime and admin-paced
- Revocation is instant and precisely scoped
- Stateless agent design makes the agent trivially replaceable
- Agent API is documented with OpenAPI alongside the rest of the platform (ADR-0034)

**Negative / Trade-offs:**
- 30–60s polling latency for config changes and platform-initiated commands (health-change fast-path mitigates the most latency-sensitive case)
- Slightly higher bandwidth overhead than streaming (repeated TLS handshakes, repeated HTTP headers) — negligible at target agent density
- Long-lived tokens violate short-lived-credential best practice; compensating controls: per-agent scope, audit logging, admin-set optional TTL, anomaly detection (future Phase 7)
- Admin responsibility to rotate periodically — UI reminders exist but cannot force action
- MiFID II / SOC2 auditors may prefer short-lived credentials; those tenants should set explicit TTLs
- Token storage in customer infrastructure requires customer-side hardening (etcd encryption, file permissions, sealed-secrets) — documented in onboarding

**Neutral:**
- New PostgreSQL tables: `agents` and `agent_tokens` (hashed token, created_at, last_used_at, last_used_ip, optional_expires_at, revoked_at)
- Agent heartbeat endpoint is versioned alongside the rest of the API (ADR-0030)
- Future evolution to WebSocket push, gRPC streaming, or JWT auto-rotation remains possible without wire-protocol breakage — each would be a new authentication or transport mode layered on top

## Implementation Notes

**Agent loop (pseudocode):**

```
token := readTokenFromEnvOrFile()
pollInterval := 30 * time.Second

for {
  payload := collectPending()    // batched metrics, health, discoveries
  if healthChanged() { /* skip wait */ }
  resp := httpsPost("/api/v1/agents/{id}/heartbeat", payload, bearerToken=token)
  if resp.status == 401 { log; sleep(backoff); continue }
  applyConfig(resp.config); enqueueCommands(resp.commands)
  sleep(pollInterval)
}
```

**Platform-side tables (simplified):**

```sql
CREATE TABLE agents (
  agent_id UUID PRIMARY KEY,
  tenant_id UUID NOT NULL,
  name TEXT NOT NULL,
  poll_interval_seconds INT NOT NULL DEFAULT 30,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  last_seen_at TIMESTAMPTZ
);

CREATE TABLE agent_tokens (
  id UUID PRIMARY KEY,
  agent_id UUID NOT NULL REFERENCES agents,
  tenant_id UUID NOT NULL,
  token_hash TEXT NOT NULL,           -- Argon2id
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  expires_at TIMESTAMPTZ,             -- NULL = no expiry
  last_used_at TIMESTAMPTZ,
  last_used_ip INET,
  revoked_at TIMESTAMPTZ
);
-- Enforced at app layer: max 2 non-revoked non-expired tokens per agent
```

**Proxy configuration (customer-side):**

Agent honors standard env vars:
```
HTTPS_PROXY=http://corporate-proxy.internal:8080
NO_PROXY=localhost,.cluster.local
```
No custom proxy configuration required on the agent binary.

## References

- PRD §4.6.1 (hybrid agent communication), §7.3 (security posture)
- Phase 6 E-15.F-01.S-01 (agent deployment), E-15.F-01.S-02 (agent communication), E-15.F-01.S-03 (agent config)
- OpenTelemetry OTLP over HTTP (industry precedent): https://opentelemetry.io/docs/specs/otlp/#otlphttp
- Datadog agent network docs (industry reference): https://docs.datadoghq.com/agent/configuration/proxy/
- Argon2id password hashing: https://datatracker.ietf.org/doc/html/rfc9106
