# ADR-0033: HMAC-Signed Outbound Webhooks with Retry, DLQ, Idempotency, and Per-Subscriber Rate Limiting

**Status:** Accepted
**Date:** 2026-04-21
**Deciders:** Roman Głogowski (solo developer)
**Category:** API & Integration Architecture
**Related:** ADR-0003 (Kafka bus), ADR-0016 (MiFID II compliance), ADR-0050 (notification log as MiFID II record), ADR-0031 (per-tenant rate limiting), ADR-0051 (status page subscribers)

## Context

Webhooks are a core integration mechanism for Kartova:
- **Outbound:** tenants subscribe to platform events (scorecard breach, drift alert, breaking change, status updates) and route them to Slack, Teams, PagerDuty, or custom endpoints
- **Inbound:** GitHub/Azure DevOps webhooks trigger doc sync and re-scans
- **Status page subscribers:** webhook is one of four notification channels (ADR-0051)

Each tenant can have tens to hundreds of webhook subscriptions × hundreds of events per day, producing thousands of deliveries per minute in peak. Delivery must be reliable (MiFID II communication records per ADR-0050 — no event can be lost) and secure (payloads may contain metadata about internal services). Industry standard for webhook authentication is HMAC (GitHub, Stripe, Slack, Shopify, Twilio, PagerDuty).

## Decision

Outbound webhook delivery uses HMAC-SHA256 payload signing with a tenant-scoped shared secret, a retry-with-backoff pipeline backed by Kafka (ADR-0003), a Dead Letter Queue (DLQ) with manual replay, idempotency keys on every delivery, and per-subscriber rate limiting with circuit breaker.

**Delivery pipeline:**
1. Event occurs in platform (e.g., scorecard threshold breach)
2. Event persisted to `webhook_outbox` table within the same PostgreSQL transaction as the business change (transactional outbox pattern)
3. Outbox publisher reads new rows and publishes to Kafka topic `webhook-delivery-queue`
4. Delivery worker consumes, builds payload, sends HTTP POST
5. Result logged to `webhook_deliveries` table (MiFID II retention per ADR-0050)

**Required HTTP headers on every delivery:**
- `X-Kartova-Signature: sha256=<hex-hmac>` — HMAC-SHA256(tenant_webhook_secret, raw_body)
- `X-Kartova-Event-Id: <uuid>` — idempotency key (stable across retries of same event)
- `X-Kartova-Delivery-Id: <uuid>` — unique per delivery attempt
- `X-Kartova-Timestamp: <iso8601>` — delivery timestamp (enables replay-window validation by subscriber)
- `X-Kartova-Event-Type: <type>` — e.g., `scorecard.threshold_breach`, `deployment.started`

**Retry policy:**
- On 2xx response → mark `delivered`
- On 4xx response (except 408 and 429) → mark `dead` immediately (client error, no retry)
- On 5xx, 408, 429, connection error, or timeout → enqueue retry with exponential backoff: 1s, 5s, 30s
- After 3 failed retries → move to DLQ

**Dead Letter Queue:**
- Visible in UI under "Integrations → Webhooks → Failed Deliveries"
- Admin can manually replay a single delivery or bulk replay after fixing subscriber
- DLQ entries retained per ADR-0017 (180 days default / 5 years MiFID II)

**Per-subscriber rate limiting and circuit breaker:**
- Each webhook subscription has an independent delivery queue
- Concurrency per subscriber capped (default 10 concurrent deliveries)
- Circuit breaker: if > 50% failures in the last 5 minutes, subscription pauses for 10 minutes and admin is notified via in-app notification (ADR-0047)
- Slow subscribers cannot backpressure the shared delivery pipeline for other tenants

**Secret management:**
- Tenant-specific shared secret generated at webhook subscription creation
- Shown once in UI at creation time; retrievable only via "Rotate" operation
- On rotation, the old secret remains valid for 24 hours to allow smooth subscriber rollover (both signatures computed and sent in `X-Kartova-Signature` and `X-Kartova-Signature-Old`)
- IP allowlist optional: Kartova publishes source IPs so tenants can firewall their endpoints

**Audit/compliance:**
- Every delivery attempt logged: timestamp, response code, latency, response body (truncated)
- Retention follows ADR-0017 per tenant MiFID II flag (ADR-0016)

## Rationale

- **Industry standard** — HMAC-SHA256 is used by GitHub, Stripe, Slack, Shopify, Twilio, PagerDuty. Developers and DevOps teams recognize the pattern immediately; documentation has no learning curve
- **Simple for subscribers** — verification is 10 lines of code in any language; no JWKS endpoint fetch, no library dependency required
- **Debuggability** — payload body is plain JSON, visible in logs, curl, and tooling without base64 decoding
- **Transactional outbox** — event persisted atomically with the business change in PostgreSQL; Kafka publisher reads outbox and guarantees at-least-once delivery even if publish fails
- **Idempotency keys** — enable subscribers to safely deduplicate on retry (at-least-once becomes effectively-once at subscriber boundary)
- **Circuit breaker** — isolates slow/broken subscribers; one misbehaving endpoint cannot cascade failures across the platform
- **DLQ with manual replay** — aligns with MiFID II "no event lost" guarantee; admin retains control after max retries
- **Kafka backbone** — reuses ADR-0003 messaging infrastructure; no new technology

