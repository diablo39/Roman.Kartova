---
doc_type: backlog-index
product: Kartova
source: PRODUCT-REQUIREMENTS.md
checklist: CHECKLIST.md
adr_library: ../architecture/decisions/README.md
convention:
  epic: "E-XX"
  feature: "E-XX.F-YY"
  story: "E-XX.F-YY.S-ZZ"
totals:
  phases: 10
  epics: 30
  features: 73
  stories: 209
mvp_scope:
  phases: [0, 1, 2, 3, 4, 5]
  epics: [E-01, E-02, E-03, E-04, E-05, E-06, E-06a, E-07, E-08, E-09, E-10, E-11, E-12, E-13, E-14, E-14a]
  features: 63
  stories: 185
post_mvp:
  phases: [6, 7, 8, 9]
  epics: [E-15, E-16, E-17, E-18, E-19, E-20, E-21, E-22, E-23, E-24, E-25, E-26, E-27, E-28]
phases:
  - id: 0
    name: Foundation
    file: phases/phase-0-foundation.md
    epics: [E-01]
    features: 8
    stories: 33
  - id: 1
    name: Core Catalog & Notifications
    file: phases/phase-1-core-catalog.md
    epics: [E-02, E-03, E-04, E-05, E-06, E-06a]
    features: 22
    stories: 55
  - id: 2
    name: Auto-Import
    file: phases/phase-2-auto-import.md
    epics: [E-07, E-08, E-09, E-10]
    features: 11
    stories: 36
  - id: 3
    name: Documentation
    file: phases/phase-3-documentation.md
    epics: [E-11]
    features: 5
    stories: 15
  - id: 4
    name: Status Page
    file: phases/phase-4-status-page.md
    epics: [E-12]
    features: 5
    stories: 16
  - id: 5
    name: CLI, Policy & Billing
    file: phases/phase-5-cli-policy.md
    epics: [E-13, E-14, E-14a]
    features: 6
    stories: 15
  - id: 6
    name: Agent & Monitoring
    file: phases/phase-6-agent-monitoring.md
    epics: [E-15, E-16]
    features: 6
    stories: 12
  - id: 7
    name: Intelligence
    file: phases/phase-7-intelligence.md
    epics: [E-17, E-18, E-19, E-20]
    features: 5
    stories: 13
  - id: 8
    name: Analytics
    file: phases/phase-8-analytics.md
    epics: [E-21, E-22, E-23, E-24]
    features: 5
    stories: 14
  - id: 9
    name: Advanced
    file: phases/phase-9-advanced.md
    epics: [E-25, E-26, E-27, E-28]
    features: null
    stories: null
status:
  decomposition: complete
  critic_audit: complete (2026-04-16)
  adr_alignment: complete (2026-04-21, 79 ADRs accepted)
---

# Kartova — Epics, Features & User Stories

**Source:** [PRODUCT-REQUIREMENTS.md](PRODUCT-REQUIREMENTS.md)
**Progress:** [CHECKLIST.md](CHECKLIST.md)
**ADRs:** [decisions/README.md](../architecture/decisions/README.md)
**Date:** 2026-04-16 (last touched 2026-04-21)
**Convention:** Epic = `E-XX` · Feature = `E-XX.F-YY` · Story = `E-XX.F-YY.S-ZZ`

> **PRD Phase 9 reconciliation:** Several original Phase 9 items were pulled forward where they logically belong:
> - Interactive dependency graph → Phase 1, **E-04.F-02**
> - Environment tracking → Phase 1, **E-02.F-05**
> - Scheduled re-scans → Phase 2, **E-08.F-03**

---

## How to use this file

