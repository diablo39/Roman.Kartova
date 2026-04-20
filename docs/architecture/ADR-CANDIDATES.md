# Kartova — Architecture Decision Records — Pending Candidates

**Date:** 2026-04-20
**Status:** Pending review
**Instructions:** Review each candidate. Mark with [KEEP], [CHANGE], [REJECT], or [DISCUSS]. For [CHANGE] note the preferred alternative.

> **69 accepted ADRs** have been transformed into formal ADR files under [decisions/](decisions/README.md). This document now contains only candidates still pending review.

---

## Summary

Pending candidates: 10
- Flagged DISCUSS: 10 (all entries)
- Unreviewed: 0

Categories: 8

---

## Category 1: Data Platform

### ADR-003: TBD message bus (RabbitMQ/Kafka)
- **Area:** Messaging
- **Current decision:** A message bus is required for event-driven architecture (notifications, agent data); RabbitMQ or Kafka recommended, not finalized.
- **Source:** PRD §8 (Technology Stack row)
- **Rationale given:** Event-driven notifications and agent data ingestion.
- **Alternatives:** RabbitMQ, Apache Kafka, Redpanda, NATS, Azure Service Bus, AWS SQS/SNS, Google Pub/Sub, Postgres-based (Outbox + LISTEN/NOTIFY).
- **User action:** [ ] KEEP  [ ] CHANGE  [ ] REJECT  [X] DISCUSS
- **Notes:** _Rationale needed — must be decided before Phase 6._

### ADR-004: Blob storage for large artifacts (implicit)
- **Area:** Storage
- **Current decision:** Separate blob storage referenced in GDPR cascade-deletion scope (PostgreSQL + Elasticsearch + blob storage).
- **Source:** Phase 0 E-01.F-05.S-04
- **Rationale given:** Rationale needed — implied by GDPR erasure scope and logo uploads / documentation assets.
- **Alternatives:** S3/MinIO, Azure Blob Storage, GCS, PostgreSQL LO/bytea, filesystem.
- **User action:** [ ] KEEP  [ ] CHANGE  [ ] REJECT  [X] DISCUSS
- **Notes:** _..._

---

## Category 3: Multi-Tenancy

### ADR-012: Row-level security (not schema-per-tenant) for isolation
- **Area:** Tenant isolation
- **Current decision:** PostgreSQL RLS with tenant_id in all tables and composite indexes.
- **Source:** Phase 0 E-01.F-03.S-01, E-01.F-08.S-01, E-01.F-08.S-03
- **Rationale given:** Scales to 1000+ tenants (schema-per-tenant doesn't).
- **Alternatives:** Schema-per-tenant, database-per-tenant, hybrid (large tenants isolated), app-layer filtering with enforced repository pattern.
- **User action:** [ ] KEEP  [ ] CHANGE  [ ] REJECT  [X] DISCUSS
- **Notes:** _..._

### ADR-013: Elasticsearch index strategy TBD (shared vs per-tenant)
- **Area:** Tenant isolation (search)
- **Current decision:** Strategy to be documented — shared index with tenant filtering vs index-per-tenant.
- **Source:** Phase 0 E-01.F-08.S-02
- **Rationale given:** Must scale with tenant count.
- **Alternatives:** Shared index + routing by tenant, index-per-tenant, alias-per-tenant on shared indices, hybrid (tiered).
- **User action:** [ ] KEEP  [ ] CHANGE  [ ] REJECT  [X] DISCUSS
- **Notes:** _Rationale needed — decision deferred._

---

## Category 6: API & Integration Architecture

### ADR-033: HMAC-signed outbound webhooks with retry + DLQ
- **Area:** Webhook delivery
- **Current decision:** HMAC payload signing; 3 retries w/ exponential backoff; DLQ with replay.
- **Source:** PRD §4.7.1, §4.8.3; Phase 0 E-01.F-06.S-04, S-05; Phase 1 E-06a.F-01.S-04
- **Rationale given:** Reliable delivery + verifiable authenticity.
- **Alternatives:** mTLS-authenticated webhooks, JWT-signed payloads, no signing, push via SSE/WebSockets, platform pub/sub.
- **User action:** [ ] KEEP  [ ] CHANGE  [ ] REJECT  [X] DISCUSS
- **Notes:** _..._

---

## Category 8: Agent Architecture

### ADR-042: Outbound-only mTLS agent communication
- **Area:** Agent comms
- **Current decision:** Agent initiates all connections; mTLS for mutual auth; no inbound ports.
- **Source:** PRD §4.6.1, §7.3; Phase 6 E-15.F-01.S-02
- **Rationale given:** Firewall-friendly, strong auth.
- **Alternatives:** JWT-over-HTTPS (no mTLS), WireGuard/Tailscale tunnel, gRPC over TLS with bearer tokens, NATS JetStream.
- **User action:** [ ] KEEP  [ ] CHANGE  [ ] REJECT  [X] DISCUSS
- **Notes:** _..._

---

## Category 12: Observability & Monitoring

### ADR-060: /health/live + /health/ready endpoints per service
- **Area:** Health checks
- **Current decision:** Liveness and readiness endpoints check DB, Elasticsearch, KeyCloak connectivity.
- **Source:** Phase 0 E-01.F-07.S-01
- **Rationale given:** K8s probes.
- **Alternatives:** Combined `/health`, ASP.NET Core HealthChecks UI, external blackbox probing only.
- **User action:** [ ] KEEP  [ ] CHANGE  [ ] REJECT  [X] DISCUSS
- **Notes:** _..._

---

## Category 13: Billing

### ADR-061: Simple per-user monthly pricing
- **Area:** Pricing model
- **Current decision:** Flat per-user/month pricing; service accounts & public status viewers excluded.
- **Source:** PRD §6.2
- **Rationale given:** Simple, predictable.
- **Alternatives:** Per-entity / per-service, tiered plans, usage-based (scans, API calls), freemium, flat-rate per-org.
- **User action:** [ ] KEEP  [ ] CHANGE  [ ] REJECT  [X] DISCUSS
- **Notes:** _..._

---

## Category 14: Domain Model

### ADR-064: Fixed entity taxonomy (Application / Service / API-Sync / API-Async / Infrastructure / Broker / Queue+Topic / Environment / Deployment)
- **Area:** Core entities
- **Current decision:** Nine predefined entity types with fixed attributes.
- **Source:** PRD §3.1; Phase 1 Epic E-02
- **Rationale given:** Opinionated model.
- **Alternatives:** Generic "component" with type field + attributes, user-extensible entity types, Backstage-style `kind`/`spec` schema.
- **User action:** [ ] KEEP  [ ] CHANGE  [ ] REJECT  [X] DISCUSS
- **Notes:** _..._

---

## Category 16: Non-Functional / Cross-Cutting

### ADR-077: Encryption in transit + at rest across all stores
- **Area:** Security
- **Current decision:** All data encrypted at rest and in transit; mTLS for agents.
- **Source:** PRD §7.3
- **Rationale given:** Baseline SaaS security posture.
- **Alternatives:** TLS in transit only (rely on cloud disk encryption by default), customer-managed keys (BYOK/HYOK), per-tenant encryption keys.
- **User action:** [ ] KEEP  [ ] CHANGE  [ ] REJECT  [X] DISCUSS
- **Notes:** _..._
