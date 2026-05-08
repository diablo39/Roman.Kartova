---
platform: Kartova
description: SaaS service catalog and developer portal platform (Backstage + Compass + Statuspage)
adr_count: 97
last_updated: 2026-05-08
architecture:
  backend: .NET 10 (LTS) / ASP.NET Core + EF Core (ADR-0027)
  backend_pattern: Modular monolith (ADR-0082) with Clean Architecture per module — Domain / Application / Infrastructure / Contracts (ADR-0028); inter-module via Wolverine mediator or Kafka events
  module_boundaries: NetArchTest fitness functions enforce no cross-module internal references; only `{Module}.Contracts` is public (ADR-0082)
testing:
  strategy: five-tier pyramid — architecture (NetArchTest) + unit (MSTest v4) + integration (Testcontainers) + contract (Pact) + E2E (Playwright) (ADR-0083 superseded by ADR-0097)
  architecture_tests: mandatory CI gate, fail-fast, enforce layers + module boundaries + forbidden deps (ADR-0083)
dev_workflow:
  frontend_verification: Playwright MCP mandatory for AI-assisted frontend changes — navigate, click, snapshot, check console errors before claiming done (ADR-0084)
  frontend_design_source: local files in `docs/ui-screens/{screen}/{code.html, screen.png}` are canonical (committed Stitch snapshot); Stitch MCP used as escalation for missing screens or live sync (ADR-0087)
  frontend_loop: local mockup files (default) or Stitch MCP (escalation) → implementation (map HTML to shadcn/ui per ADR-0088) → Playwright MCP (verify) → commit
operations:
  migrations: EF Core migrations run via dedicated `Kartova.Migrator` container — K8s pre-install/pre-upgrade Helm Job or Docker init container; never at app startup (ADR-0085)
  helm_chart: Co-located in application repo at `deploy/helm/kartova/`; published to OCI registry on release (ADR-0086)
  frontend: React SPA + TypeScript strict, Vite, React Router, TanStack Query (ADR-0039); UI primitives via shadcn/ui + Tailwind CSS v4 (ADR-0088); TanStack Table, react-hook-form + zod, cmdk, sonner, Recharts, React Flow, lucide-react, motion
  api_style: REST with cursor pagination and consistent error envelope (ADR-0029)
  api_versioning: URL-based primary (/api/v1/...), optional Accept-Version header (ADR-0030)
  api_docs: OpenAPI 3.x auto-generated, self-rendered inside Kartova docs engine (ADR-0034)
  database: PostgreSQL 18+ with Row-Level Security (ADR-0001, ADR-0012)
  search: Elasticsearch shared-index with per-tenant routing and filtered aliases (ADR-0002, ADR-0013)
  messaging: Apache Kafka via Strimzi on K8s, KRaft mode — Wolverine outbound + KafkaFlow inbound over Confluent.Kafka (ADR-0003, ADR-0080, ADR-0081)
  mediator: Wolverine (CQRS mediation + transactional outbox), MediatR not used (ADR-0080)
  blob_storage: S3 abstraction with MinIO default, per-tenant prefixes (ADR-0004)
  identity: KeyCloak self-hosted on K8s, OIDC / JWT, single realm (ADR-0006, ADR-0007)
  deployment: Kubernetes, cloud-agnostic (no cloud-specific managed services) (ADR-0022)
  local_dev: Docker Compose with PG / ES / KeyCloak (ADR-0024)
  ci_cd: CI on push, CD to staging on merge, prod manual via tagged release (ADR-0025)
  status_page: Separate K8s cluster, independent async replica, own ingress (ADR-0005, ADR-0023)
multi_tenancy:
  model: one-organization-one-tenant (ADR-0011)
  isolation: PostgreSQL RLS + EF Core global filters + Elasticsearch tenant routing (ADR-0012, ADR-0013)
  tenant_context: tenant_id JWT claim propagated to SET app.current_tenant (ADR-0014)
  rbac: five fixed roles — Org Admin, Editor, Contributor, Viewer, Service Account (ADR-0008)
compliance:
  - GDPR from day one — right to erasure, export, consent, residency, DPA (ADR-0015)
  - MiFID II per-tenant opt-in (mifid_ii_enabled flag) triggering 5-year retention (ADR-0016)
  - Default 180-day retention, 5 years for MiFID tenants (ADR-0017)
  - Append-only tamper-evident audit log with hash chaining, no UPDATE/DELETE grants (ADR-0018)
  - Soft delete with 30-day purge window (ADR-0019)
  - Cold-storage archival after active retention (Glacier / Azure Archive / GCS Archive) (ADR-0020)
  - Per-tenant residency_region field, EU for MVP (ADR-0021)
  - Notification log qualifies as MiFID II communication record (ADR-0050)
  - No secrets stored, references only (ADR-0078)
security:
  encryption_at_rest: storage-level AES-256 baseline + column-level encryption for OAuth tokens (ADR-0077)
  encryption_in_transit: TLS 1.2+ mandatory, TLS 1.3 preferred (ADR-0077)
  mtls: not used anywhere (agent, webhooks, service-to-service) (ADR-0042, ADR-0033, ADR-0077)
  secrets_policy: references only, never stored values (ADR-0078)
agent:
  language: .NET with AOT compilation, linux-x64 / linux-arm64 / windows-x64 (ADR-0041)
  protocol: HTTPS polling of heartbeat endpoint, no gRPC / WebSockets (ADR-0042)
  auth: long-lived bearer tokens with manual dual-token rotation (ADR-0042)
  config: centrally managed, agent polls ~60s, hot-reload (ADR-0044)
  deployment: Docker image + official Helm chart + standalone binary (ADR-0043)
  discovery: agent-discovered services require admin approval (ADR-0045)
api_contract:
  rate_limiting: per-tenant token-bucket, HTTP 429 with Retry-After / X-RateLimit-* (ADR-0031)
  bulk_ops: partial success with max batch size ~500 (ADR-0032)
  webhooks: HMAC-SHA256 signing, Kafka retry + DLQ, idempotency, per-subscriber rate limit (ADR-0033)
  git: GitHub / GitLab / Bitbucket via OAuth, three-layer integration (ADR-0035, ADR-0057)
  schema_registry: Confluent Schema Registry + Apicurio integrations (ADR-0037)
  plugins: deferred to v2.0+, MVP extensibility via webhooks + REST (ADR-0038)
notifications:
  engine: unified multi-channel dispatch (ADR-0047)
  channels: in-app, email, webhook, Slack (Block Kit), Teams (Adaptive Cards), RSS, SMS (ADR-0047, ADR-0048)
  email: configurable SMTP / SendGrid / SES adapters (ADR-0049)
  status_page_subscribers: email (double opt-in), SMS, webhook, RSS (ADR-0051)
scan_import:
  depth: deep scan at import + scheduled rescan — language, frameworks, deps, APIs, infra (ADR-0054)
  resilience: 5-min per-repo timeout, exponential retry, rate-limit aware (ADR-0055)
  conflict_policy: manual always wins over auto-discovered, conflicts queued (ADR-0056)
observability:
  logs: structured JSON with timestamp / level / service / tenant_id / correlation_id / event (ADR-0058)
  metrics: Prometheus /metrics endpoint with HTTP / dependency / business / runtime (ADR-0059)
  integrations: Prometheus + Grafana Cloud mandatory MVP (ADR-0036)
  health_checks: /health/live, /health/ready, /health/startup via ASP.NET Core HealthChecks (ADR-0060)
scale_targets:
  tenants: 1000+ (ADR-0074)
  services_per_tenant: 10000 (ADR-0074)
  users_per_tenant: 5000 (ADR-0074)
  slo_p95_api_read: <200ms (ADR-0075)
  slo_p95_api_write: <500ms (ADR-0075)
  slo_p95_search: <1s (ADR-0075)
  slo_p95_graph: <2s (ADR-0075)
