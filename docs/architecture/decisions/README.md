# Architecture Decision Records

**Status:** Living document
**Last updated:** 2026-04-20

This directory holds the formal Architecture Decision Records (ADRs) for Kartova. Each file follows the Michael Nygard template (Context / Decision / Rationale / Alternatives / Consequences). See [ADR-CANDIDATES.md](../ADR-CANDIDATES.md) for the full list of candidates and their review state.

## Accepted ADRs (69)

### Data Platform
- [ADR-0001: PostgreSQL as Primary Database](ADR-0001-postgresql-as-primary-database.md)
- [ADR-0002: Elasticsearch for Search](ADR-0002-elasticsearch-for-search.md)
- [ADR-0005: Independent Data Replica for Status Page](ADR-0005-independent-data-replica-for-status-page.md)

### Authentication & Authorization
- [ADR-0006: KeyCloak as Identity Provider](ADR-0006-keycloak-as-identity-provider.md)
- [ADR-0007: JWT (OIDC) for API and CLI Auth](ADR-0007-jwt-oidc-for-api-and-cli-auth.md)
- [ADR-0008: Five Fixed RBAC Roles](ADR-0008-five-fixed-rbac-roles.md)
- [ADR-0009: Service Account JWT Model for CI/CD](ADR-0009-service-account-jwt-model-for-cicd.md)
- [ADR-0010: Internal Status Page Auth via KeyCloak](ADR-0010-internal-status-page-auth-via-keycloak.md)

### Multi-Tenancy
- [ADR-0011: One Organization Equals One Tenant](ADR-0011-one-organization-equals-one-tenant.md)
- [ADR-0014: Tenant Claim Extracted from JWT](ADR-0014-tenant-claim-extracted-from-jwt.md)

### Compliance & Retention
- [ADR-0015: GDPR Compliance From Day One](ADR-0015-gdpr-compliance-from-day-one.md)
- [ADR-0016: MiFID II Compliance From Day One](ADR-0016-mifid-ii-compliance-from-day-one.md)
- [ADR-0017: Default 180-Day Retention, 5-Year for MiFID II](ADR-0017-default-180-day-retention-5-year-mifid.md)
- [ADR-0018: Append-Only Tamper-Evident Audit Log](ADR-0018-append-only-tamper-evident-audit-log.md)
- [ADR-0019: Soft Delete With 30-Day Purge](ADR-0019-soft-delete-with-30-day-purge.md)
- [ADR-0020: Cold-Storage Archival After Active Retention](ADR-0020-cold-storage-archival-after-active-retention.md)
- [ADR-0021: Data Residency Tracking Per Tenant](ADR-0021-data-residency-tracking-per-tenant.md)
- [ADR-0050: Notification Log as MiFID II Communication Record](ADR-0050-notification-log-as-mifid-ii-record.md)

### Platform Infrastructure
- [ADR-0022: Kubernetes, Cloud-Agnostic Deployment](ADR-0022-kubernetes-cloud-agnostic-deployment.md)
- [ADR-0023: Status Page as Separate K8s Cluster](ADR-0023-status-page-as-separate-k8s-cluster.md)
- [ADR-0024: Docker Compose for Local Dev](ADR-0024-docker-compose-for-local-dev.md)
- [ADR-0025: CI on Push; CD to Staging on Merge](ADR-0025-ci-on-push-cd-to-staging-on-merge.md)
- [ADR-0026: Fully Proprietary — No Open-Source Core](ADR-0026-fully-proprietary-no-open-source-core.md)

### API & Integration Architecture
- [ADR-0027: .NET / ASP.NET Core for Backend API](ADR-0027-dotnet-aspnet-core-for-backend-api.md)
- [ADR-0028: Clean Architecture Layering](ADR-0028-clean-architecture-layering.md)
- [ADR-0029: REST as Primary API Style](ADR-0029-rest-as-primary-api-style.md)
- [ADR-0030: URL- or Header-Based API Versioning](ADR-0030-url-or-header-based-api-versioning.md)
- [ADR-0031: Per-Tenant Rate Limiting](ADR-0031-per-tenant-rate-limiting.md)
- [ADR-0032: Bulk API Endpoints With Partial Success](ADR-0032-bulk-api-endpoints-with-partial-success.md)
- [ADR-0034: OpenAPI Auto-Generated & Self-Rendered](ADR-0034-openapi-auto-generated-and-self-rendered.md)
- [ADR-0035: Git as First-Class Integration](ADR-0035-git-as-first-class-integration.md)
- [ADR-0036: Prometheus + Grafana Cloud Integrations](ADR-0036-prometheus-grafana-cloud-integrations.md)
- [ADR-0037: Schema Registry Integrations (Confluent + Apicurio)](ADR-0037-schema-registry-integrations.md)
- [ADR-0038: Plugin Architecture Deferred to v2.0+](ADR-0038-plugin-architecture-deferred-to-v2.md)

### Frontend Architecture
- [ADR-0039: React SPA with TypeScript](ADR-0039-react-spa-with-typescript.md)
- [ADR-0040: Two-View Dependency Graph Navigation](ADR-0040-two-view-dependency-graph-navigation.md)