## Alternatives Considered

- **mTLS-authenticated webhooks:** Stronger cryptographic authentication but imposes significant operational burden on subscribers (client certificate lifecycle, rotation, PKI). Most DevOps teams reject this overhead for webhook receivers. Rejected in favor of HMAC's lower friction and near-equivalent security for this use case.
- **JWS with HS256:** Wraps HMAC in a JWS structure; adds base64 encoding overhead and library dependency without improving security over raw HMAC. Rejected as worst-of-both-worlds.
- **JWS with RS256 / JWKS endpoint:** Asymmetric signing eliminates the need for tenants to protect a shared secret and enables automatic key rotation via JWKS. Genuinely better security posture for enterprise subscribers, but deviates from industry-standard webhook convention, adds JWKS endpoint + key rotation scheduler + 1–2 weeks of additional implementation. Rejected for MVP; can be added later as an opt-in alternative signing scheme if enterprise demand emerges.
- **SSE / WebSockets push:** Long-lived connections complicate HA and scaling at 1000+ tenants; stateful reconnect logic is operationally complex. Push-based webhooks are battle-tested for this pattern. Rejected.
- **Platform pub/sub SDK (tenant subscribes directly to Kafka):** Would require exposing Kafka brokers to tenant consumers, conflicting with ADR-0003's internal-only Kafka deployment. Webhook model remains universal.

## Consequences

**Positive:**
- Familiar industry-standard pattern; subscribers integrate quickly with existing webhook tooling
- Idempotency key enables safe at-least-once delivery semantics
- Circuit breaker isolates slow subscribers — noisy-neighbor protection
- DLQ with manual replay preserves MiFID II guarantee of no lost events
- Transactional outbox prevents split-brain between business state and event emission
- Kafka-backed pipeline reuses existing infrastructure (ADR-0003)

**Negative / Trade-offs:**
- Shared secret model requires tenants to treat the webhook secret as sensitive; key rotation is manual per subscription
- HMAC alone does not prevent replay — relies on subscribers to validate `X-Kartova-Timestamp` window (documented in integration guide)
- Enterprise customers with strict zero-shared-secret policies may ask for JWS RS256 later; migration path documented as a future ADR
- Rate limiting and circuit-breaker state per subscriber adds operational storage (Redis or PostgreSQL-backed)

**Neutral:**
- Adds `webhook_outbox`, `webhook_deliveries`, `webhook_subscriptions` tables in PostgreSQL — standard CRUD
- DLQ entries are subject to retention per ADR-0017 like any other delivery record
- Inbound webhooks (GitHub, Azure DevOps) use provider-specific signature schemes — this ADR covers outbound only

## Implementation Notes

**Subscriber-side verification (example, any language):**

```python
import hmac, hashlib

received_signature = request.headers["X-Kartova-Signature"].split("=")[1]
expected = hmac.new(
    webhook_secret.encode(),
    request.body,
    hashlib.sha256
).hexdigest()

if not hmac.compare_digest(received_signature, expected):
    return 401

# Optional: check X-Kartova-Timestamp within 5 min of now
# Optional: deduplicate by X-Kartova-Event-Id in local store
```

**Kartova-side table schema (simplified):**

```sql
CREATE TABLE webhook_subscriptions (
  id UUID PRIMARY KEY,
  tenant_id UUID NOT NULL,
  endpoint_url TEXT NOT NULL,
  event_types TEXT[] NOT NULL,
  secret_hash TEXT NOT NULL,       -- current secret
  previous_secret_hash TEXT,       -- valid for 24h after rotation
  previous_secret_expires_at TIMESTAMPTZ,
  paused BOOLEAN NOT NULL DEFAULT FALSE,
  concurrency_limit INT NOT NULL DEFAULT 10
);

CREATE TABLE webhook_deliveries (
  id UUID PRIMARY KEY,
  subscription_id UUID NOT NULL REFERENCES webhook_subscriptions,
  event_id UUID NOT NULL,          -- idempotency key
  event_type TEXT NOT NULL,
  attempt INT NOT NULL,
  status TEXT NOT NULL,            -- delivered | failed | dead
  response_code INT,
  latency_ms INT,
  created_at TIMESTAMPTZ NOT NULL
);
```

## References

- PRD §4.7.1 (notifications), §4.8.3 (extensibility)
- Phase 0 E-01.F-06.S-04 (webhook registration), E-01.F-06.S-05 (retry + DLQ)
- Phase 1 E-06a.F-01.S-04 (webhook notifications)
- Phase 4 E-12.F-03.S-01 (status page webhook subscribers)
- Transactional outbox pattern: https://microservices.io/patterns/data/transactional-outbox.html
- GitHub webhook security (reference implementation): https://docs.github.com/en/webhooks/using-webhooks/validating-webhook-deliveries