| Goal | Go to |
|------|-------|
| Find a specific epic by ID or title | [Epic Index](#epic-index) |
| Find a feature by ID or title | [Feature Index](#feature-index) |
| Explore one phase in detail | [Phase Files](#phase-files) |
| Search by concept (e.g. "notifications", "agent", "GDPR") | [Keyword Index](#keyword-index) |
| Map story → ADR → PRD section | [Cross-References](#cross-references) |
| Track implementation progress | [CHECKLIST.md](CHECKLIST.md) |
| See what shifted from PRD | [Audit Trail](#audit-trail) |

---

## Phase Files

| Phase | File | Epics | Features | Stories | MVP |
|-------|------|-------|----------|---------|-----|
| 0 — Foundation | [phase-0-foundation.md](phases/phase-0-foundation.md) | 1 (E-01) | 8 | 33 | ✅ |
| 1 — Core Catalog & Notifications | [phase-1-core-catalog.md](phases/phase-1-core-catalog.md) | 6 (E-02..E-06, E-06a) | 22 | 55 | ✅ |
| 2 — Auto-Import | [phase-2-auto-import.md](phases/phase-2-auto-import.md) | 4 (E-07..E-10) | 11 | 36 | ✅ |
| 3 — Documentation | [phase-3-documentation.md](phases/phase-3-documentation.md) | 1 (E-11) | 5 | 15 | ✅ |
| 4 — Status Page | [phase-4-status-page.md](phases/phase-4-status-page.md) | 1 (E-12) | 5 | 16 | ✅ |
| 5 — CLI, Policy & Billing | [phase-5-cli-policy.md](phases/phase-5-cli-policy.md) | 3 (E-13, E-14, E-14a) | 6 | 15 | ✅ |
| 6 — Agent & Monitoring | [phase-6-agent-monitoring.md](phases/phase-6-agent-monitoring.md) | 2 (E-15, E-16) | 6 | 12 | — |
| 7 — Intelligence | [phase-7-intelligence.md](phases/phase-7-intelligence.md) | 4 (E-17..E-20) | 5 | 13 | — |
| 8 — Analytics | [phase-8-analytics.md](phases/phase-8-analytics.md) | 4 (E-21..E-24) | 5 | 14 | — |
| 9 — Advanced | [phase-9-advanced.md](phases/phase-9-advanced.md) | 4 (E-25..E-28) | — | — | — |
| **Total** |  | **30** | **73** | **209** | **16 / 63 / 185** |

---

## Epic Index

Flat list of all 30 epics with phase, scope, and file pointer.

| ID | Title | Phase | Features | Stories | File |
|----|-------|-------|----------|---------|------|
| E-01 | Project Foundation & Infrastructure | 0 | 8 | 33 | [phase-0](phases/phase-0-foundation.md) |
| E-02 | Entity Registry | 1 | 5 | — | [phase-1](phases/phase-1-core-catalog.md) |
| E-03 | Organization & Team Management | 1 | 5 | — | [phase-1](phases/phase-1-core-catalog.md) |
| E-04 | Entity Relationships | 1 | 2 | — | [phase-1](phases/phase-1-core-catalog.md) |
| E-05 | Search | 1 | 1 | — | [phase-1](phases/phase-1-core-catalog.md) |
| E-06 | Dashboards & Visualizations (Core) | 1 | 5 | — | [phase-1](phases/phase-1-core-catalog.md) |
| E-06a | Notification Infrastructure | 1 | 3 | — | [phase-1](phases/phase-1-core-catalog.md) |
| E-07 | Git Provider Integration | 2 | 2 | — | [phase-2](phases/phase-2-auto-import.md) |
| E-08 | Auto-Import Engine | 2 | 4 | — | [phase-2](phases/phase-2-auto-import.md) |
| E-09 | Self-Service Onboarding Wizard | 2 | 1 | — | [phase-2](phases/phase-2-auto-import.md) |
| E-10 | Scorecards & Data Quality | 2 | 2 | — | [phase-2](phases/phase-2-auto-import.md) |
| E-11 | Documentation Management | 3 | 5 | 15 | [phase-3](phases/phase-3-documentation.md) |
| E-12 | Public Status Page | 4 | 5 | 16 | [phase-4](phases/phase-4-status-page.md) |
| E-13 | CLI Tool | 5 | 3 | — | [phase-5](phases/phase-5-cli-policy.md) |
| E-14 | Policy Engine | 5 | 2 | — | [phase-5](phases/phase-5-cli-policy.md) |
| E-14a | Billing & Subscription Management | 5 | 1 | — | [phase-5](phases/phase-5-cli-policy.md) |
| E-15 | Hybrid Agent | 6 | 4 | — | [phase-6](phases/phase-6-agent-monitoring.md) |
| E-16 | Monitoring Integrations | 6 | 2 | — | [phase-6](phases/phase-6-agent-monitoring.md) |
| E-17 | Service Maturity Model | 7 | 2 | — | [phase-7](phases/phase-7-intelligence.md) |
| E-18 | Dependency Risk Scoring | 7 | 1 | — | [phase-7](phases/phase-7-intelligence.md) |
| E-19 | Developer Experience Score | 7 | 1 | — | [phase-7](phases/phase-7-intelligence.md) |
| E-20 | Multi-Environment Drift Detection | 7 | 1 | — | [phase-7](phases/phase-7-intelligence.md) |
| E-21 | API Changelog & Breaking Change Detection | 8 | 2 | — | [phase-8](phases/phase-8-analytics.md) |
| E-22 | Change Impact Preview | 8 | 1 | — | [phase-8](phases/phase-8-analytics.md) |
| E-23 | Built-in Tech Radar | 8 | 1 | — | [phase-8](phases/phase-8-analytics.md) |
| E-24 | Cost Attribution | 8 | 1 | — | [phase-8](phases/phase-8-analytics.md) |
| E-25 | Incident Management | 9 | — | — | [phase-9](phases/phase-9-advanced.md) |
| E-26 | Plugin Architecture | 9 | — | — | [phase-9](phases/phase-9-advanced.md) |
| E-27 | AI-Powered Search | 9 | — | — | [phase-9](phases/phase-9-advanced.md) |
| E-28 | Multi-Region Deployment | 9 | — | — | [phase-9](phases/phase-9-advanced.md) |

---

## Feature Index

All 73 scoped features across phases 0–8. (Phase 9 epics are not yet feature-decomposed.)

### Phase 0 — Foundation

| ID | Title |
|----|-------|
| E-01.F-01 | Project Scaffolding |
| E-01.F-02 | CI/CD Pipeline |
| E-01.F-03 | Database Foundation |
| E-01.F-04 | Authentication & Authorization |
| E-01.F-05 | Data Retention & Compliance Infrastructure |
| E-01.F-06 | Platform API Infrastructure |
| E-01.F-07 | Platform Observability |
| E-01.F-08 | Performance & Scalability Baseline |

### Phase 1 — Core Catalog & Notifications

| ID | Title |
|----|-------|
| E-02.F-01 | Application Entity Management |
| E-02.F-02 | Service Entity Management |
| E-02.F-03 | API Entity Management (Sync & Async) |
| E-02.F-04 | Infrastructure & Broker Entity Management |
| E-02.F-05 | Environment & Deployment Tracking |
| E-03.F-01 | Organization Management |
| E-03.F-02 | Team Management |
| E-03.F-03 | System Grouping |
| E-03.F-04 | Tag System |
| E-03.F-05 | Multi-Ownership |
| E-04.F-01 | Manual Relationship Management |
| E-04.F-02 | Relationship Visualization |
| E-05.F-01 | Entity Search |
| E-06.F-01 | Catalog Home Dashboard |
| E-06.F-02 | Team Dashboard |
| E-06.F-03 | Organization Overview Dashboard |
| E-06.F-04 | Environment Map Dashboard |
| E-06.F-05 | Status Board Dashboard |
| E-06a.F-01 | Notification Dispatch Engine |
| E-06a.F-02 | Notification Preferences & Policies |
| E-06a.F-03 | Native Integrations (Slack & Teams) |

### Phase 2 — Auto-Import

| ID | Title |
|----|-------|
| E-07.F-01 | GitHub Integration |
| E-07.F-02 | Azure DevOps Integration |
| E-08.F-01 | Single Repository Deep Scan |
| E-08.F-02 | Bulk Organization Scan |
| E-08.F-03 | Scheduled Re-scan |
| E-08.F-04 | Scan Resilience & Error Handling |
| E-09.F-01 | Onboarding Wizard Flow |
| E-10.F-01 | Scorecard System |
| E-10.F-02 | Nudge System |

### Phase 3 — Documentation

| ID | Title |
|----|-------|
| E-11.F-01 | Git-Synced Markdown Documentation |
| E-11.F-02 | Sync API Documentation (OpenAPI/gRPC/GraphQL) |
| E-11.F-03 | Async API Documentation (AsyncAPI/CloudEvents/Schema Registry) |
| E-11.F-04 | Documentation Hub per Service |
| E-11.F-05 | Cross-Service Referencing & Documentation Search |

### Phase 4 — Status Page

| ID | Title |
|----|-------|
| E-12.F-01 | Status Page Configuration |
| E-12.F-02 | Status Management |
| E-12.F-03 | Subscriber Notifications |
| E-12.F-04 | Uptime History & Charts |
| E-12.F-05 | Status Page Infrastructure & High Availability |

### Phase 5 — CLI, Policy & Billing

| ID | Title |
|----|-------|
| E-13.F-01 | CLI Core |
| E-13.F-02 | Deployment Reporting |
| E-13.F-03 | Validation & Scanning |
| E-14.F-01 | Policy Definition |
| E-14.F-02 | CLI Policy Enforcement |
| E-14a.F-01 | Billing Integration |

### Phase 6 — Agent & Monitoring

| ID | Title |
|----|-------|
| E-15.F-01 | Agent Deployment & Communication |
| E-15.F-02 | Health Checks |
| E-15.F-03 | Metrics Collection |
| E-15.F-04 | Service Discovery |
| E-16.F-01 | Prometheus Integration |
| E-16.F-02 | Grafana Cloud Integration |

### Phase 7 — Intelligence

| ID | Title |
|----|-------|
| E-17.F-01 | Maturity Calculation & Display |
| E-17.F-02 | Maturity Dashboards |
| E-18.F-01 | Risk Calculation & Heatmap |
| E-19.F-01 | DX Score Calculation & Suggestions |
| E-20.F-01 | Drift Monitoring & Dashboard |

### Phase 8 — Analytics

| ID | Title |
|----|-------|
| E-21.F-01 | API Version Tracking |
| E-21.F-02 | Breaking Change Detection & Alerting |
| E-22.F-01 | Blast Radius Analysis |
| E-23.F-01 | Tech Radar Visualization & Governance |
| E-24.F-01 | Cost Tracking & Dashboards |

---

## Keyword Index

Alphabetical concept → where to look. Use this when searching by topic rather than by ID.

| Keyword | Epics / Features |
|---------|-------------------|
| Agent (hybrid) | E-15 (all), E-15.F-01 (HTTPS polling, token rotation — ADR-0042) |
| AI / semantic search | E-27 (Phase 9) |
| API (sync — OpenAPI/gRPC/GraphQL) | E-02.F-03, E-11.F-02, E-21 |
| API (async — AsyncAPI/CloudEvents) | E-02.F-03, E-11.F-03 |
| API changelog / breaking changes | E-21 (both features) |
| Audit log | E-01.F-03 (S-03), E-01.F-05 |
| Authentication (OIDC/JWT/KeyCloak) | E-01.F-04 |
| Authorization (RBAC) | E-01.F-04, E-03.F-05 |
| Auto-import | Phase 2 (E-07..E-10) |
| Azure DevOps | E-07.F-02 |
| Billing & subscriptions | E-14a (ADR-0061 — four-tier pricing) |
| Blast radius / change impact | E-22.F-01 |
| Bulk operations | E-01.F-06, E-08.F-02 |
| CI/CD | E-01.F-02 |
| CLI tool | E-13 (all) |
| Compliance (GDPR, MiFID II) | E-01.F-05 (S-04..S-08) |
| Cost attribution / FinOps | E-24 |
| Custom attributes (JSONB) | E-02.F-01..F-04 (per ADR-0064) |
| Custom Entity (10th type) | Phase 2 addition (ADR-0064) |
| Dashboards | E-06 (all), E-17.F-02, E-20.F-01, E-24 |
| Data retention / erasure | E-01.F-05 |
| Dependency graph (interactive) | E-04.F-02 |
| Dependency risk scoring | E-18 |
| Deployment tracking | E-02.F-05, E-13.F-02 |
| Developer Experience (DX) Score | E-19 |
| Documentation management | E-11 (all) |
| Drift detection (multi-env) | E-20 |
| Elasticsearch search | E-05.F-01 (ADR-0013 — shared index + routing) |
| Encryption | E-01.F-03, E-01.F-04 (ADR-0077 — OAuth tokens only, TLS 1.2+) |
| Entity Registry | E-02 (all) |
| Entity types (9 fixed) | E-02.F-01..F-04 (Application, Service, API, Infrastructure, Broker...) |
| Environment tracking | E-02.F-05, E-06.F-04 |
| GDPR right to erasure | E-01.F-05 (S-04) |
| GitHub integration | E-07.F-01 |
| Grafana Cloud | E-16.F-02 |
| Health checks | E-01.F-07, E-15.F-02 (ADR-0060 — three K8s probes) |
| HMAC webhooks | E-01.F-06 (ADR-0033) |
| Incident management | E-25 (Phase 9) |
| Kafka (Strimzi) | E-01.F-01 (ADR-0003) |
| Keyword "Critic audit" | See [Audit Trail](#audit-trail) |
| Maturity model | E-17 (all) |
| Metrics collection | E-15.F-03, E-16 |
| MinIO / S3 blob storage | E-01.F-01 (ADR-0004) |
| Monitoring integrations | E-16 (all) |
| Multi-ownership | E-03.F-05 |
| Multi-region | E-28 (Phase 9) |
| Multi-tenancy (RLS) | E-01.F-03, E-01.F-08 (ADR-0012) |
| Nudges | E-10.F-02 |
| Observability | E-01.F-07 |
| Onboarding wizard | E-09 |
| Organization management | E-03.F-01 |
| Ownership | E-03 (all, esp. F-05) |
| Plugin architecture | E-26 (Phase 9) |
| Policy engine | E-14 (all) |
| PostgreSQL foundation | E-01.F-03 |
| Prometheus | E-16.F-01 |
| Re-scan (scheduled) | E-08.F-03 |
| Relationships (manual vs auto) | E-04.F-01 (manual highlighted, auto can't override) |
| Relationships (visualization) | E-04.F-02 |
| RLS (Row-Level Security) | E-01.F-03, E-01.F-08 (ADR-0012) |
| Scan — single repo deep | E-08.F-01 |
| Scan — bulk org | E-08.F-02 |
| Scan — resilience | E-08.F-04 |
| Schema Registry | E-11.F-03 |
| Scorecards | E-10.F-01 |
| Search | E-05 (core), E-11.F-05 (docs), E-27 (AI — Phase 9) |
| Service Catalog core | E-02 |
| Slack / Teams integrations | E-06a.F-03 |
| SLA / uptime charts | E-12.F-04 |
| Status Page (public) | E-12 (all) |
| SSL / custom domain | E-12.F-01 (S-02, S-03) |
| Subscriber notifications (email/SMS) | E-12.F-03 |
| System grouping | E-03.F-03 |
| Tag system | E-03.F-04 |
| Team management | E-03.F-02 |
| Tech Radar | E-23 |
| Tenancy isolation | E-01.F-08 (ADR-0012) |
| Webhooks | E-01.F-06 (ADR-0033) |

---

## Cross-References

How this backlog connects to neighboring documents.

### Backlog → PRD

| Backlog element | PRD section |
|-----------------|-------------|
| E-02 (Entity Registry) | §3 Entity Model |
| E-03 (Org & Teams) | §3.2 Ownership |
| E-01.F-04 (Auth) | §5 Authentication |
| E-01.F-05 (Compliance) | §5 Multi-Tenancy + §7 NFRs |
| E-06a (Notifications) | §4 Feature Requirements — Notifications |
| E-12 (Status Page) | §4 Feature Requirements — Status Page |
| E-14a (Billing) | §6.2 Pricing Tiers |
| E-15 (Agent) | §4.6.1 Agent Protocol |

### Backlog → ADRs

Detailed story → ADR mapping lives in each phase file's ADRs column. High-traffic anchors:

| Feature | ADR(s) |
|---------|--------|
| E-01.F-01 | ADR-0001, 0003 (Kafka), 0004 (MinIO) |
| E-01.F-03 | ADR-0012 (RLS), 0077 (encryption) |
| E-01.F-04 | ADR-0077 (TLS/mTLS posture) |
| E-01.F-06 | ADR-0033 (HMAC webhooks) |
| E-01.F-07 | ADR-0060 (three-probe health) |
| E-02.F-01..F-04 | ADR-0064 (9 fixed + JSONB + Custom Entity phased) |
| E-05.F-01 | ADR-0013 (ES shared index + routing) |
| E-14a.F-01 | ADR-0061 (four-tier pricing) |
| E-15.F-01 | ADR-0042 (HTTPS polling agent) |

Full library: [decisions/README.md](../architecture/decisions/README.md).

---

## Audit Trail

| Date | Action |
|------|--------|
| 2026-04-16 | Initial decomposition created |
| 2026-04-16 | Critic audit completed — 9 CRITICAL, 16 IMPORTANT, 6 SUGGESTION findings |
| 2026-04-16 | All CRITICAL and IMPORTANT findings incorporated (see changes below) |
| 2026-04-16 | Split into individual phase files and progress checklist created |
| 2026-04-21 | ADR-0064 accepted — Phase 2 adds Custom Entity (10th type); MVP adds `custom_attributes` JSONB to all nine fixed types |
| 2026-04-21 | Document restructured for LLM-friendly navigation (YAML front-matter, Epic Index, Feature Index, Keyword Index, Cross-References) |

### Key changes from Critic audit

- **C-01:** Added E-01.F-06 (Platform API Infrastructure) — versioning, rate limiting, bulk ops, webhooks
- **C-02/C-06/C-07:** Added E-06a (Notification Infrastructure) — dispatch engine, preferences, Slack/Teams
- **C-03:** Added E-12.F-05 (Status Page HA Infrastructure) — separate deployment, data sync, monitoring
- **C-04:** Added SMS to E-12.F-03.S-01 subscriber notifications
- **C-05:** Added E-12.F-01.S-05 (Internal-only status page)
- **C-08:** Added E-01.F-08 (Performance & Scalability Baseline) — indexing strategy, RLS decision
- **C-09:** Added E-08.F-03.S-04 (Conflict review queue for re-scan conflicts)
- **I-01:** Added E-06.F-04 (Environment Map Dashboard), E-06.F-05 (Status Board Dashboard)
- **I-02/I-06:** Added E-01.F-05.S-04..S-08 (GDPR erasure, consent, breach notification, data residency, MiFID II)
- **I-07:** Scan feature split deferred to sprint planning
- **I-09:** RLS chosen over schema-per-tenant (E-01.F-08.S-03)
- **I-10:** Split custom domain into domain config + SSL provisioning (E-12.F-01.S-02/S-03)
- **I-13:** Added E-08.F-04 (Scan Resilience & Error Handling) — 4 stories
- **I-14:** Added E-01.F-07 (Platform Observability) — health, logging, metrics, alerting
- **I-16:** Added E-14a (Billing & Subscription Management)
- **I-17:** Reconciliation note added at top
- **I-19:** Added E-02.F-01.S-05 (minimum field enforcement)
- **I-20:** Added E-03.F-05.S-03/S-04 (multi-ownership rules, team deletion)
- **I-22:** Note added to E-17.F-01.S-01 about L5 dependency on Phase 8
- **S-02:** Added E-01.F-06.S-06 (Kartova API self-documentation)
- **S-04:** Updated E-04.F-01.S-04 with demotion warning semantics
- **S-05:** Updated E-01.F-03.S-03 audit scope to explicitly include relationship changes