slas:
  platform: 99.9% (~8.7h/yr) (ADR-0076)
  status_page: 99.99% (~52min/yr) (ADR-0076)
pricing:
  model: 4-tier — Free / Starter / Pro / Enterprise, per user/month, minimum seats on paid (ADR-0061)
  metering: per-user active count per billing period (ADR-0063)
  provider: external Stripe-style; Kartova owns metering + entitlements (ADR-0062)
domain_model:
  entity_types: 9 fixed + JSONB custom_attributes (MVP); 10th Custom Entity in Phase 2 (ADR-0064)
  org_structure: hybrid hierarchy (Org → Team → System → Component) + cross-cutting tags (ADR-0065)
  ownership: multi-team with platform flag + quorum approval (ADR-0066)
  relationships: fixed 7-type vocabulary (depends-on / owns / consumes / produces / exposes / implements / part-of) (ADR-0068)
  relationship_origin: manual | scan | agent, immutable (ADR-0067)
  required_fields: owner, lifecycle, etc. enforced on all creation paths (ADR-0069)
  scorecards: per-org configurable rule sets, weighted (ADR-0070)
  maturity_model: L1 Registered → L2 Documented → L3 Monitored → L4 Resilient → L5 Optimized (ADR-0071)
  tags: system-defined categories + tenant-extensible custom (ADR-0072)
  lifecycle: Active → Deprecated → Retired, mandatory (ADR-0073)
cli:
  distribution: dotnet tool install -g kartova + standalone AOT binary (ADR-0046)
  auth: kartova auth --token exchanges service-account token for JWT (ADR-0009)
gtm: dogfooding → design partners → public GA (ADR-0079)
open_source_strategy: fully proprietary, no OSS core / source-available (ADR-0026)
---

# Architecture Decision Records — Kartova

**Status:** Living document
**Last updated:** 2026-05-08
**Total accepted:** 97
**Convention:** Michael Nygard template (Status / Context / Decision / Rationale / Alternatives / Consequences / References)

## How to use this index

LLM agents and humans can scan the table below to identify ADRs relevant to a topic. Each row links to the full ADR file. For candidate/pending state, see [ADR-CANDIDATES.md](../ADR-CANDIDATES.md) (all resolved as of 2026-04-21).

## Index

