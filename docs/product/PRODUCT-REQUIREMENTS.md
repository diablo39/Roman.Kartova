# Product Requirements Document: Kartova — Service Catalog & Developer Portal Platform

**Document Version:** 1.0
**Date:** 2026-04-03
**Status:** Draft — Pending Stakeholder Review

---

## 1. Product Vision

A managed SaaS platform for tracking applications, components, dependencies, health status, and documentation — combining the best of Backstage (developer portal) and Atlassian Compass (component tracking) with Statuspage (public status communication). The platform differentiates through **frictionless self-service onboarding**, an opinionated managed service model, and deep CI/CD integration via API and CLI.

**Product Name:** Kartova

---

## 2. Target Users

| User Type | Role | Primary Needs |
|-----------|------|---------------|
| **Developers** (Primary) | Build and maintain services | Find services, understand dependencies, access documentation, register new components |
| **DevOps / Platform Engineers** (Primary) | Manage infrastructure and deployments | Track environments, monitor health, enforce policies, manage CI/CD integration |
| **Engineering Managers** (Secondary) | Oversee teams and systems | Dashboards, ownership visibility, scorecard compliance, org-wide health overview |
| **External Stakeholders** (Secondary) | Customers, partners | Public status page with uptime, SLA information, incident history, maintenance schedules |

---

## 3. Core Entity Model

### 3.1 Tracked Entity Types

| Entity | Description | Key Attributes |
|--------|-------------|----------------|
| **Application** | A deployable software unit | Name, description, language, framework, repo URL, owner(s), team, tags |
| **Service** | A running instance of an application or standalone service | Endpoints, protocol, health status, dependencies |
| **API (Sync)** | A synchronous interface (REST, gRPC, GraphQL) | Spec URL (OpenAPI/proto/schema), version, consumers, providers |
| **API (Async)** | An asynchronous event-driven interface | AsyncAPI spec, protocol (Kafka/RabbitMQ/MQTT/AMQP/NATS), channels, schemas, producers, consumers |
| **Infrastructure** | Cloud resources, databases, caches, etc. | Provider, type, region, configuration |
| **Message Broker** | Messaging infrastructure (Kafka, RabbitMQ, etc.) | Broker type, connection details |
| **Queue / Topic** | Individual messaging channels within brokers | Name, schema, producers, consumers, broker reference |
| **Environment** | A deployment target (dev, staging, prod, etc.) | Name, type, region, cluster, infrastructure details |
| **Deployment** | A specific version deployed to an environment | Application, environment, version, config, timestamp, deployer, replicas, resource allocation |

### 3.2 Organization Structure (Hybrid: Hierarchy + Tags)

**Hierarchy** (for ownership and navigation):
```
Organization (Tenant)
  └── Team
        └── System (logical grouping of related entities)
              └── Component (application, service, API, etc.)
```

**Tags** (for cross-cutting concerns):
- Domain / business capability (e.g., `payments`, `identity`, `logistics`)
- Criticality level (e.g., `tier-1`, `tier-2`, `tier-3`)
- Compliance requirements (e.g., `pci`, `gdpr`, `hipaa`)
- Technology stack (e.g., `dotnet`, `react`, `postgresql`)
- Custom tenant-defined tags

**Multi-ownership:** Components can be owned by multiple teams. Shared/platform components have a `platform` designation but can also have co-owning teams.

> **ADRs:** [ADR-0065](../architecture/decisions/ADR-0065-hybrid-org-structure-hierarchy-plus-tags.md) (hybrid hierarchy+tags), [ADR-0066](../architecture/decisions/ADR-0066-multi-ownership-with-quorum-rules.md) (multi-ownership), [ADR-0072](../architecture/decisions/ADR-0072-tag-taxonomy-predefined-plus-custom.md) (tag taxonomy)

### 3.3 Entity Relationships

- **Primary mechanism:** Auto-discovery with manual override
- System auto-discovers dependencies from code analysis, configuration scanning, and runtime data (via agents)
- Users can manually add, correct, or remove any relationship
- Relationship types: `depends-on`, `provides-api-for`, `consumes-api-from`, `publishes-to`, `subscribes-from`, `deployed-on`, `part-of`

**Relationship origin tracking:**
- Every relationship is tagged with its origin: `manual` (user-created) or `auto-discovered` (system-detected)
- **Visual distinction:** Manual relationships are visually highlighted in all views (dependency graphs, entity detail pages, lists) with a distinct icon/badge/color to differentiate them from auto-discovered ones
- **Manual relationships are protected:** Auto-import and scheduled re-scans cannot modify or delete manual relationships. If a re-scan detects a conflict with a manual relationship, it is flagged for user review rather than overridden
- **Override hierarchy:** Manual > Auto-discovered. A user can promote an auto-discovered relationship to manual (pinning it), or demote a manual one back to auto-managed
- **Audit trail:** All relationship changes (creation, modification, deletion) are logged with timestamp, actor (user or system), and previous state

> **ADRs:** [ADR-0056](../architecture/decisions/ADR-0056-manual-relationship-precedence.md) (manual precedence), [ADR-0067](../architecture/decisions/ADR-0067-relationship-origin-tracking.md) (origin tracking), [ADR-0068](../architecture/decisions/ADR-0068-fixed-relationship-type-vocabulary.md) (relationship types)