### Agent Architecture
- [ADR-0041: .NET Agent with AOT Compilation](ADR-0041-dotnet-agent-with-aot-compilation.md)
- [ADR-0043: Agent Deployable as Docker / Helm](ADR-0043-agent-deployable-as-docker-and-helm.md)
- [ADR-0044: Centrally Managed Agent Config (Pull-Based)](ADR-0044-centrally-managed-agent-config-pull-based.md)
- [ADR-0045: Agent-Discovered Services Require Approval Workflow](ADR-0045-agent-discovered-services-approval-workflow.md)

### CLI & Distribution
- [ADR-0046: .NET Global Tool & Standalone Binary CLI Distribution](ADR-0046-dotnet-global-tool-cli-distribution.md)

### Notification Architecture
- [ADR-0047: Unified Multi-Channel Notification Dispatch Engine](ADR-0047-unified-multi-channel-notification-engine.md)
- [ADR-0048: Native Slack & Microsoft Teams Integrations](ADR-0048-native-slack-and-teams-integrations.md)
- [ADR-0049: Configurable SMTP / Email Provider](ADR-0049-configurable-smtp-email-provider.md)

### Status Page Architecture
- [ADR-0051: Multi-Channel Status Page Subscribers](ADR-0051-multi-channel-status-page-subscribers.md)
- [ADR-0052: Custom Domains with Auto-Provisioned SSL](ADR-0052-custom-domains-with-auto-ssl.md)
- [ADR-0053: 99.99% SLA Target for Status Page](ADR-0053-status-page-99-99-sla-target.md)

### Scan / Import Architecture
- [ADR-0054: Deep Repository Scan at Import-Time](ADR-0054-deep-repository-scan-at-import.md)
- [ADR-0055: Scan Timeout, Retry, Rate-Limit Aware](ADR-0055-scan-timeout-retry-rate-limit-aware.md)
- [ADR-0056: Manual Relationship Precedence (Conflict Queue)](ADR-0056-manual-relationship-precedence.md)
- [ADR-0057: OAuth-Based Git Provider Connection](ADR-0057-oauth-git-provider-connection.md)

### Observability & Monitoring
- [ADR-0058: Structured JSON Logs with Tenant Context](ADR-0058-structured-json-logs-with-tenant-context.md)
- [ADR-0059: Prometheus-Compatible Metrics Exposition](ADR-0059-prometheus-compatible-metrics-exposition.md)

### Billing
- [ADR-0062: External Billing Provider for Payment Processing](ADR-0062-external-billing-provider.md)
- [ADR-0063: User-Count Metering Per Billing Period](ADR-0063-user-count-metering-per-billing-period.md)

### Domain Model
- [ADR-0065: Hybrid Org Structure — Hierarchy + Tags](ADR-0065-hybrid-org-structure-hierarchy-plus-tags.md)
- [ADR-0066: Multi-Ownership with Quorum Rules](ADR-0066-multi-ownership-with-quorum-rules.md)
- [ADR-0067: Relationship Origin Tracking](ADR-0067-relationship-origin-tracking.md)
- [ADR-0068: Fixed Relationship Type Vocabulary](ADR-0068-fixed-relationship-type-vocabulary.md)
- [ADR-0069: Required Minimum Fields Enforcement](ADR-0069-required-minimum-fields-enforcement.md)
- [ADR-0070: Per-Organization Scorecard Configurability](ADR-0070-per-organization-scorecard-configurability.md)
- [ADR-0071: Five-Level Maturity Model](ADR-0071-five-level-maturity-model.md)
- [ADR-0072: Tag Taxonomy — Predefined Plus Custom](ADR-0072-tag-taxonomy-predefined-plus-custom.md)
- [ADR-0073: Enforced Entity Lifecycle States](ADR-0073-enforced-entity-lifecycle-states.md)

### Scale & Performance
- [ADR-0074: Scale Targets — 1000+ Tenants, 10k Services/Tenant](ADR-0074-scale-targets-1000-tenants.md)
- [ADR-0075: Performance SLOs — p95 Latency Targets](ADR-0075-performance-slos-p95-latency.md)
- [ADR-0076: Two-Tier SLA — Platform 99.9% / Status Page 99.99%](ADR-0076-two-tier-sla-platform-99-9-status-99-99.md)

### Non-Functional / Cross-Cutting
- [ADR-0078: No Secrets or Credentials Stored — References Only](ADR-0078-no-secrets-stored-references-only.md)
- [ADR-0079: Dogfooding + Design Partners Go-to-Market Strategy](ADR-0079-dogfooding-design-partners-gtm.md)

## Pending Review

The following candidates are marked DISCUSS. See [ADR-CANDIDATES.md](../ADR-CANDIDATES.md).

### DISCUSS (10) — need resolution before affected phase implementation
- ADR-003 Message bus (RabbitMQ / Kafka / alternatives)
- ADR-004 Blob storage for large artifacts
- ADR-012 Tenant isolation strategy (RLS vs schema-per-tenant)
- ADR-013 Elasticsearch index strategy (shared vs per-tenant)
- ADR-033 Webhook delivery mechanism (HMAC signing, retry, DLQ)
- ADR-042 Agent communication protocol (outbound-only mTLS)
- ADR-060 `/health/live` + `/health/ready` endpoints
- ADR-061 Simple per-user monthly pricing
- ADR-064 Fixed entity taxonomy (9 entity types)
- ADR-077 Encryption in transit + at rest

### Unreviewed (0)
_All candidates have been reviewed._
