# Phase 0: Foundation

**Version:** v1.0 | **Epics:** 1 | **Features:** 8 | **Stories:** 33
**Dependencies:** None (first phase)

---

### Epic E-01: Project Foundation & Infrastructure

> Establish the project scaffolding, CI/CD pipelines, and base infrastructure required for all subsequent development.

#### Feature E-01.F-01: Project Scaffolding

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-01.F-01.S-01 | As a developer, I need a .NET solution structure with clean architecture layers (API, Domain, Application, Infrastructure) so that the codebase is maintainable from day one | Solution builds successfully; layers have proper project references; no circular dependencies |
| E-01.F-01.S-02 | As a developer, I need a React frontend project with TypeScript, routing, and build tooling configured so that UI development can begin | `npm run build` succeeds; TypeScript strict mode enabled; routing configured; dev server starts |
| E-01.F-01.S-03 | As a developer, I need Docker Compose for local development (API, PostgreSQL, Elasticsearch, KeyCloak) so that the full stack runs locally | `docker compose up` starts all services; API connects to all dependencies; health checks pass |

#### Feature E-01.F-02: CI/CD Pipeline

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-01.F-02.S-01 | As a developer, I need a CI pipeline that builds, tests, and lints on every push so that code quality is enforced automatically | Pipeline triggers on push; runs build, unit tests, linting; fails on errors |
| E-01.F-02.S-02 | As a developer, I need a CD pipeline that deploys to a staging environment on merge to main so that changes are validated before production | Merge to main triggers deploy; staging environment updated; smoke tests pass |

#### Feature E-01.F-03: Database Foundation

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-01.F-03.S-01 | As a developer, I need a multi-tenant database schema with tenant isolation so that each organization's data is separated | Tenant ID on all tables; row-level security or schema-per-tenant enforced; cross-tenant queries impossible |
| E-01.F-03.S-02 | As a developer, I need a database migration framework configured so that schema changes are versioned and repeatable | Migrations run on startup; rollback supported; migration history tracked |
| E-01.F-03.S-03 | As a developer, I need an append-only audit log table with tamper-evident design so that MiFID II compliance is met from day one | Audit table is insert-only (no UPDATE/DELETE); stores actor, action, timestamp, previous state; indexed for query performance; covers all write operations including entity CRUD, relationship changes (create/modify/delete/promote/demote), policy changes, and configuration changes |

#### Feature E-01.F-04: Authentication & Authorization

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-01.F-04.S-01 | As a developer, I need KeyCloak configured with OIDC so that users can authenticate via SSO | KeyCloak realm configured; OIDC discovery endpoint accessible; token issuance works |
| E-01.F-04.S-02 | As a developer, I need JWT validation middleware in the API so that all endpoints are secured | Requests without valid JWT return 401; expired tokens rejected; tenant claim extracted from token |
| E-01.F-04.S-03 | As a developer, I need RBAC with roles (Org Admin, Team Admin, Member, Viewer, Service Account) so that permissions are enforced | Each role has defined permission set; unauthorized actions return 403; role assignment works per org |
| E-01.F-04.S-04 | As a user, I want to log in via the web UI using my organization's SSO provider so that I don't need a separate password | Login redirects to KeyCloak; SSO flow completes; user lands on dashboard with correct org context |

#### Feature E-01.F-05: Data Retention & Compliance Infrastructure

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-01.F-05.S-01 | As an operator, I need a data retention engine that purges data older than the configured retention period so that storage is managed and GDPR is respected | Default 180-day retention applied; MiFID II tenants retain 5 years; purge jobs run on schedule |
| E-01.F-05.S-02 | As an operator, I need a tenant-level compliance flag (MiFID II regulated) so that stricter retention rules are automatically applied | Flag toggleable per tenant; all retention policies adjust based on flag; audit log confirms flag changes |
| E-01.F-05.S-03 | As a tenant admin, I want to export all my organization's data in JSON/CSV format so that I can fulfill data portability requests (GDPR) | Export endpoint generates complete data dump; all entity types included; download available within reasonable time |
| E-01.F-05.S-04 | As a tenant admin, I want full deletion of all my organization's data on account termination (GDPR right to erasure) so that no data remains after we leave | Cascade deletion across all data stores (PostgreSQL, Elasticsearch, blob storage); confirmation workflow; deletion certificate generated; irreversible after confirmation |
| E-01.F-05.S-05 | As a user, I want clear consent flows during registration and data collection so that GDPR consent requirements are met | Consent checkboxes on registration; consent records stored; consent withdrawable; data collection limited to consented purposes |
| E-01.F-05.S-06 | As an operator, I need a breach notification workflow (72-hour capability) so that GDPR breach reporting obligations can be met | Breach report template; affected tenant identification; notification dispatch within 72 hours; DPO contact published in UI and legal pages |
| E-01.F-05.S-07 | As an operator, I need all system-generated notifications and status updates retained as communication records so that MiFID II requirements are met for regulated tenants | All outbound notifications (email, webhook, in-app) logged with full content; retention follows MiFID II schedule (5 years) for flagged tenants; records queryable for audit |
| E-01.F-05.S-08 | As an operator, I need data residency tracking per tenant so that I can report where each tenant's data is stored | Tenant record includes data residency region; visible to tenant admins; audit log for residency changes |