| ID | Title | Category | Status | Related | Summary |
|----|-------|----------|--------|---------|---------|
| [0001](ADR-0001-postgresql-as-primary-database.md) | PostgreSQL as Primary Database | Data Platform | Accepted | 0012, 0018 | Use PostgreSQL (v16+) as the primary transactional datastore for all tenant data, with RLS and `tenant_id` for isolation. |
| [0002](ADR-0002-elasticsearch-for-search.md) | Elasticsearch for Search | Data Platform | Accepted | 0001, 0013 | Use Elasticsearch as the search engine for catalog, docs, and faceted filtering; PostgreSQL remains system of record. |
| [0003](ADR-0003-apache-kafka-via-strimzi-on-kubernetes.md) | Apache Kafka via Strimzi on Kubernetes | Data Platform / Messaging | Accepted | 0001, 0022, 0037, 0041, 0047, 0080, 0081 | Use Apache Kafka via Strimzi Operator in KRaft mode (3-broker minimum) as the event bus, accessed via Wolverine (outbound, ADR-0080) + KafkaFlow (inbound, ADR-0081) over Confluent.Kafka. |
| [0004](ADR-0004-s3-compatible-blob-storage-with-minio-default.md) | S3-Compatible Blob Storage with MinIO Default | Data Platform / Storage | Accepted | 0001, 0015, 0020, 0022, 0026 | Use S3-compatible blob storage via an `IBlobStorage` abstraction, default MinIO on K8s, shared bucket with per-tenant prefixes. |
| [0005](ADR-0005-independent-data-replica-for-status-page.md) | Independent Data Replica for Status Page | Data Platform / HA | Accepted | 0023, 0076 | Status page reads from an independent, one-way asynchronously replicated data replica holding only status-page-relevant data. |
| [0006](ADR-0006-keycloak-as-identity-provider.md) | KeyCloak as Identity Provider | Authentication & Authorization | Accepted | 0007, 0008, 0009, 0010 | Self-host KeyCloak on Kubernetes as the OIDC identity provider; one realm for all Kartova tenants, tenant assigned via JWT claim. |
| [0007](ADR-0007-jwt-oidc-for-api-and-cli-auth.md) | JWT (OIDC) for API and CLI Auth | Authentication & Authorization | Accepted | 0006, 0009, 0014 | Use short-lived (~15 min) OIDC JWT access tokens from KeyCloak for all clients; ASP.NET Core validates locally via JWKS. |
| [0008](ADR-0008-five-fixed-rbac-roles.md) | Five Fixed RBAC Roles | Authentication & Authorization | Accepted | 0006, 0007 | Define five fixed per-organization roles: Org Admin, Editor, Contributor, Viewer, and Service Account. |
| [0009](ADR-0009-service-account-jwt-model-for-cicd.md) | Service Account JWT Model for CI/CD | Authentication & Authorization | Accepted | 0007, 0008 | Service accounts are first-class KeyCloak principals authenticating via `kartova auth --token`, exchanging for JWTs with a `service_account` role. |
| [0010](ADR-0010-internal-status-page-auth-via-keycloak.md) | Internal Status Page Auth via KeyCloak | Authentication & Authorization | Accepted | 0005, 0006, 0023 | Internal status pages authenticate via KeyCloak OIDC with cached JWKS; public status pages remain unauthenticated. |
| [0011](ADR-0011-one-organization-equals-one-tenant.md) | One Organization Equals One Tenant | Multi-Tenancy | Accepted | 0012, 0061 | Each organization is a single tenant — one billing entity, one RBAC scope, one residency record, one GDPR boundary. |
| [0012](ADR-0012-postgresql-row-level-security-for-tenant-isolation.md) | PostgreSQL Row-Level Security for Tenant Isolation | Multi-Tenancy | Accepted | 0001, 0011, 0014, 0074, 0075 | Enforce tenant isolation via PostgreSQL RLS with `tenant_id` column and `SET LOCAL app.current_tenant_id`, plus EF Core global filters as defense-in-depth. |
| [0013](ADR-0013-elasticsearch-shared-index-with-tenant-routing.md) | Elasticsearch Shared Index with Tenant Routing | Multi-Tenancy / Search | Accepted | 0002, 0012, 0015, 0017, 0074, 0075 | Use shared ES indexes per type with `tenant_id` field, routing by tenant, per-tenant filtered aliases, and ILM retention tiers. |
| [0014](ADR-0014-tenant-claim-extracted-from-jwt.md) | Tenant Claim Extracted from JWT | Multi-Tenancy | Accepted | 0007, 0012 | Read the tenant ID exclusively from the validated JWT `tenant_id` claim and propagate it to `SET app.current_tenant` for RLS enforcement. |
| [0015](ADR-0015-gdpr-compliance-from-day-one.md) | GDPR Compliance From Day One | Compliance & Retention | Accepted | 0016, 0019, 0021, 0078 | Implement right-to-erasure across Postgres/ES/blob, data export, consent records, residency disclosure, and DPA from MVP. |
| [0016](ADR-0016-mifid-ii-compliance-from-day-one.md) | MiFID II Compliance From Day One | Compliance & Retention | Accepted | 0015, 0017, 0018, 0050 | Every tenant carries a `mifid_ii_enabled` flag triggering 5-year retention of audit log, notifications, and communications. |
| [0017](ADR-0017-default-180-day-retention-5-year-mifid.md) | Default 180-Day Retention, 5-Year for MiFID | Compliance & Retention | Accepted | 0016, 0019, 0020 | Default 180-day retention for uptime/audit/scan/incident history; 5 years for tenants with MiFID II enabled. |
| [0018](ADR-0018-append-only-tamper-evident-audit-log.md) | Append-Only Tamper-Evident Audit Log | Compliance & Retention | Accepted | 0016, 0017 | Maintain an insert-only PostgreSQL audit table with no UPDATE/DELETE grants and tamper-evident hash chaining. |
| [0019](ADR-0019-soft-delete-with-30-day-purge.md) | Soft Delete with 30-Day Purge | Compliance & Retention | Accepted | 0015, 0016, 0017 | Two-phase deletion: soft-delete marks `deleted_at` and hides from UI/API; hard purge runs after a 30-day window. |
| [0020](ADR-0020-cold-storage-archival-after-active-retention.md) | Cold-Storage Archival After Active Retention | Compliance & Retention | Accepted | 0017, 0018 | After active retention expires, automatically move data to cold object storage (S3 Glacier / Azure Archive / GCS Archive) with hours-SLA retrieval. |
| [0021](ADR-0021-data-residency-tracking-per-tenant.md) | Data Residency Tracking Per Tenant | Compliance & Retention | Accepted | 0015, 0022 | Store a `residency_region` field on each tenant (EU for MVP); actual multi-region routing deferred to v2.0+. |
| [0022](ADR-0022-kubernetes-cloud-agnostic-deployment.md) | Kubernetes, Cloud-Agnostic Deployment | Platform Infrastructure | Accepted | 0023, 0043 | Deploy on Kubernetes using cloud-agnostic building blocks (standard APIs, Helm, Ingress, cert-manager); avoid cloud-specific managed services. |
| [0023](ADR-0023-status-page-as-separate-k8s-cluster.md) | Status Page as Separate K8s Cluster | Platform Infrastructure | Accepted | 0005, 0022, 0076 | Deploy the status page to a separate K8s cluster (or isolated namespace/AZ) with its own ingress, replica, and scaling. |
| [0024](ADR-0024-docker-compose-for-local-dev.md) | Docker Compose for Local Dev | Platform Infrastructure | Accepted | 0022 | Provide a repo-root `docker-compose.yml` bringing up PostgreSQL, Elasticsearch, KeyCloak, and dependencies for local development. |
| [0025](ADR-0025-ci-on-push-cd-to-staging-on-merge.md) | CI on Push; CD to Staging on Merge | Platform Infrastructure | Accepted | 0022 | CI runs on every push/PR; CD to staging is automatic on merge to main; production deploy is manual via tagged release. |
| [0026](ADR-0026-fully-proprietary-no-open-source-core.md) | Fully Proprietary — No Open-Source Core | Platform Infrastructure | Accepted | — | Kartova source is fully proprietary with no open-source core or source-available license; individual libraries may be OSS opportunistically. |
| [0027](ADR-0027-dotnet-aspnet-core-for-backend-api.md) | .NET / ASP.NET Core for Backend API | API & Integration Architecture | Accepted | 0028, 0041, 0046, 0080 | Use .NET 10 LTS + ASP.NET Core with EF Core for the backend API; standard idioms (DI, FluentValidation). CQRS mediation via Wolverine is mandatory (ADR-0080). |
| [0028](ADR-0028-clean-architecture-layering.md) | Clean Architecture Layering | API & Integration Architecture | Accepted | 0027 | Organize solution into Domain / Application / Infrastructure / API layers with enforced inward-only reference direction. |
| [0029](ADR-0029-rest-as-primary-api-style.md) | REST as Primary API Style | API & Integration Architecture | Accepted | 0030, 0032, 0034 | Use REST (resource URLs, HTTP verbs, JSON, cursor pagination, consistent error envelope) with OpenAPI auto-generated. |
| [0030](ADR-0030-url-or-header-based-api-versioning.md) | URL- or Header-Based API Versioning | API & Integration Architecture | Accepted | 0029 | Primary scheme is URL-based versioning (`/api/v1/...`); optional `Accept-Version` header; old versions supported ≥6 months after deprecation. |
| [0031](ADR-0031-per-tenant-rate-limiting.md) | Per-Tenant Rate Limiting | API & Integration Architecture | Accepted | 0029, 0032 | Apply per-tenant token-bucket rate limits returning HTTP 429 with `Retry-After`/`X-RateLimit-*` headers; limits overridable per tenant. |
| [0032](ADR-0032-bulk-api-endpoints-with-partial-success.md) | Bulk API Endpoints with Partial Success | API & Integration Architecture | Accepted | 0029, 0031, 0054 | Provide batch CRUD endpoints per entity type with per-item status (partial success) and an enforced max batch size (~500). |
| [0033](ADR-0033-hmac-signed-webhooks-with-retry-dlq-idempotency-rate-limiting.md) | HMAC-Signed Webhooks with Retry, DLQ, Idempotency, Rate Limiting | API & Integration Architecture | Accepted | 0003, 0016, 0031, 0050, 0051 | Outbound webhooks use HMAC-SHA256 signing with tenant secret, Kafka-backed retry pipeline, DLQ with replay, idempotency keys, and per-subscriber rate limiting. |
| [0034](ADR-0034-openapi-auto-generated-and-self-rendered.md) | OpenAPI Auto-Generated & Self-Rendered | API & Integration Architecture | Accepted | 0029, 0079 | Auto-generate OpenAPI 3.x from ASP.NET Core endpoints/DTOs; render it inside Kartova's own docs engine (Swagger UI only in dev/staging). |
| [0035](ADR-0035-git-as-first-class-integration.md) | Git as First-Class Integration | API & Integration Architecture | Accepted | 0054, 0057 | Ship Git integration in three layers: provider-generic clone/scan, native GitHub/GitLab/Bitbucket connectors, and OAuth-based auth. |
| [0036](ADR-0036-prometheus-grafana-cloud-integrations.md) | Prometheus + Grafana Cloud Integrations | API & Integration Architecture | Accepted | 0059 | Ship Prometheus (pull metrics, PromQL uptime rules) and Grafana Cloud integrations as mandatory MVP monitoring connectors. |
| [0037](ADR-0037-schema-registry-integrations.md) | Schema Registry Integrations | API & Integration Architecture | Accepted | 0054 | Integrate Confluent Schema Registry and Apicurio to pull live Avro/JSON/Protobuf schemas into the catalog as API-Async entities. |
| [0038](ADR-0038-plugin-architecture-deferred-to-v2.md) | Plugin Architecture Deferred to v2.0+ | API & Integration Architecture | Accepted | 0033 | No plugin SDK in MVP; extensibility is via webhooks (outbound) and REST API (inbound); plugin framework deferred to v2.0+. |
| [0039](ADR-0039-react-spa-with-typescript.md) | React SPA with TypeScript | Frontend Architecture | Accepted | 0027, 0040 | Build the web UI as a React SPA with TypeScript strict mode, using Vite, React Router, and TanStack Query for server state. |
| [0040](ADR-0040-two-view-dependency-graph-navigation.md) | Two-View Dependency Graph Navigation | Frontend Architecture | Accepted | 0039 | Provide an embedded 1-level mini-graph on every entity page plus a full-screen interactive explorer with filters and path-finding. |
| [0041](ADR-0041-dotnet-agent-with-aot-compilation.md) | .NET Agent with AOT Compilation | Agent Architecture | Accepted | 0027, 0042, 0043, 0046 | Build the agent in .NET with AOT compilation for small, self-contained native binaries targeting linux-x64, linux-arm64, windows-x64. |
| [0042](ADR-0042-agent-communication-via-https-polling-with-long-lived-tokens.md) | Agent Communication via HTTPS Polling with Long-Lived Tokens | Agent Architecture | Accepted | 0006, 0007, 0018, 0022, 0029, 0033, 0041, 0043, 0044 | Agent communicates via HTTPS/JSON polling of a heartbeat endpoint using long-lived bearer tokens with manual rotation (no gRPC/WebSockets). |
| [0043](ADR-0043-agent-deployable-as-docker-and-helm.md) | Agent Deployable as Docker / Helm | Agent Architecture | Accepted | 0022, 0041 | Primary agent shapes are a published Docker image and an official Helm chart; also distributed as a standalone binary. |
| [0044](ADR-0044-centrally-managed-agent-config-pull-based.md) | Centrally Managed Agent Config (Pull-Based) | Agent Architecture | Accepted | 0041, 0042 | Agent polls the platform (~60s) for its config document; changes hot-reload where possible; config is authored in the UI/API and versioned. |
| [0045](ADR-0045-agent-discovered-services-approval-workflow.md) | Agent-Discovered Services Require Approval Workflow | Agent Architecture | Accepted | 0041, 0042, 0044, 0067 | Agent-discovered services land in a pending-approval discovery inbox; admins explicitly promote, merge, or reject each candidate. |
| [0046](ADR-0046-dotnet-global-tool-cli-distribution.md) | .NET Global Tool & Standalone Binary CLI Distribution | CLI & Distribution | Accepted | 0007, 0027, 0041 | Distribute the CLI as both a `dotnet tool install -g kartova` global tool and a standalone AOT binary for non-.NET users. |
| [0047](ADR-0047-unified-multi-channel-notification-engine.md) | Unified Multi-Channel Notification Engine | Notification Architecture | Accepted | 0033, 0048, 0049, 0050 | A single dispatch engine handles all outbound notifications across in-app, email, webhook, Slack, Teams, RSS, and SMS channels. |
| [0048](ADR-0048-native-slack-and-teams-integrations.md) | Native Slack & Microsoft Teams Integrations | Notification Architecture | Accepted | 0047, 0057 | Ship native OAuth-installed Slack (Block Kit) and Microsoft Teams (Adaptive Cards) bot integrations with per-channel routing. |
| [0049](ADR-0049-configurable-smtp-email-provider.md) | Configurable SMTP / Email Provider | Notification Architecture | Accepted | 0047, 0050 | Email channel targets a configurable provider interface — generic SMTP default plus pluggable adapters for SendGrid/SES/etc. |
| [0050](ADR-0050-notification-log-as-mifid-ii-record.md) | Notification Log as MiFID II Communication Record | Notification Architecture / Compliance | Accepted | 0016, 0017, 0018, 0047 | Persist every outbound notification with full rendered payload in a `notification_log` table qualifying as a MiFID II communication record. |
| [0051](ADR-0051-multi-channel-status-page-subscribers.md) | Multi-Channel Status Page Subscribers | Status Page Architecture | Accepted | 0023, 0047, 0053 | Status page visitors subscribe via four channels: email (double opt-in), SMS, webhook, and RSS. |
| [0052](ADR-0052-custom-domains-with-auto-ssl.md) | Custom Domains with Auto-Provisioned SSL | Status Page Architecture | Accepted | 0023, 0053 | Tenants configure custom status-page domains; platform validates via DNS challenge and auto-provisions SSL certificates (Let's Encrypt/ACME). |
| [0053](ADR-0053-status-page-99-99-sla-target.md) | 99.99% SLA Target for Status Page | Status Page Architecture / Availability | Accepted | 0005, 0023, 0051, 0052, 0076 | Status page carries a 99.99% availability SLA (≤ ~52 min/year), one nine higher than the 99.9% platform SLA. |
| [0054](ADR-0054-deep-repository-scan-at-import.md) | Deep Repository Scan at Import-Time | Scan / Import Architecture | Accepted | 0035, 0055, 0056, 0057 | Perform a deep scan at import and on schedule that extracts language, framework, dependencies, APIs, and infrastructure files. |
| [0055](ADR-0055-scan-timeout-retry-rate-limit-aware.md) | Scan Timeout, Retry, Rate-Limit Aware | Scan / Import Architecture | Accepted | 0054, 0056, 0057 | Scans operate under explicit resilience parameters: 5-min per-repo timeout, exponential retry, and Git provider rate-limit awareness. |
| [0056](ADR-0056-manual-relationship-precedence.md) | Manual Relationship Precedence (Conflict Queue) | Scan / Import Architecture / Data Model | Accepted | 0045, 0054, 0067 | Manual data always wins over auto-discovered data; manual relationships are never overwritten by rescans, conflicts go to a queue. |
| [0057](ADR-0057-oauth-git-provider-connection.md) | OAuth-Based Git Provider Connection | Scan / Import Architecture / Integrations | Accepted | 0035, 0054, 0055, 0078 | Connect to GitHub/GitLab/Bitbucket via OAuth flows with least-privilege scopes (`repo:read`, `read:org`, webhook admin). |
| [0058](ADR-0058-structured-json-logs-with-tenant-context.md) | Structured JSON Logs with Tenant Context | Observability & Monitoring | Accepted | 0014, 0036, 0059 | All services emit structured JSON logs with mandatory fields: timestamp, level, service, tenant_id, correlation_id, and event. |
| [0059](ADR-0059-prometheus-compatible-metrics-exposition.md) | Prometheus-Compatible Metrics Exposition | Observability & Monitoring | Accepted | 0036, 0058 | Every service exposes a Prometheus-format `/metrics` endpoint with HTTP, dependency, business, and runtime instrumentation. |
| [0060](ADR-0060-three-probe-health-checks-aspnet-core-framework.md) | Three-Probe Health Check Endpoints Using ASP.NET Core Framework | Observability & Monitoring | Accepted | 0001, 0002, 0003, 0004, 0006, 0008, 0022, 0027, 0036, 0053, 0058, 0059, 0076 | Expose three K8s-semantic endpoints — `/health/live`, `/health/ready`, `/health/startup` — via ASP.NET Core HealthChecks framework. |
| [0061](ADR-0061-four-tier-pricing-model.md) | Four-Tier Pricing Model | Billing | Accepted | 0016, 0017, 0062, 0063, 0076, 0079 | Launch with four tiers — Free, Starter, Pro, Enterprise — priced per user/month with minimum seat counts on paid tiers. |
| [0062](ADR-0062-external-billing-provider.md) | External Billing Provider for Payment Processing | Billing | Accepted | 0015, 0016, 0021, 0063 | Integrate a third-party Stripe-style billing provider for payments, subscriptions, and invoicing; Kartova owns metering and entitlement. |
| [0063](ADR-0063-user-count-metering-per-billing-period.md) | User-Count Metering Per Billing Period | Billing | Accepted | 0008, 0009, 0010, 0062 | Meter each org's active user count per billing period (typically monthly) and report the count to the billing provider at close. |
| [0064](ADR-0064-entity-taxonomy-nine-fixed-plus-jsonb-custom-entity-phased.md) | Entity Taxonomy — Nine Fixed Types with JSONB Custom Attributes, Custom Entity Type Phased | Domain Model | Accepted | 0012, 0013, 0054, 0061, 0065, 0067, 0068, 0069, 0070, 0071, 0072, 0073 | Phase 1 ships nine fixed entity types with JSONB custom attributes; Phase 2 adds a tenant-defined custom entity type. |
| [0065](ADR-0065-hybrid-org-structure-hierarchy-plus-tags.md) | Hybrid Org Structure — Hierarchy + Tags | Domain Model | Accepted | 0064, 0066, 0072 | Combine a strict ownership hierarchy (Org → Team → System → Component) with cross-cutting tags for flexible grouping. |
| [0066](ADR-0066-multi-ownership-with-quorum-rules.md) | Multi-Ownership with Quorum Rules | Domain Model | Accepted | 0008, 0065, 0073 | Components support multiple owning teams with a platform flag and quorum approval rules for shared/foundational changes. |
| [0067](ADR-0067-relationship-origin-tracking.md) | Relationship Origin Tracking | Domain Model | Accepted | 0045, 0054, 0056, 0068 | Every relationship carries a required `origin` field with values `manual`, `scan`, or `agent`, immutable after creation. |
| [0068](ADR-0068-fixed-relationship-type-vocabulary.md) | Fixed Relationship Type Vocabulary | Domain Model | Accepted | 0064, 0065, 0067 | Support a fixed vocabulary of seven relationship types (depends-on, owns, consumes, produces, exposes, implements, part-of). |
| [0069](ADR-0069-required-minimum-fields-enforcement.md) | Required Minimum Fields Enforcement | Domain Model / Data Quality | Accepted | 0054, 0070, 0071 | All entity creation paths (UI, API, CLI, scan import) reject entities missing required minimum fields (owner, lifecycle, etc.). |
| [0070](ADR-0070-per-organization-scorecard-configurability.md) | Per-Organization Scorecard Configurability | Domain Model / Quality | Accepted | 0038, 0069, 0071 | Scorecards are configurable per organization as named rule sets grouped into categories with weighted scoring. |
| [0071](ADR-0071-five-level-maturity-model.md) | Five-Level Maturity Model | Domain Model / Quality | Accepted | 0069, 0070 | Define a five-level monotonic maturity model: L1 Registered, L2 Documented, L3 Monitored, L4 Resilient, L5 Optimized. |
| [0072](ADR-0072-tag-taxonomy-predefined-plus-custom.md) | Tag Taxonomy — Predefined Plus Custom | Domain Model | Accepted | 0065, 0067, 0070 | Tags are organized into system-defined categories with predefined values plus tenant-extensible custom categories and values. |
| [0073](ADR-0073-enforced-entity-lifecycle-states.md) | Enforced Entity Lifecycle States | Domain Model | Accepted | 0018, 0019, 0066 | Entities carry a mandatory `lifecycle` field progressing linearly through three states: Active, Deprecated, Retired. |
| [0074](ADR-0074-scale-targets-1000-tenants.md) | Scale Targets — 1000+ Tenants, 10k Services/Tenant | Scale & Performance | Accepted | 0001, 0002, 0012, 0075, 0076 | Design envelope is 1000+ tenants, 10k services per tenant, 5k users per tenant, millions of entities on a single deployment. |
| [0075](ADR-0075-performance-slos-p95-latency.md) | Performance SLOs — p95 Latency Targets | Scale & Performance | Accepted | 0001, 0002, 0055, 0074, 0076 | Platform p95 latency SLOs: <200ms API reads, <500ms writes, <1s search, <2s graph queries within the scale envelope. |
| [0076](ADR-0076-two-tier-sla-platform-99-9-status-99-99.md) | Two-Tier SLA — Platform 99.9% / Status Page 99.99% | Scale & Performance / Availability | Accepted | 0005, 0023, 0053, 0074, 0075 | Two-tier availability SLA: 99.9% for the main platform (~8.7h/yr budget) and 99.99% for the status page (~52min/yr). |
| [0077](ADR-0077-encryption-storage-baseline-plus-oauth-column-and-tls-1-2-plus.md) | Encryption at Rest (Storage Baseline + OAuth Column Encryption) and TLS 1.2+ in Transit | Non-Functional / Cross-Cutting | Accepted | 0001, 0002, 0003, 0004, 0012, 0015, 0016, 0018, 0022, 0033, 0042, 0057, 0078 | Encryption at rest via storage-baseline AES-256 plus column-level encryption for OAuth tokens; TLS 1.2+ mandatory in transit. |
| [0078](ADR-0078-no-secrets-stored-references-only.md) | No Secrets or Credentials Stored — References Only | Non-Functional / Security | Accepted | 0015, 0018, 0054, 0057, 0077 | Kartova never stores secret or credential values; the catalog stores only references, names, and structural metadata. |
| [0079](ADR-0079-dogfooding-design-partners-gtm.md) | Dogfooding + Design Partners Go-to-Market Strategy | Non-Functional / Go-to-Market | Accepted | 0025, 0026, 0074 | Three-phase GTM: (1) dogfooding from Phase 0, (2) design partners, (3) public GA, with each phase gating feature scope. |
| [0080](ADR-0080-wolverine-for-mediation-and-outbound-messaging.md) | Wolverine for In-Process Mediation and Outbound Messaging | Backend Architecture | Accepted | 0003, 0027, 0028, 0033, 0047, 0081 | Wolverine as single library for CQRS mediation, outbound Kafka publishing, transactional outbox, and future sagas. MediatR and MassTransit not used. |
| [0081](ADR-0081-kafkaflow-for-inbound-kafka-consumers.md) | KafkaFlow for Inbound Kafka Consumers | Backend Architecture | Accepted | 0003, 0027, 0037, 0074, 0080 | KafkaFlow for all inbound consumers to get per-key parallel-within-partition workers; Wolverine (ADR-0080) handles outbound. |
| [0082](ADR-0082-modular-monolith-architecture.md) | Modular Monolith Architecture | Backend Architecture | Accepted | 0003, 0012, 0027, 0028, 0080, 0081 | Modular monolith with one bounded-context module per domain area, Clean Architecture per module, enforced boundaries via NetArchTest; inter-module only via Wolverine or Kafka. |
| [0083](ADR-0083-testing-strategy-with-architecture-tests.md) | Testing Strategy — Test Pyramid with Architecture Tests as CI Gate | Testing & Quality | Superseded by 0097 | 0025, 0028, 0080, 0082, 0084 | Five-tier pyramid: architecture (NetArchTest, mandatory), unit (xUnit), integration (Testcontainers), contract (Pact), E2E (Playwright). Architecture tests enforce module boundaries + layering + forbidden deps. |
| [0084](ADR-0084-playwright-mcp-for-frontend-development.md) | Playwright MCP for Frontend Development and Verification Workflow | Development Workflow | Accepted | 0039, 0079, 0083 | Playwright MCP mandatory for AI-assisted frontend verification — navigate, interact, snapshot, check console errors before declaring work done. Complementary to ADR-0083 E2E tier. |
| [0085](ADR-0085-database-migrations-as-k8s-jobs-docker-init-containers.md) | Database Migrations as K8s Jobs and Docker Init Containers | Deployment & Operations | Accepted | 0001, 0022, 0024, 0025, 0074, 0082, 0086 | EF Core migrations run in dedicated `Kartova.Migrator` container via Helm pre-install/pre-upgrade Job (K8s) or init container (Docker); never at app startup. Per-module orchestration. |
| [0086](ADR-0086-helm-chart-co-located-in-application-repository.md) | Helm Chart Co-located in Application Repository | Deployment & Operations | Accepted | 0022, 0024, 0025, 0043, 0082, 0085 | Helm chart lives at `deploy/helm/kartova/` in-repo, versioned with the app, published as OCI artifact to GHCR on release. Agent chart remains separate (ADR-0043). |
| [0087](ADR-0087-google-stitch-mcp-as-design-source.md) | Google Stitch MCP as Design Source for Frontend Implementation | Development Workflow | Accepted | 0039, 0083, 0084, 0094 | Google Stitch MCP mandatory as canonical design source — query mockup before implementing any screen; pairs with ADR-0084 Playwright MCP to form full Stitch → code → verify loop. |
| [0088](ADR-0088-shadcn-ui-component-library-stack.md) | React Component Library — shadcn/ui + Tailwind Stack for Frontend Primitives | Frontend Architecture | Superseded by 0094 | 0039, 0040, 0084, 0087 | ~~shadcn/ui + Tailwind CSS v4 + Radix primitives~~ Superseded by ADR-0094 (Untitled UI free-tier). |
| [0089](ADR-0089-slnx-solution-file-format.md) | Use `.slnx` Solution File Format (Not Classic `.sln`) | Backend Architecture | Accepted | 0027, 0028, 0082 | Adopt `.slnx` (XML, .NET 10 SDK default) over classic `.sln` for cleaner git diffs at scale (40+ csprojs), forward toolchain direction. `dotnet sln migrate` available if reversal ever needed. |
| [0090](ADR-0090-tenant-scope-mechanism.md) | Tenant Scope Mechanism — Transaction-Bound `SET LOCAL` with Shared Connection per Request | Multi-Tenancy | Accepted | 0006, 0011, 0012, 0014, 0080, 0082 | `ITenantScope` owns one connection + tx per request; `SET LOCAL app.current_tenant_id` at Begin; commit via transport adapter before response/ack. All module DbContexts share the scope's connection + enlist in the tx. |
| [0091](ADR-0091-problem-details-for-error-responses.md) | RFC 7807 Problem Details for All HTTP Error Responses | API & Integration Architecture | Accepted | 0029, 0034, 0058 | All HTTP error responses use `application/problem+json` per RFC 7807 with `type`/`title`/`status`/`detail`/`instance`/`traceId` fields; validation errors extend with `errors` map. ASP.NET `AddProblemDetails()`. |
| [0092](ADR-0092-rest-api-url-convention.md) | REST API URL Convention — Module-Prefixed with Admin-First and Skip Rule | API & Integration Architecture | Accepted | 0029, 0034, 0082, 0090 | Routes live at `/api/v1/<module-slug>/<collection>` with `/api/v1/admin/<module-slug>/...` for admin-only, and the module segment collapses when slug equals plural primary collection (e.g., `/api/v1/organizations/me`). Enforced by `MapTenantScopedModule(slug)` / `MapAdminModule(slug)` helpers + new `IModuleRules` arch test. |
| [0093](ADR-0093-wolverine-scope-narrowed.md) | Wolverine Scope — Outbox/Async Only, Direct Dispatch for Sync HTTP | Backend Architecture | Accepted | 0028, 0080, 0081, 0090 | Narrows ADR-0028. Sync HTTP handlers dispatch directly from endpoint delegates (share request scope with `TenantScopeBeginMiddleware`); Wolverine remains mandatory for transactional outbox, async messaging, and Kafka outbound. `WolverineFx.Http` evaluation deferred until post-slice-6. |
| [0094](ADR-0094-untitled-ui-component-library.md) | Untitled UI Free-Tier as Primary UI Primitive Layer | Frontend Architecture | Accepted | 0039, 0040, 0084, 0087, 0088 | Untitled UI free-tier (react-aria-components + Tailwind CSS v4) + @untitledui/icons as primary UI primitive layer; supersedes ADR-0088 (shadcn/ui). |
| [0095](ADR-0095-cursor-pagination-contract.md) | Cursor Pagination Contract — Wire Shape, Sort Syntax, and First-Cut Mandate | API & Integration Architecture | Accepted | 0029, 0083, 0090, 0091, 0092 | List endpoints return `CursorPage<T>` envelope with opaque base64url cursor `{s,i,d}`; `?sortBy=<field>&sortOrder=asc\|desc` per-resource enum allowlist; default 50, max 200; pure cursor (no total). First-cut mandate enforced by `PaginationConventionRules` arch test; `[BoundedListResult]` opt-out for bounded lists. |
| [0096](ADR-0096-rest-verb-policy.md) | REST Verb Policy — PUT for Full Replacement, POST for Actions, No PATCH | API & Integration Architecture | Accepted | 0029, 0073, 0091, 0092, 0095 | `PUT /resources/{id}` for idempotent full-resource replacement on small/stable DTOs; `POST /resources/{id}/<action>` for named domain commands (deprecate, decommission, restore, transfer-ownership); `PATCH` forbidden (semantics drift, missing-vs-null ambiguity, uneven codegen). Enforced by `RestVerbPolicyRules` arch test. |
| [0097](ADR-0097-mstest-supersedes-xunit.md) | MSTest v4 supersedes xUnit | Testing & Quality | Accepted | 0028, 0080, 0082, 0083, 0084, 0095 | Replaces xUnit + FluentAssertions with MSTest v4 native assertions across all 10 xUnit-using test projects. Project SDK, VSTest runner, `coverlet.collector`, and Stryker per-module orchestration all unchanged. MTP deferred — Stryker.NET 4.14.1 does not support it (stryker-net#3094). Migration tracked in `docs/superpowers/specs/2026-05-08-xunit-to-mstest-migration-design.md`. |

## By category (quick navigation)

- **Data Platform**: 0001, 0002, 0003, 0004, 0005
- **Authentication & Authorization**: 0006, 0007, 0008, 0009, 0010
- **Multi-Tenancy**: 0011, 0012, 0013, 0014, 0090
- **Compliance & Retention**: 0015, 0016, 0017, 0018, 0019, 0020, 0021, 0050
- **Platform Infrastructure**: 0022, 0023, 0024, 0025, 0026
- **API & Integration Architecture**: 0027, 0028, 0029, 0030, 0031, 0032, 0033, 0034, 0035, 0036, 0037, 0038, 0091, 0092, 0095, 0096
- **Backend Architecture**: 0080, 0081, 0082, 0089, 0093
- **Frontend Architecture**: 0039, 0040, 0088
- **Agent Architecture**: 0041, 0042, 0043, 0044, 0045
- **CLI & Distribution**: 0046
- **Notification Architecture**: 0047, 0048, 0049, 0050
- **Status Page Architecture**: 0051, 0052, 0053
- **Scan / Import Architecture**: 0054, 0055, 0056, 0057
- **Observability & Monitoring**: 0058, 0059, 0060
- **Billing**: 0061, 0062, 0063
- **Domain Model**: 0064, 0065, 0066, 0067, 0068, 0069, 0070, 0071, 0072, 0073
- **Scale & Performance**: 0074, 0075, 0076
- **Non-Functional / Cross-Cutting**: 0077, 0078, 0079
- **Testing & Quality**: 0083, 0097
- **Development Workflow**: 0084, 0087
- **Deployment & Operations**: 0085, 0086

## By common topic (LLM helper tags)

- **Multi-tenancy isolation**: 0011, 0012, 0013, 0014, 0031
- **Encryption / security**: 0018, 0042, 0057, 0077, 0078
- **Notifications / webhooks**: 0033, 0047, 0048, 0049, 0050, 0051, 0080, 0081
- **Messaging / mediation / CQRS**: 0003, 0028, 0037, 0080, 0081, 0093
- **Modular monolith / bounded contexts**: 0028, 0080, 0081, 0082, 0089
- **Solution file format / tooling**: 0082, 0089
- **Testing / quality gates**: 0025, 0083
- **Frontend workflow / dev-time verification**: 0039, 0083, 0084, 0087
- **AI-assisted development**: 0079, 0084, 0087
- **Design source / mockups**: 0039, 0087
- **Agent architecture**: 0041, 0042, 0043, 0044, 0045, 0067
- **API contract**: 0029, 0030, 0031, 0032, 0033, 0034
- **Compliance (GDPR / MiFID II)**: 0015, 0016, 0017, 0018, 0019, 0020, 0021, 0050, 0078
- **Retention / archival / deletion**: 0017, 0019, 0020, 0073
- **Audit & logging**: 0018, 0050, 0058
- **Domain model**: 0064, 0065, 0066, 0067, 0068, 0069, 0070, 0071, 0072, 0073
- **Scale & performance**: 0013, 0031, 0074, 0075, 0076
- **Availability & SLA**: 0005, 0023, 0053, 0076
- **Billing & pricing**: 0061, 0062, 0063
- **Observability**: 0036, 0058, 0059, 0060
- **Frontend**: 0039, 0040, 0088
- **Component library / UI primitives**: 0088
- **Git integration**: 0035, 0054, 0055, 0057
- **Scan / import**: 0045, 0054, 0055, 0056, 0067
- **Status page**: 0005, 0010, 0023, 0051, 0052, 0053, 0076
- **Identity & auth**: 0006, 0007, 0008, 0009, 0010, 0014, 0042
- **Infrastructure & deployment**: 0022, 0023, 0024, 0025, 0043, 0085, 0086
- **Database migrations**: 0001, 0012, 0082, 0085
- **Helm / packaging / GitOps**: 0043, 0085, 0086
- **Data storage**: 0001, 0002, 0003, 0004, 0005, 0013, 0020
- **Data quality**: 0069, 0070, 0071
- **Extensibility / plugins**: 0033, 0038
- **CLI**: 0009, 0046

## Keyword Index

Alphabetical keyword index for concept-based lookup. Each entry maps a keyword to the ADR(s) that discuss it.

- **AES-256** → 0077
- **Agent (hybrid)** → 0041, 0042, 0043, 0044, 0045
- **AOT compilation** → 0041, 0046
- **API versioning** → 0030
- **Apicurio** → 0037
- **Approval workflow (discovery inbox)** → 0045
- **ASP.NET Core** → 0027, 0028, 0034, 0060
- **Audit log** → 0016, 0017, 0018, 0050, 0058, 0073
- **Avro / JSON / Protobuf schemas** → 0037
- **Bearer tokens (long-lived)** → 0042, 0077
- **Billing** → 0061, 0062, 0063
- **Blast radius / Impact analysis** → 0040, 0067, 0068
- **Blob storage** → 0004, 0020
- **Block Kit (Slack)** → 0048
- **Bulk endpoints / partial success** → 0032
- **CI/CD** → 0025, 0046
- **CLI** → 0007, 0009, 0046
- **Clean Architecture** → 0028
- **Cloud-agnostic** → 0022, 0026
- **Cold storage / Glacier / Archive** → 0020
- **Column-level encryption** → 0077
- **Confluent Schema Registry** → 0037
- **Confluent.Kafka** → 0003, 0080, 0081
- **CQRS** → 0028, 0080
- **Consent records** → 0015
- **Correlation ID** → 0058
- **Cursor pagination** → 0029, 0095
- **Custom attributes (JSONB)** → 0064
- **Custom Entity type** → 0064
- **Data residency** → 0015, 0021
- **Dead Letter Queue (DLQ)** → 0033
- **Deep scan** → 0054, 0055
- **Dependency graph** → 0040, 0067, 0068
- **DNS challenge / ACME** → 0052
- **Docker Compose (local dev)** → 0024
- **Docker image / Helm chart (agent)** → 0043
- **Dogfooding** → 0079
- **DPA (Data Processing Agreement)** → 0015
- **EF Core** → 0012, 0027, 0077
- **Elasticsearch** → 0002, 0013
- **Email / SMTP** → 0049, 0051
- **Entity lifecycle (Active/Deprecated/Retired)** → 0073
- **Entity types (9 fixed)** → 0064
- **Entitlements** → 0061, 0062
- **Error envelope** → 0029
- **Exponential backoff / retry** → 0033, 0055
- **Federation / SSO** → 0006
- **FluentAssertions (removed)** → 0083, 0097
- **FluentValidation** → 0027
- **Four-tier pricing (Free/Starter/Pro/Enterprise)** → 0061
- **GDPR** → 0015, 0019, 0021, 0062, 0077, 0078
- **Git integration** → 0035, 0054, 0055, 0057
- **GitHub / GitLab / Bitbucket** → 0035, 0057
- **Grafana Cloud** → 0036
- **Graph explorer (dependency)** → 0040
- **Hash chaining (audit)** → 0018
- **Health checks (live / ready / startup)** → 0060
- **Heartbeat endpoint** → 0042
- **Helm** → 0022, 0043, 0085, 0086
- **Helm hooks (pre-install/pre-upgrade)** → 0085
- **Helm chart location (in-repo)** → 0086
- **Init container (Docker Compose)** → 0085
- **K8s Job / CronJob** → 0085
- **Migration container (Kartova.Migrator)** → 0085
- **EF Core migrations** → 0001, 0085
- **GitOps / ArgoCD / Flux** → 0086
- **OCI registry / GHCR chart publishing** → 0086
- **Hierarchy (Org → Team → System → Component)** → 0065
- **HMAC-SHA256** → 0033
- **Hot-reload (agent config)** → 0044
- **HTTPS polling** → 0042
- **IBlobStorage abstraction** → 0004
- **Idempotency** → 0033
- **ILM (Index Lifecycle Management)** → 0013, 0017
- **Ingress / cert-manager** → 0022, 0052
- **JSONB custom attributes** → 0064
- **JWKS** → 0007, 0010
- **JWT** → 0007, 0009, 0014, 0042
- **Kafka** → 0003, 0033, 0047, 0080, 0081
- **KafkaFlow (inbound consumers)** → 0081
- **KeyCloak** → 0006, 0007, 0008, 0009, 0010, 0014
- **KRaft (Kafka)** → 0003
- **Least-privilege OAuth scopes** → 0057
- **Let's Encrypt / ACME / auto-SSL** → 0052
- **Lifecycle states** → 0073
- **Manual precedence (conflict queue)** → 0056
- **MassTransit (NOT used)** → 0003, 0080, 0081
- **Maturity model (5 levels)** → 0071
- **MediatR (NOT used)** → 0027, 0080
- **Mediator pattern** → 0028, 0080
- **Modular monolith** → 0082
- **MSTest v4** → 0097
- **Solution file format / .slnx / .sln** → 0089
- **.NET 10 SDK defaults** → 0027, 0089
- **Module boundaries / bounded contexts** → 0082
- **NetArchTest / fitness functions** → 0082, 0083
- **Pact.NET / contract tests** → 0083
- **Playwright / E2E tests** → 0083
- **Playwright MCP (dev-time browser automation)** → 0084
- **Google Stitch MCP (design source)** → 0087
- **Mockup / design reference** → 0087
- **Stitch (design tool)** → 0087
- **shadcn/ui (UI primitives)** → 0088
- **Tailwind CSS** → 0088
- **Radix UI (accessibility primitives)** → 0088
- **TanStack Table (data tables)** → 0088
- **react-hook-form + zod (forms)** → 0088
- **cmdk (command palette)** → 0088
- **sonner (toasts)** → 0088
- **Recharts (charts)** → 0088
- **React Flow (@xyflow/react)** → 0040, 0088
- **lucide-react (icons)** → 0088
- **motion / framer-motion** → 0088
- **MCP servers (general)** → 0084
- **Browser automation (development)** → 0084
- **Testcontainers** → 0083
- **Testing pyramid / test strategy** → 0083
- **xUnit / FluentAssertions (removed)** → 0083, 0097
- **Per-key parallelism (Kafka consumers)** → 0081
- **Metering (per-user)** → 0063
- **Metrics (/metrics)** → 0036, 0059
- **MiFID II** → 0016, 0017, 0018, 0050, 0062
- **MinIO** → 0004
- **mTLS (NOT used)** → 0033, 0042, 0077
- **Multi-ownership / quorum** → 0066
- **Multi-tenancy** → 0011, 0012, 0013, 0014
- **Notifications engine** → 0047, 0048, 0049, 0050, 0051
- **Notification log (MiFID)** → 0050
- **OAuth** → 0035, 0048, 0057
- **OIDC** → 0006, 0007, 0010
- **One-org-one-tenant** → 0011
- **OpenAPI** → 0029, 0034
- **Origin tracking (relationship)** → 0067
- **Partial success (bulk API)** → 0032
- **Per-tenant prefix (blob)** → 0004
- **Plugins (deferred v2)** → 0038
- **Policy engine** → (not a dedicated ADR; see PRD)
- **PostgreSQL** → 0001, 0012, 0018
- **Prometheus** → 0036, 0059, 0060
- **Proprietary (no OSS core)** → 0026
- **Quorum approval** → 0066
- **Rate limiting** → 0031, 0033, 0055
- **RBAC (5 roles)** → 0008
- **React SPA** → 0039, 0040
- **Relationship vocabulary (7 types)** → 0068
- **Relationship origin** → 0067
- **Required minimum fields** → 0069
- **Residency region** → 0021
- **REST API** → 0029, 0030, 0031, 0032, 0034, 0092, 0096
- **HTTP verbs / PUT / POST / PATCH** → 0096
- **Named action endpoints** → 0073, 0096
- **Retention (180 days / 5 years)** → 0017, 0019, 0020
- **Right to erasure (GDPR)** → 0015, 0019
- **RLS (Row-Level Security)** → 0012, 0014
- **Roles (Org Admin / Editor / Contributor / Viewer / Service Account)** → 0008
- **RSS (status page)** → 0047, 0051
- **Scale envelope** → 0074
- **Scan engine** → 0054, 0055, 0056
- **Schema Registry** → 0037
- **Scorecards** → 0070, 0071
- **Secrets policy (references only)** → 0078
- **SendGrid / SES adapters** → 0049
- **Service account** → 0008, 0009, 0063
- **SLA (99.9% / 99.99%)** → 0053, 0076
- **SLO (p95 latency)** → 0075
- **SMS** → 0047, 0051
- **SMTP** → 0049
- **Slack** → 0048
- **Soft delete / 30-day purge** → 0019
- **SPA (React)** → 0039
- **SSL / Let's Encrypt / ACME** → 0052
- **Staging (CD)** → 0025
- **Status page (architecture)** → 0005, 0010, 0023, 0051, 0052, 0053
- **Strimzi** → 0003
- **Stripe-style provider** → 0062
- **Structured JSON logs** → 0058
- **Swagger UI (dev/staging only)** → 0034
- **Tags (taxonomy)** → 0065, 0072
- **TanStack Query** → 0039
- **Teams (Microsoft)** → 0048
- **Test framework** → 0083, 0097
- **Tenant claim (JWT)** → 0014
- **Tenant ID / tenant_id** → 0001, 0012, 0013, 0014, 0058
- **Tenant isolation** → 0012, 0013, 0014
- **Tiered pricing** → 0061
- **TLS 1.2+ / 1.3** → 0077
- **Token-bucket rate limit** → 0031
- **Tokens (service account)** → 0009, 0063
- **Transactional outbox** → 0033, 0047, 0080
- **Wolverine (mediator + outbound + outbox)** → 0080
- **TypeScript (strict)** → 0039
- **URL-based versioning** → 0030
- **User-count metering** → 0063
- **Versioned config (agent)** → 0044
- **Vite** → 0039
- **Webhooks (outbound)** → 0033, 0038, 0047, 0051
- **WebSockets (rejected)** → 0042

## Deprecated / Superseded

_No ADRs have been deprecated or superseded yet. When an ADR is superseded by a new decision, it will be listed here with a link to the successor ADR and an explanation of why the change was made._

| ADR | Superseded By | Reason | Date |
|-----|---------------|--------|------|
| 0083 | 0097 | Migrated test framework + assertion library (xUnit + FluentAssertions → MSTest v4 + native asserts). Runner (VSTest) and project SDK unchanged; MTP deferred — Stryker incompatibility, see ADR-0097 Note. | 2026-05-08 |

## History

| Date | Event |
|------|-------|
| 2026-04-17 | Initial ADR library created (38 accepted, 41 pending) |
| 2026-04-17 | Batches 2 and 3 transformed 31 more candidates (69 total) |
| 2026-04-20 | ADR-0003, 0004, 0012, 0013 accepted (DISCUSS resolved) |
| 2026-04-21 | ADR-0033, 0042, 0060, 0061, 0064, 0077 accepted (final DISCUSS resolved) |
| 2026-04-21 | README restructured as LLM-friendly index with Summary/Related columns |
| 2026-04-21 | Added YAML front-matter, Keyword Index, and Deprecated/Superseded section |
| 2026-04-21 | ADR-0080 (Wolverine — mediation + outbound + outbox) and ADR-0081 (KafkaFlow — inbound consumers) accepted; MassTransit and MediatR removed from stack; ADR-0003, 0027, 0028, 0033, 0047 updated accordingly |
| 2026-04-21 | ADR-0082 (Modular monolith) accepted — bounded-context modules with NetArchTest-enforced boundaries, Wolverine/Kafka-only inter-module communication; ADR-0028 updated |
| 2026-04-21 | ADR-0083 (Testing strategy) accepted — five-tier pyramid with NetArchTest architecture tests as mandatory CI gate |
| 2026-04-21 | ADR-0084 (Playwright MCP for frontend dev) accepted — mandatory browser verification during AI-assisted frontend work; complementary to ADR-0083 E2E tier |
| 2026-04-21 | ADR-0085 (DB migrations as K8s Jobs / Docker init containers) and ADR-0086 (Helm chart in-repo at `deploy/helm/kartova/`) accepted; ADR-0022 and ADR-0024 updated |
| 2026-04-21 | ADR-0087 (Google Stitch MCP as design source) accepted — full frontend loop formalized: Stitch (design) → code → Playwright (verify); ADR-0084 updated |
| 2026-04-21 | ADR-0088 (shadcn/ui + Tailwind stack) accepted — frontend primitives decided based on Stitch output visual analysis; ADR-0039, 0040, 0087 updated |
| 2026-04-21 | ADR-0087 refined — local-first workflow: `docs/ui-screens/{screen}/code.html` + `screen.png` are default canonical source; Stitch MCP is escalation for missing or stale screens |
| 2026-04-21 | ADR-0089 (`.slnx` solution format) accepted — adopted during Slice 1; ADR-0082 Implementation Notes simplified to cross-reference; ADR-0083 Implementation Notes updated to document co-located module test layout |
| 2026-04-22 | ADR-0091 (RFC 7807 Problem Details) accepted — uniform error body shape across API clients with trace-id correlation |
| 2026-04-22 | ADR-0090 (Tenant scope mechanism) accepted — `ITenantScope` with transaction-bound `SET LOCAL`, shared connection per request, per-transport adapters; Slice 2 starts |
| 2026-04-29 | ADR-0092 (REST API URL convention) accepted — module-prefixed URLs with admin-first prefix and primary-collection skip rule; precursor PR before Slice 3 (Catalog: Register Application) |
| 2026-04-30 | ADR-0093 (Wolverine scope narrowed) accepted — narrows ADR-0028; sync HTTP handlers use direct dispatch to share `ITenantScope` request scope, Wolverine retained for outbox/async/Kafka; `WolverineFx.Http` deferred |
| 2026-05-01 | ADR-0094 (Untitled UI free-tier as primary UI primitive layer) accepted — supersedes ADR-0088 (shadcn/ui); `react-aria-components` + Tailwind CSS v4 + `@untitledui/icons` adopted; DESIGN.md color/typography deferred to Untitled defaults. (Originally numbered ADR-0092; renumbered to ADR-0094 on 2026-05-04 after a numbering collision with the REST API URL convention ADR was discovered.) |
| 2026-05-04 | ADR-0095 (Cursor pagination contract) accepted — concrete contract for ADR-0029's "pagination via cursors" mention; first-cut mandate + arch fitness rule; reference impl on Applications list |
| 2026-05-06 | ADR-0096 (REST verb policy) accepted — `PUT` for full replacement, `POST /<action>` for named domain commands, `PATCH` forbidden; arch fitness rule pins the no-PATCH invariant; first instantiated by Slice 5 (Applications edit + lifecycle) |
| 2026-05-08 | ADR-0097 (MSTest v4 supersedes xUnit) accepted — replaces xUnit + FluentAssertions with MSTest v4 native assertions across all 10 xUnit-using test projects; project SDK and VSTest runner unchanged (MTP deferred — Stryker.NET 4.14.1 does not support it, see stryker-net#3094); ADR-0083 marked superseded |