---

## 4. Feature Requirements

### 4.1 Service Catalog (MVP — v1.0)

**FR-4.1.1 Entity Registry**
- CRUD operations for all entity types (Application, Service, API, Infrastructure, Message Broker, Queue/Topic, Environment, Deployment)
- Each entity has a detail page with metadata, relationships, health status, documentation, and deployment history
- Entity lifecycle management (active, deprecated, decommissioned)

> **ADRs:** [ADR-0073](../architecture/decisions/ADR-0073-enforced-entity-lifecycle-states.md) (lifecycle states)

**FR-4.1.2 Organization & Team Management**
- Hierarchical org structure: Organization → Team → System → Component
- Team profiles with members, owned components, and contact information
- Multi-ownership support for shared components
- Tag management with both system-defined and custom tag taxonomies

**FR-4.1.3 Dependency Tracking & Visualization**
- **Two-view navigation model:**
  - **Embedded mini-graph** on entity detail page ("Dependencies" tab): Shows 1 level deep (direct dependencies/dependents), summary stats, relationship table with origin badges, basic zoom/pan, "Open full graph" link
  - **Standalone graph explorer** (`/graph`): Accessible from sidebar navigation and from entity detail via "Open full graph" button (opens with `?focus=entity-id`). Full interactive graph with multi-level depth (1-3 levels + all), entity search/selector, side panel with node details, "Focus Graph Here" to re-center
- Filter by team, system, domain, criticality, entity type, or relationship origin (manual/auto)
- Impact analysis: "if Service X goes down, what is affected?" — visual overlay dimming unaffected nodes, tier-based glow on affected paths
- Environment-specific dependency views (prod vs staging may differ)

> **ADRs:** [ADR-0040](../architecture/decisions/ADR-0040-two-view-dependency-graph-navigation.md)

**FR-4.1.4 Search**
- Entity search by name, type, tags, team, owner
- Faceted filtering and sorting
- Full-text search across imported documentation, API specs, and runbooks