#### Feature E-01.F-06: Platform API Infrastructure

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-01.F-06.S-01 | As a developer, I need an API versioning strategy (URL or header-based) so that breaking changes don't affect existing consumers | Versioning scheme implemented (e.g., /api/v1/); version negotiation documented; old version supported during deprecation window |
| E-01.F-06.S-02 | As an operator, I need per-tenant rate limiting on all API endpoints so that no single tenant can degrade the platform for others | Rate limits configurable per tenant; 429 responses with Retry-After header; rate limit headers on all responses; burst allowance |
| E-01.F-06.S-03 | As a developer, I need bulk operation endpoints (batch create/update/delete) so that large catalog operations are efficient | Batch endpoints for entity CRUD; max batch size enforced; partial success handling (report per-item errors); transactional where possible |
| E-01.F-06.S-04 | As a developer, I need webhook registration and management endpoints so that external systems can subscribe to catalog events | CRUD for webhook subscriptions; event type filtering; secret-based payload signing (HMAC); delivery status tracking |
| E-01.F-06.S-05 | As a developer, I need outbound webhooks to retry with exponential backoff and dead-letter failed deliveries so that webhook consumers don't miss events | Retry logic: 3 attempts with exponential backoff; failed deliveries logged; dead letter queue queryable; manual replay capability |
| E-01.F-06.S-06 | As a developer, I need Kartova's own API documented via an auto-generated OpenAPI spec so that API consumers have accurate reference documentation (dogfooding our own doc rendering) | OpenAPI spec auto-generated from ASP.NET Core endpoints; served at /api/docs; rendered using Kartova's own documentation engine |

#### Feature E-01.F-07: Platform Observability

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-01.F-07.S-01 | As an operator, I need health check endpoints for all platform services so that K8s liveness/readiness probes work and uptime is monitorable | /health/live and /health/ready endpoints; checks DB, Elasticsearch, KeyCloak connectivity; returns degraded status per dependency |
| E-01.F-07.S-02 | As an operator, I need structured logging across all platform components so that issues can be diagnosed | Structured JSON logs; correlation IDs across requests; tenant context in all log entries; log level configurable per component |
| E-01.F-07.S-03 | As an operator, I need platform metrics emitted (request latency, error rates, queue depths, scan durations) so that SLA compliance is measurable | Prometheus-compatible metrics endpoint; p50/p95/p99 latency; error rate per endpoint; dashboard-ready |
| E-01.F-07.S-04 | As an operator, I need alerting on platform failures and SLA breaches so that issues are caught before customers notice | Alert rules for: error rate spike, latency > 200ms p95, service unavailability; notification to ops team |

#### Feature E-01.F-08: Performance & Scalability Baseline

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-01.F-08.S-01 | As a developer, I need a database indexing strategy designed for multi-tenant scale (1000+ tenants, millions of entities) so that queries perform at target latency | Indexes defined for all common query patterns; tenant_id included in composite indexes; query plan analysis for key queries; p95 < 200ms for catalog reads |
| E-01.F-08.S-02 | As a developer, I need an Elasticsearch index strategy (per-tenant vs shared with tenant filtering) so that search scales with the tenant count | Index strategy documented and implemented; search p95 < 500ms at target scale; index lifecycle management for retention |
| E-01.F-08.S-03 | As a developer, I need the multi-tenant database isolation strategy to be row-level security (not schema-per-tenant) so that it scales to 1000+ tenants | RLS policies on all tables; tenant_id automatically injected in queries; cross-tenant access impossible; verified with integration tests |