**FR-4.1.5 Dashboards & Visualizations**
- Standalone dependency graph explorer (accessible from sidebar navigation — see FR-4.1.3)
- Environment map (which versions are deployed where)
- Status board (health overview across all services)
- Team dashboard (team's owned components, health, scorecard)
- Organization overview (aggregate health, compliance, coverage)

### 4.2 Self-Service Onboarding & Auto-Import (MVP — v1.0)

**FR-4.2.1 Self-Service Onboarding Wizard**
- Step-by-step guided flow: Create Org → Connect Git Provider → Scan Repos → Review Discovered Entities → Confirm Import
- No human assistance required — fully self-service
- Progress indicators and contextual help at each step

**FR-4.2.2 Auto-Import — Full Deep Scan**
The platform scans repositories and automatically extracts:
- **Code metadata:** Language, framework, dependencies (package.json, .csproj, pom.xml, go.mod, etc.)
- **Infrastructure:** Dockerfiles, Helm charts, Terraform files, CI/CD pipeline configs
- **Sync API contracts:** OpenAPI/Swagger specs, gRPC proto files, GraphQL schemas
- **Async API contracts:** AsyncAPI 2.x/3.x specs, CloudEvents definitions, schema registry references (Confluent, Apicurio)
- **Messaging:** Queue/topic names from configuration files, message broker connection strings
- **Database:** Connection strings, migration files, schema definitions
- **Environment variables:** Detected (not values — only names/keys for mapping)
- **Documentation:** README, docs/ folders, architecture diagrams

> **ADRs:** [ADR-0054](../architecture/decisions/ADR-0054-deep-repository-scan-at-import.md) (deep scan)

**FR-4.2.3 Bulk Import & Scheduled Re-scan**
- Bulk import: Point at a GitHub organization or Azure DevOps project and scan all repositories
- Filtering: Include/exclude repos by naming convention, language, last activity date
- Scheduled re-scan: Configurable periodic re-scanning (e.g., daily/weekly) to detect:
  - New repositories
  - Changed dependencies
  - Updated API specs (OpenAPI and AsyncAPI)
  - Schema registry changes
  - New/removed infrastructure
- Change detection with diff view: Show what changed since last scan

**FR-4.2.4 Data Quality — Scorecards with Required Minimum**
- **Required minimum fields** (block import without): Owner/team, name, description
- **Scorecard system** for everything else:
  - Completeness score per entity (0-100%)
  - Categories: Documentation, Operations, Security, Quality
  - Configurable scoring rules per organization
  - Dashboard showing org-wide scorecard compliance
- **Nudge system:** Notifications to owners when scorecard drops below threshold

> **ADRs:** [ADR-0069](../architecture/decisions/ADR-0069-required-minimum-fields-enforcement.md) (required fields), [ADR-0070](../architecture/decisions/ADR-0070-per-organization-scorecard-configurability.md) (scorecard configurability)

### 4.3 Documentation Management

**FR-4.3.1 Git-Synced Documentation**
- Import markdown documentation from git repositories
- Webhook-triggered sync on push events
- Render markdown with full GitHub-flavored markdown support
- Support for docs/ directory structures with navigation

**FR-4.3.2 Auto-Generated API Documentation**
- Detect and render OpenAPI/Swagger specs as interactive API documentation
- Support for gRPC proto files and GraphQL schema introspection
- Versioned API documentation aligned with deployments

**FR-4.3.2a AsyncAPI Documentation**
- Auto-detect AsyncAPI specs (v2.x and v3.x) in repositories during import
- Render as interactive documentation showing:
  - Channels (topics/queues) with publish/subscribe operations
  - Message schemas with examples and validation
  - Protocol bindings (Kafka, RabbitMQ, MQTT, AMQP, NATS)
  - Server/broker connection information
- **CloudEvents support:** Detect and render CloudEvents metadata (type, source, subject, dataschema)
- **Schema registry integration:** Pull live schemas from Confluent Schema Registry and Apicurio Registry
  - Display schema versions and evolution history
  - Link registry schemas to corresponding AsyncAPI channel definitions
- Unified API view: Show both sync (OpenAPI) and async (AsyncAPI) APIs for a service side-by-side

> **ADRs:** [ADR-0037](../architecture/decisions/ADR-0037-schema-registry-integrations.md)

**FR-4.3.3 Full Documentation Site per Service**
Each service gets a documentation hub:
- Markdown pages (synced from git)
- Sync API reference (auto-generated from OpenAPI/gRPC/GraphQL specs)
- Async API reference (auto-generated from AsyncAPI specs, schema registry)
- Architecture diagrams
- Changelog (derived from git history)
- Runbooks and onboarding guides

**FR-4.3.4 Cross-Service Referencing**
- Automatic cross-linking: When Service A's docs reference Service B, it becomes a navigable link
- Global documentation search across all services and teams
- "Related services" suggestions based on dependency graph

### 4.4 Status Page

**FR-4.4.1 Public Status Page (Statuspage Feature Parity)**
- Per-tenant public status page with:
  - Custom domain support (status.customer.com)
  - Branding: Logo, colors, custom CSS
  - Service component grouping and hierarchy
  - Real-time status indicators: Operational, Degraded Performance, Partial Outage, Major Outage, Under Maintenance
- **Subscriber notifications:** Email, SMS, webhook, RSS for status changes
- **Incident history:** Timeline of past incidents with status updates
- **Scheduled maintenance:** Announce upcoming maintenance windows
- **Uptime charts:** Historical uptime percentage per component (daily/weekly/monthly)
- **Tenant controls:** Choose which internal services are exposed on the public page

> **ADRs:** [ADR-0051](../architecture/decisions/ADR-0051-multi-channel-status-page-subscribers.md) (subscribers), [ADR-0052](../architecture/decisions/ADR-0052-custom-domains-with-auto-ssl.md) (custom domains + SSL)

**FR-4.4.2 Uptime & Health Monitoring**
- Source of truth: Multi-source aggregation
  - Prometheus integration (pull metrics)
  - Grafana Cloud integration
  - Hybrid agent health data
  - Custom webhook/API health reporting
- Uptime calculation per service, per environment
- Historical uptime data retention

> **ADRs:** [ADR-0036](../architecture/decisions/ADR-0036-prometheus-grafana-cloud-integrations.md)

**FR-4.4.3 Incident Management (Future — post-MVP)**
- Basic incident lifecycle: Create → Update → Resolve
- Link incidents to affected services
- Post-mortem templates and timeline
- Integration with PagerDuty / OpsGenie for on-call

### 4.5 CLI & API

**FR-4.5.1 RESTful API**
- Full CRUD for all entity types
- Bulk operations
- Webhook registration and management
- Authentication via JWT tokens (from KeyCloak)
- API versioning
- Rate limiting per tenant

> **ADRs:** [ADR-0007](../architecture/decisions/ADR-0007-jwt-oidc-for-api-and-cli-auth.md), [ADR-0029](../architecture/decisions/ADR-0029-rest-as-primary-api-style.md), [ADR-0030](../architecture/decisions/ADR-0030-url-or-header-based-api-versioning.md), [ADR-0031](../architecture/decisions/ADR-0031-per-tenant-rate-limiting.md)

**FR-4.5.2 CLI Tool**
- Register and update components from CI/CD pipelines
- Report deployment events (version, environment, config)
- Report health check results
- Trigger repository re-scans
- Validate catalog entries (lint/check completeness)
- Run compliance checks against organizational policies
- Gate deployments: CLI returns non-zero exit code if policy violations exist

**FR-4.5.3 Policy Enforcement**
- Organizations define policies (e.g., "all services must have a runbook", "APIs must have OpenAPI spec", "tier-1 services must have on-call defined")
- CLI validates against policies in CI/CD pipeline
- Policy violations can be warnings or blocking errors (configurable)
- Policy compliance dashboard in the web UI

### 4.6 Hybrid Agent

**FR-4.6.1 Lightweight Agent for Customer Infrastructure**
- Deployable as a Docker container or Kubernetes DaemonSet/Deployment
- Minimal resource footprint
- Secure outbound-only communication to platform (no inbound ports needed)

> **ADRs:** [ADR-0041](../architecture/decisions/ADR-0041-dotnet-agent-with-aot-compilation.md), [ADR-0043](../architecture/decisions/ADR-0043-agent-deployable-as-docker-and-helm.md)

**FR-4.6.2 Agent Capabilities**
- **Health checks:** HTTP, TCP, gRPC health probes on configured endpoints
- **Metrics collection:** Scrape Prometheus metrics endpoints and forward aggregated data
- **Service discovery:** Scan local Kubernetes clusters, Docker hosts, and service meshes to discover running services and their relationships
- Agent configuration managed from platform UI
- Auto-registration of discovered services (with approval workflow)

> **ADRs:** [ADR-0044](../architecture/decisions/ADR-0044-centrally-managed-agent-config-pull-based.md)

### 4.7 Notifications

**FR-4.7.1 Multi-Channel Notification System**
- **In-app:** Real-time notifications within the platform UI
- **Email:** Configurable email notifications for important events
- **Webhooks:** Outbound webhooks for custom routing
- **Native integrations:** Slack and Microsoft Teams
- Notification preferences per user (channel, frequency, event types)
- Organization-level notification policies

> **ADRs:** [ADR-0047](../architecture/decisions/ADR-0047-unified-multi-channel-notification-engine.md) (dispatch engine), [ADR-0048](../architecture/decisions/ADR-0048-native-slack-and-teams-integrations.md) (Slack/Teams), [ADR-0049](../architecture/decisions/ADR-0049-configurable-smtp-email-provider.md) (SMTP/email)

**FR-4.7.2 Notification Events**
- Status changes (service health transitions)
- Scorecard threshold breaches
- New services discovered by agent or re-scan
- Deployment events
- Policy violation alerts
- Subscription notifications for status page updates

### 4.8 Integrations

**FR-4.8.1 Mandatory Integrations (MVP)**
- **Git (generic):** Clone, scan, sync documentation, webhook support
- **Prometheus:** Query metrics, define uptime rules
- **Grafana Cloud:** Dashboard linking, annotation sync

> **ADRs:** [ADR-0035](../architecture/decisions/ADR-0035-git-as-first-class-integration.md), [ADR-0036](../architecture/decisions/ADR-0036-prometheus-grafana-cloud-integrations.md)

**FR-4.8.2 Additional Integrations**
- **GitHub:** OAuth, org/repo discovery, Actions integration, webhooks
- **Azure DevOps:** OAuth, project/repo discovery, pipeline integration, webhooks

> **ADRs:** [ADR-0035](../architecture/decisions/ADR-0035-git-as-first-class-integration.md)

**FR-4.8.3 Extensibility (Future)**
- Inbound/outbound webhooks for custom event handling (near-term)
- Plugin architecture for third-party integrations (future roadmap)

> **ADRs:** [ADR-0038](../architecture/decisions/ADR-0038-plugin-architecture-deferred-to-v2.md)

### 4.9 API Changelog & Breaking Change Detection

**FR-4.9.1 API Version Tracking**
- Track all versions of OpenAPI, AsyncAPI, gRPC proto, and GraphQL schemas per service
- Automatic diff generation between versions (field added, removed, type changed, etc.)
- Visual changelog: Side-by-side comparison of API spec versions

**FR-4.9.2 Breaking Change Detection**
- Automatically classify changes as: backward-compatible, breaking, or deprecation
- Breaking change rules: removed endpoints/channels, changed field types, removed required fields, modified authentication
- **Consumer alerting:** Notify all downstream consumers of an API before breaking changes are deployed
- **CI/CD integration:** CLI command to check for breaking changes; optionally block deployment if breaking change detected without version bump
- **Compatibility timeline:** Track when deprecated fields/endpoints will be removed

### 4.10 Developer Experience Score (DX Score)

**FR-4.10.1 DX Metrics per Service**
- Composite score (0-100) measuring how easy a service is to work with:
  - **Documentation completeness** — README, API docs, runbook, architecture diagram
  - **Onboarding readiness** — Getting started guide, local dev setup instructions, example requests
  - **Operational maturity** — Health checks defined, monitoring configured, alerting set up
  - **API quality** — Spec completeness, consistent naming, versioning in place
- Trend tracking: DX Score over time per service, team, and org
- Leaderboards and team-level aggregation

**FR-4.10.2 DX Improvement Suggestions**
- Actionable recommendations: "Add a runbook to improve your DX Score by 15 points"
- Priority-ranked by impact on score

### 4.11 Dependency Risk Scoring

**FR-4.11.1 Automated Risk Assessment**
- Risk score per entity combining multiple signals:
  - **Ownership:** No owner or inactive owner = higher risk
  - **Criticality:** Tier-1 services weighted more heavily
  - **Operational readiness:** Missing runbook, no health checks, no monitoring = higher risk
  - **Dependency fan-in:** Many services depend on this one = higher blast radius
  - **Staleness:** No deployments in X days, outdated dependencies
  - **Scorecard compliance:** Low scorecard score contributes to risk
- Risk levels: Critical, High, Medium, Low
- Risk heatmap dashboard across all services

**FR-4.11.2 Risk Alerts**
- Notify team and org admins when a service crosses a risk threshold
- Trending alerts: "Service X risk score has increased by 20 points in the last 30 days"

### 4.12 Cost Attribution per Service

**FR-4.12.1 Infrastructure Cost Mapping**
- Map infrastructure resources (K8s pods, cloud services, databases) to catalog entities
- Aggregate cost per service, per team, per system
- Data sources: Kubernetes resource requests/limits, cloud provider cost APIs, manual cost entries
- Cost per environment breakdown (dev vs staging vs prod)

**FR-4.12.2 Cost Dashboards**
- Monthly cost per service with trend visualization
- Team and org-level cost rollup
- Cost anomaly detection: Alert when a service's cost spikes beyond threshold
- "Cost per request" estimation (cost ÷ traffic volume, where available)

### 4.13 Service Maturity Model

**FR-4.13.1 Maturity Levels**
Predefined maturity model with 5 levels:

| Level | Name | Requirements |
|-------|------|-------------|
| L1 | **Registered** | Entity exists in catalog with owner and description |
| L2 | **Documented** | Has README, API spec, and basic documentation |
| L3 | **Observable** | Health checks, monitoring, and alerting configured |
| L4 | **Operationally Ready** | Runbook, on-call defined, incident response plan, SLA defined |
| L5 | **Production-Grade** | Full scorecard compliance, DX Score > 80, risk score = Low, cost tracked |

- Auto-calculated from existing data (scorecards, DX Score, risk score, health checks)
- Organizations can customize level requirements
- Progression path: Clear list of actions needed to reach the next level

> **ADRs:** [ADR-0071](../architecture/decisions/ADR-0071-five-level-maturity-model.md) (5-level maturity model)

**FR-4.13.2 Maturity Dashboards**
- Distribution of services across maturity levels (org-wide)
- Team maturity comparison
- Maturity trend over time

### 4.14 Change Impact Preview

**FR-4.14.1 Blast Radius Analysis**
- Before deployment: Show all downstream services affected by a change
- Categorize impact by criticality tier of affected services
- Display: "This change impacts 12 downstream services (3 × tier-1, 5 × tier-2, 4 × tier-3)"
- Visual blast radius graph highlighting affected paths

**FR-4.14.2 CI/CD Integration**
- CLI command: `kartova impact-check` — returns blast radius report
- Configurable thresholds: Require manual approval if blast radius exceeds X tier-1 services
- Impact report as PR comment (via GitHub/Azure DevOps integration)

### 4.15 Built-in Tech Radar

**FR-4.15.1 Technology Radar**
- Four rings: **Adopt**, **Trial**, **Assess**, **Hold**
- Four quadrants: Languages & Frameworks, Infrastructure, Data Management, Tools
- **Auto-populated:** Radar entries generated from actual technology usage detected in catalog (languages, frameworks, databases, message brokers from auto-import)
- Organizations can override ring placement (e.g., move a framework from Adopt to Hold)
- Per-technology view: Which services use this technology, how many, trend over time

**FR-4.15.2 Tech Radar Governance**
- Policy support: "Services using 'Hold' technologies must have a migration plan"
- Drift detection: Alert when new services adopt a technology marked as Hold
- CLI enforcement: Warn or block in CI/CD when deploying a service with Hold-listed technology

### 4.16 Multi-Environment Drift Detection

**FR-4.16.1 Drift Monitoring**
- Continuously compare deployed versions across environments per service
- Detect drift scenarios:
  - Version on staging for > X days but not promoted to prod
  - Config differences between environments beyond expected (env-specific values)
  - Infrastructure drift (different resource allocations between staging and prod)
- Staleness alerts: "Service X v2.3.1 has been on staging for 14 days without promotion to prod"

**FR-4.16.2 Drift Dashboard**
- Environment comparison matrix: Service × Environment with version numbers, color-coded for drift
- Filter by team, criticality, drift duration
- Historical drift timeline per service

---

## 5. Authentication & Authorization

### 5.1 Identity Provider
- **KeyCloak** as the identity server
- OIDC (OpenID Connect) protocol
- JWT tokens for API/CLI authentication
- Support for SSO via KeyCloak federation (SAML, LDAP, social providers)

> **ADRs:** [ADR-0006](../architecture/decisions/ADR-0006-keycloak-as-identity-provider.md), [ADR-0007](../architecture/decisions/ADR-0007-jwt-oidc-for-api-and-cli-auth.md)

### 5.2 Role-Based Access Control (RBAC)
- **Organization Admin:** Full control over org settings, teams, policies, billing
- **Team Admin:** Manage team members, owned components, team-level settings
- **Member:** View all, edit owned components, register new components
- **Viewer:** Read-only access (for external stakeholders or limited accounts)
- **API/CLI Service Account:** Machine-to-machine authentication for CI/CD

> **ADRs:** [ADR-0008](../architecture/decisions/ADR-0008-five-fixed-rbac-roles.md), [ADR-0009](../architecture/decisions/ADR-0009-service-account-jwt-model-for-cicd.md)

### 5.3 Status Page Access
- Public status page: No authentication required
- Internal status page option: Requires authentication (for internal-only service status)

> **ADRs:** [ADR-0010](../architecture/decisions/ADR-0010-internal-status-page-auth-via-keycloak.md)

---

## 6. Multi-Tenancy & Billing

### 6.1 Tenant Isolation
- Each organization is a single tenant
- Full data isolation between tenants
- Tenant-specific configuration, policies, branding

> **ADRs:** [ADR-0011](../architecture/decisions/ADR-0011-one-organization-equals-one-tenant.md)

### 6.2 Billing Model
- **Simple per-user pricing** per month
- User = any authenticated member of the organization
- Service accounts (CLI/API) do not count toward user limit
- Status page viewers (public) do not count toward user limit

> **ADRs:** [ADR-0062](../architecture/decisions/ADR-0062-external-billing-provider.md) (external billing provider), [ADR-0063](../architecture/decisions/ADR-0063-user-count-metering-per-billing-period.md) (user-count metering)

---

## 7. Non-Functional Requirements

### 7.1 Scale Targets
- **1000+ tenants**
- Per tenant: Up to 10,000 services, 5,000 users
- Total platform: Millions of entities
- Requires distributed, horizontally scalable architecture

> **ADRs:** [ADR-0074](../architecture/decisions/ADR-0074-scale-targets-1000-tenants.md) (scale envelope)

### 7.2 Availability & Performance
- Platform SLA target: 99.9% uptime
- Status page SLA target: 99.99% (must be more available than the services it monitors)
- API response time: < 200ms p95 for catalog reads
- Search response time: < 500ms p95
- Auto-import scan: < 5 minutes per repository (depending on size)

> **ADRs:** [ADR-0053](../architecture/decisions/ADR-0053-status-page-99-99-sla-target.md) (status page 99.99% SLA), [ADR-0055](../architecture/decisions/ADR-0055-scan-timeout-retry-rate-limit-aware.md) (scan resilience), [ADR-0075](../architecture/decisions/ADR-0075-performance-slos-p95-latency.md) (performance SLOs), [ADR-0076](../architecture/decisions/ADR-0076-two-tier-sla-platform-99-9-status-99-99.md) (two-tier SLA)

### 7.3 Security & Compliance
- Data encryption at rest and in transit
- Tenant data isolation (database-level or schema-level)
- Audit logging for all write operations
- Agent communication encrypted (mTLS)
- No storage of secrets/credentials (only references)

**GDPR Compliance (Day One):**
- Right to erasure: Full tenant data deletion on account termination
- Data portability: Export all tenant data in machine-readable format (JSON/CSV)
- Consent management: Clear consent flows for data collection
- Data Processing Agreement (DPA) template for enterprise customers
- Privacy by design: Minimal data collection, purpose limitation
- Data residency awareness: Track where tenant data is stored
- Breach notification process: 72-hour notification capability
- Data Protection Officer (DPO) contact published

**MiFID II Compliance (Day One):**
- Immutable audit trails for all configuration changes (who, what, when)
- Record retention: All audit logs retained for minimum 5 years (MiFID II requirement overrides 180-day default for financial services tenants)
- Communication records: All system-generated notifications and status updates retained
- Data integrity: Tamper-evident logging (append-only audit store)
- Tenant-level compliance flag: Mark tenants as "MiFID II regulated" to apply stricter retention and audit rules automatically

> **ADRs:** [ADR-0015](../architecture/decisions/ADR-0015-gdpr-compliance-from-day-one.md), [ADR-0016](../architecture/decisions/ADR-0016-mifid-ii-compliance-from-day-one.md), [ADR-0018](../architecture/decisions/ADR-0018-append-only-tamper-evident-audit-log.md), [ADR-0021](../architecture/decisions/ADR-0021-data-residency-tracking-per-tenant.md), [ADR-0050](../architecture/decisions/ADR-0050-notification-log-as-mifid-ii-record.md), [ADR-0078](../architecture/decisions/ADR-0078-no-secrets-stored-references-only.md) (no secrets stored — references only)

### 7.4 Data Retention Policies
| Data Type | Default Retention | MiFID II Tenants |
|-----------|------------------|------------------|
| Uptime history | 180 days | 5 years |
| Deployment history | 180 days | 5 years |
| Audit logs | 180 days | 5 years |
| Scan results / import history | 180 days | 5 years |
| Status page incident history | 180 days | 5 years |
| Deleted entity data | Purged after 30 days | 5 years (soft-delete) |

- Configurable per-tenant (can extend beyond defaults, not shorten below regulatory minimum)
- Automated archival to cold storage after active retention period
- Data export before purge (on request)

> **ADRs:** [ADR-0017](../architecture/decisions/ADR-0017-default-180-day-retention-5-year-mifid.md), [ADR-0019](../architecture/decisions/ADR-0019-soft-delete-with-30-day-purge.md), [ADR-0020](../architecture/decisions/ADR-0020-cold-storage-archival-after-active-retention.md)

### 7.5 Deployment Model
- **Cloud SaaS** as primary offering — cloud-agnostic, deployed on Kubernetes
- **Hybrid agents** deployed in customer infrastructure
- **Status page** deployed as separate K8s cluster/namespace for independent availability
- Design for multi-region data residency (future)

> **ADRs:** [ADR-0005](../architecture/decisions/ADR-0005-independent-data-replica-for-status-page.md), [ADR-0022](../architecture/decisions/ADR-0022-kubernetes-cloud-agnostic-deployment.md), [ADR-0023](../architecture/decisions/ADR-0023-status-page-as-separate-k8s-cluster.md)

---

## 8. Technology Stack

| Layer | Technology | Notes |
|-------|-----------|-------|
| Backend API | **.NET / ASP.NET Core** | Primary expertise, strong performance |
| Frontend | **React** | SPA with TypeScript |
| Identity | **KeyCloak** | OIDC, JWT, SSO federation |
| Database | **PostgreSQL** | Multi-tenant, scalable, strong JSON support |
| Search | **Elasticsearch** | Full-text documentation search, entity search |
| Message Bus | TBD (RabbitMQ/Kafka recommended) | Event-driven architecture for notifications, agent data |
| Agent | **.NET** | Consistent stack, cross-platform via .NET AOT compilation |
| CLI | **.NET global tool / standalone binary** | Cross-platform distribution, consistent with agent |
| Infrastructure | **Kubernetes** | Cloud-agnostic, container-based deployment |
| Status Page | **Separate K8s deployment** | Independent from main platform for higher availability |

> **ADRs:** [ADR-0001](../architecture/decisions/ADR-0001-postgresql-as-primary-database.md), [ADR-0002](../architecture/decisions/ADR-0002-elasticsearch-for-search.md), [ADR-0006](../architecture/decisions/ADR-0006-keycloak-as-identity-provider.md), [ADR-0022](../architecture/decisions/ADR-0022-kubernetes-cloud-agnostic-deployment.md), [ADR-0027](../architecture/decisions/ADR-0027-dotnet-aspnet-core-for-backend-api.md), [ADR-0039](../architecture/decisions/ADR-0039-react-spa-with-typescript.md), [ADR-0041](../architecture/decisions/ADR-0041-dotnet-agent-with-aot-compilation.md), [ADR-0046](../architecture/decisions/ADR-0046-dotnet-global-tool-cli-distribution.md)

---

## 9. MVP Phasing Strategy

Given the solo developer + AI agent constraint, the following phased approach is recommended:

### Phase 0: Foundation
- Project scaffolding, CI/CD setup, KeyCloak integration
- Database schema, multi-tenant data layer
- Core API structure, authentication/authorization

### Phase 1: Core Catalog (MVP v1.0)
- Entity CRUD (all types)
- Organization → Team → System → Component hierarchy
- Tag system
- Manual relationship management
- Basic entity search with filters
- Web UI: Entity list, detail pages, team pages

### Phase 2: Auto-Import (MVP v1.0)
- Git provider connection (GitHub, Azure DevOps)
- Single-repo deep scan and import
- Bulk org-wide scan
- Self-service onboarding wizard
- Scorecard system with required minimum fields

### Phase 3: Documentation (MVP v1.0)
- Markdown import and rendering from git
- API spec detection and rendering (OpenAPI)
- Documentation search
- Cross-service linking

### Phase 4: Status Page (v1.1)
- Public status page with branding
- Manual status management
- Subscriber notifications (email, webhook)
- Uptime history and charts
- Scheduled maintenance

### Phase 5: CLI & Policy (v1.2)
- CLI tool for CI/CD integration
- Deployment event reporting
- Policy definition and enforcement
- Scorecard validation from CLI

### Phase 6: Health Monitoring & Agent (v1.3)
- Hybrid agent (health checks + metrics + discovery)
- Prometheus/Grafana integration
- Automated uptime tracking
- Agent-discovered services with approval workflow

### Phase 7: Intelligence & Governance (v1.4)
- Service Maturity Model (auto-calculated L1-L5)
- Dependency Risk Scoring with risk heatmap
- Developer Experience Score (DX Score)
- Multi-environment Drift Detection with alerts

### Phase 8: Advanced Analytics (v1.5)
- API Changelog & Breaking Change Detection
- Change Impact Preview (blast radius analysis in CI/CD)
- Built-in Tech Radar (auto-populated from catalog)
- Cost Attribution per Service

### Phase 9: Advanced Features (v2.0+)
- Dependency graph visualization (interactive)
- Environment maps
- Scheduled re-scans
- Incident management
- Native Slack/Teams notifications
- Plugin architecture
- AI-powered search
- Multi-region deployment

---

## 10. Key Differentiators vs. Competitors

| Differentiator | vs. Backstage | vs. Compass |
|---------------|---------------|-------------|
| **Managed SaaS** | Backstage is self-hosted, requires significant ops investment | Compass is SaaS but tied to Atlassian ecosystem |
| **Self-service onboarding** | Backstage onboarding requires templates, YAML, developer effort | Compass requires manual setup for each component |
| **Auto-import (deep scan)** | Backstage requires catalog-info.yaml in each repo | Compass has limited auto-discovery |
| **CI/CD CLI with policy enforcement** | Backstage has no built-in CI/CD integration | Compass has limited CLI capabilities |
| **Public status page (built-in)** | Not available in Backstage | Requires separate Statuspage product (additional cost) |
| **Opinionated, batteries-included** | Backstage is a framework — you build your own portal | Compass is opinionated but limited in scope |
| **Hybrid agents** | No equivalent | No equivalent |
| **API Breaking Change Detection** | No built-in API diffing or consumer alerting | No built-in API compatibility checks |
| **Developer Experience Score** | Backstage has scorecards but no composite DX metric | Compass has basic scorecards, no DX focus |
| **Dependency Risk Scoring** | No automated risk assessment combining multiple signals | Basic health checks only, no risk aggregation |
| **Cost Attribution per Service** | No cost tracking | No cost tracking |
| **Service Maturity Model** | No built-in maturity framework | Basic maturity via scorecards, not multi-level |
| **Change Impact Preview (Blast Radius)** | No equivalent | No equivalent |
| **Built-in Tech Radar** | Requires custom plugin | No equivalent |
| **Multi-Environment Drift Detection** | No equivalent | No equivalent |

---

## 11. Resolved Decisions

| # | Question | Decision | ADR |
|---|----------|----------|-----|
| 1 | Product name | **Kartova** | — |
| 2 | Database technology | **PostgreSQL** | [ADR-0001](../architecture/decisions/ADR-0001-postgresql-as-primary-database.md) |
| 3 | Search technology | **Elasticsearch** | [ADR-0002](../architecture/decisions/ADR-0002-elasticsearch-for-search.md) |
| 4 | Agent technology | **.NET** (consistent stack, AOT for small footprint) | [ADR-0041](../architecture/decisions/ADR-0041-dotnet-agent-with-aot-compilation.md) |
| 5 | Hosting provider | **Cloud-agnostic, Kubernetes** | [ADR-0022](../architecture/decisions/ADR-0022-kubernetes-cloud-agnostic-deployment.md) |
| 6 | Status page infrastructure | **Separate deployment** (independent availability) | [ADR-0023](../architecture/decisions/ADR-0023-status-page-as-separate-k8s-cluster.md) |
| 7 | Data retention | **180 days default**, 5 years for MiFID II tenants | [ADR-0017](../architecture/decisions/ADR-0017-default-180-day-retention-5-year-mifid.md) |
| 8 | Compliance | **GDPR + MiFID II** from day one | [ADR-0015](../architecture/decisions/ADR-0015-gdpr-compliance-from-day-one.md), [ADR-0016](../architecture/decisions/ADR-0016-mifid-ii-compliance-from-day-one.md) |
| 9 | Open source strategy | **Fully proprietary** — no open-source components | [ADR-0026](../architecture/decisions/ADR-0026-fully-proprietary-no-open-source-core.md) |
| 10 | Beta/early access strategy | **See Section 12** | [ADR-0079](../architecture/decisions/ADR-0079-dogfooding-design-partners-gtm.md) |

---

## 12. Beta / Early Access Strategy

**Selected approach: Dogfooding + Design Partners**

### Stage 1: Dogfooding (During Phase 0-2 development)
- Use Kartova to track your own projects, services, and infrastructure
- You are both developer and first user — experience every friction point firsthand
- Document pain points and UX issues as you encounter them
- Validate the onboarding wizard, auto-import, and core catalog by using them on real repos

### Stage 2: Design Partners (Once Phase 2-3 are functional)
- Recruit **2-3 design partners** from professional network
- Target criteria: Teams with 10-50 services, active CI/CD, pain with current tooling
- Free access in exchange for regular feedback and case study permission
- White-glove onboarding by you — observe friction points in real time
- Consider targeting fintech teams (MiFID II compliance is already built in — a selling point)

### Stage 3: Closed Beta (Post design-partner feedback incorporation)
- Open to a broader set of **10-20 tenants** via waitlist
- Self-service onboarding (validated through dogfooding and partners)
- Feedback via community channel (Discord/Slack)
- Convert to paid at GA

> **ADRs:** [ADR-0079](../architecture/decisions/ADR-0079-dogfooding-design-partners-gtm.md) (dogfooding + design partners GTM)

---

## Appendix A: Glossary

| Term | Definition |
|------|-----------|
| **Entity** | Any tracked item in the catalog (application, service, API, etc.) |
| **System** | A logical grouping of related entities that form a product or capability |
| **Scorecard** | A completeness/quality score for an entity based on configurable rules |
| **Deep Scan** | Full analysis of a repository to extract metadata, dependencies, and configuration |
| **Hybrid Agent** | A lightweight process deployed in customer infrastructure that reports to the platform |
| **Policy** | An organizational rule that can be enforced via CLI in CI/CD pipelines |
| **AsyncAPI** | Specification for defining asynchronous/event-driven APIs (analogous to OpenAPI for sync APIs) |
| **CloudEvents** | A CNCF specification for describing event data in a common, interoperable format |
| **Schema Registry** | A centralized store for message schemas (e.g., Confluent, Apicurio) that tracks schema versions and compatibility |

---

*This document will be decomposed into Epics, Features, and User Stories in the next phase.*
