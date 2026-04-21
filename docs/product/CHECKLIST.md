# Kartova — Development Progress Checklist

**Last updated:** 2026-04-16

## How to use
- [ ] = Not started
- [x] = Completed
- Mark stories as you complete them during development

## Progress Summary

| Phase | Status | Progress |
|-------|--------|----------|
| Phase 0: Foundation | Not Started | 0/33 |
| Phase 1: Core Catalog & Notifications | Not Started | 0/55 |
| Phase 2: Auto-Import | Not Started | 0/36 |
| Phase 3: Documentation | Not Started | 0/15 |
| Phase 4: Status Page | Not Started | 0/16 |
| Phase 5: CLI, Policy & Billing | Not Started | 0/15 |
| Phase 6: Agent & Monitoring | Not Started | 0/12 |
| Phase 7: Intelligence | Not Started | 0/13 |
| Phase 8: Analytics | Not Started | 0/14 |
| Phase 9: Advanced | Not Started | 0/0 |
| **Total** | | **0/209** |

---

## Phase 0: Foundation (33 stories)

### E-01: Project Foundation & Infrastructure

**E-01.F-01: Project Scaffolding**
- [x] E-01.F-01.S-01 — .NET solution structure with clean architecture
- [x] E-01.F-01.S-02 — React frontend project with TypeScript
- [x] E-01.F-01.S-03 — Docker Compose for local development

**E-01.F-02: CI/CD Pipeline**
- [ ] E-01.F-02.S-01 — CI pipeline (build, test, lint)
- [ ] E-01.F-02.S-02 — CD pipeline to staging

**E-01.F-03: Database Foundation**
- [ ] E-01.F-03.S-01 — Multi-tenant database schema with tenant isolation
- [ ] E-01.F-03.S-02 — Database migration framework
- [ ] E-01.F-03.S-03 — Append-only audit log table (MiFID II)

**E-01.F-04: Authentication & Authorization**
- [ ] E-01.F-04.S-01 — KeyCloak configured with OIDC
- [ ] E-01.F-04.S-02 — JWT validation middleware in API
- [ ] E-01.F-04.S-03 — RBAC with five roles
- [ ] E-01.F-04.S-04 — SSO login via web UI

**E-01.F-05: Data Retention & Compliance Infrastructure**
- [ ] E-01.F-05.S-01 — Data retention engine with configurable purge
- [ ] E-01.F-05.S-02 — Tenant-level MiFID II compliance flag
- [ ] E-01.F-05.S-03 — Data export in JSON/CSV (GDPR portability)
- [ ] E-01.F-05.S-04 — Full data deletion on account termination
- [ ] E-01.F-05.S-05 — GDPR consent flows during registration
- [ ] E-01.F-05.S-06 — Breach notification workflow (72-hour)
- [ ] E-01.F-05.S-07 — Notification retention as communication records
- [ ] E-01.F-05.S-08 — Data residency tracking per tenant

**E-01.F-06: Platform API Infrastructure**
- [ ] E-01.F-06.S-01 — API versioning strategy
- [ ] E-01.F-06.S-02 — Per-tenant rate limiting
- [ ] E-01.F-06.S-03 — Bulk operation endpoints
- [ ] E-01.F-06.S-04 — Webhook registration and management
- [ ] E-01.F-06.S-05 — Webhook retry with exponential backoff
- [ ] E-01.F-06.S-06 — Auto-generated OpenAPI spec (dogfooding)

**E-01.F-07: Platform Observability**
- [ ] E-01.F-07.S-01 — Health check endpoints for all services
- [ ] E-01.F-07.S-02 — Structured logging across components
- [ ] E-01.F-07.S-03 — Platform metrics (latency, errors, queues)
- [ ] E-01.F-07.S-04 — Alerting on failures and SLA breaches

**E-01.F-08: Performance & Scalability Baseline**
- [ ] E-01.F-08.S-01 — Database indexing strategy for multi-tenant scale
- [ ] E-01.F-08.S-02 — Elasticsearch index strategy
- [ ] E-01.F-08.S-03 — Row-level security for tenant isolation

---

## Phase 1: Core Catalog & Notifications (52 stories)

### E-02: Entity Registry

**E-02.F-01: Application Entity Management**
- [ ] E-02.F-01.S-01 — Register new application in catalog
- [ ] E-02.F-01.S-02 — Application detail page with metadata
- [ ] E-02.F-01.S-03 — Edit application metadata
- [ ] E-02.F-01.S-04 — Application lifecycle status transitions

**E-02.F-02: Service Entity Management**
- [ ] E-02.F-01.S-05 — Required minimum fields on all entity types
- [ ] E-02.F-02.S-01 — Register service with endpoints and protocol
- [ ] E-02.F-02.S-02 — Service detail page with health and consumers

**E-02.F-03: API Entity Management (Sync & Async)**
- [ ] E-02.F-03.S-01 — Register sync API (REST/gRPC/GraphQL)
- [ ] E-02.F-03.S-02 — Register async API with AsyncAPI spec
- [ ] E-02.F-03.S-03 — Unified sync/async API view per service

**E-02.F-04: Infrastructure & Broker Entity Management**
- [ ] E-02.F-04.S-01 — Register infrastructure components
- [ ] E-02.F-04.S-02 — Register message brokers with queues/topics

**E-02.F-05: Environment & Deployment Tracking**
- [ ] E-02.F-05.S-01 — Register environments with infra details
- [ ] E-02.F-05.S-02 — Record deployment events
- [ ] E-02.F-05.S-03 — Version-per-environment matrix view

### E-03: Organization & Team Management

**E-03.F-01: Organization Management**
- [ ] E-03.F-01.S-01 — Configure organization profile
- [ ] E-03.F-01.S-02 — Invite users with specific roles

**E-03.F-02: Team Management**
- [ ] E-03.F-02.S-01 — Create and manage team profile
- [ ] E-03.F-02.S-02 — Assign components to team
- [ ] E-03.F-02.S-03 — Team page with components and scorecard

**E-03.F-03: System Grouping**
- [ ] E-03.F-03.S-01 — Create System and assign components
- [ ] E-03.F-03.S-02 — Browse catalog by Org/Team/System hierarchy

**E-03.F-04: Tag System**
- [ ] E-03.F-04.S-01 — Define tag taxonomies
- [ ] E-03.F-04.S-02 — Tag entities with multiple tags
- [ ] E-03.F-04.S-03 — Filter catalog by tag combinations

**E-03.F-05: Multi-Ownership**
- [ ] E-03.F-05.S-01 — Mark component as shared with co-owners
- [ ] E-03.F-05.S-02 — Dedicated shared/platform components view
- [ ] E-03.F-05.S-03 — Co-ownership permission rules
- [ ] E-03.F-05.S-04 — Clean ownership transfer on team deletion

### E-04: Entity Relationships

**E-04.F-01: Manual Relationship Management**
- [ ] E-04.F-01.S-01 — Create relationship between entities
- [ ] E-04.F-01.S-02 — View relationships with origin distinction
- [ ] E-04.F-01.S-03 — Promote auto-discovered to manual (pin)
- [ ] E-04.F-01.S-04 — Demote manual to auto-managed (unpin)

**E-04.F-02: Relationship Visualization**
- [ ] E-04.F-02.S-01 — Embedded mini dependency graph (entity Dependencies tab)
- [ ] E-04.F-02.S-02 — Relationship table below mini-graph
- [ ] E-04.F-02.S-03 — "Open full graph" button linking to standalone
- [ ] E-04.F-02.S-04 — Standalone Dependency Graph Explorer (/graph)
- [ ] E-04.F-02.S-05 — Graph filters (team, domain, criticality, origin)
- [ ] E-04.F-02.S-06 — Visual impact analysis on standalone graph

### E-05: Search

**E-05.F-01: Entity Search**
- [ ] E-05.F-01.S-01 — Search entities by name with instant results
- [ ] E-05.F-01.S-02 — Filter search by type, team, tags, owner
- [ ] E-05.F-01.S-03 — Search results with key metadata

### E-06: Dashboards & Visualizations (Core)

**E-06.F-01: Catalog Home Dashboard**
- [ ] E-06.F-01.S-01 — Home dashboard with recent activity and search

**E-06.F-02: Team Dashboard**
- [ ] E-06.F-02.S-01 — Team dashboard with components and health

**E-06.F-03: Organization Overview Dashboard**
- [ ] E-06.F-03.S-01 — Org overview with entity counts and health

**E-06.F-04: Environment Map Dashboard**
- [ ] E-06.F-04.S-01 — Environment map (service x env with versions)

**E-06.F-05: Status Board Dashboard**
- [ ] E-06.F-05.S-01 — Status board with health overview

### E-06a: Notification Infrastructure

**E-06a.F-01: Notification Dispatch Engine**
- [ ] E-06a.F-01.S-01 — Multi-channel notification dispatch engine
- [ ] E-06a.F-01.S-02 — In-app notification center (bell icon)
- [ ] E-06a.F-01.S-03 — Email notifications for important events
- [ ] E-06a.F-01.S-04 — Outbound webhook notifications

**E-06a.F-02: Notification Preferences & Policies**
- [ ] E-06a.F-02.S-01 — User notification preferences
- [ ] E-06a.F-02.S-02 — Organization-level notification policies

**E-06a.F-03: Native Integrations (Slack & Teams)**
- [ ] E-06a.F-03.S-01 — Slack integration with channel notifications
- [ ] E-06a.F-03.S-02 — Microsoft Teams integration

---

## Phase 2: Auto-Import (36 stories)

### E-07: Git Provider Integration

**E-07.F-01: GitHub Integration**
- [ ] E-07.F-01.S-01 — Connect GitHub organization via OAuth
- [ ] E-07.F-01.S-02 — List repositories from connected GitHub org
- [ ] E-07.F-01.S-03 — GitHub webhooks trigger doc re-sync

**E-07.F-02: Azure DevOps Integration**
- [ ] E-07.F-02.S-01 — Connect Azure DevOps organization via OAuth
- [ ] E-07.F-02.S-02 — List repositories across Azure DevOps projects
- [ ] E-07.F-02.S-03 — Azure DevOps service hooks trigger re-sync

### E-08: Auto-Import Engine

**E-08.F-01: Single Repository Deep Scan**
- [ ] E-08.F-01.S-01 — Scan repo for code metadata (lang, framework)
- [ ] E-08.F-01.S-02 — Detect infrastructure definitions
- [ ] E-08.F-01.S-03 — Detect sync API specs (OpenAPI/gRPC/GraphQL)
- [ ] E-08.F-01.S-04 — Detect async API specs (AsyncAPI/CloudEvents)
- [ ] E-08.F-01.S-05 — Detect messaging config (queues/brokers)
- [ ] E-08.F-01.S-06 — Detect database connections and migrations
- [ ] E-08.F-01.S-07 — Detect environment variable names
- [ ] E-08.F-01.S-08 — Import README and docs/ folder content
- [ ] E-08.F-01.S-09 — Review scan results before confirming import

**E-08.F-02: Bulk Organization Scan**
- [ ] E-08.F-02.S-01 — Scan all repos in GitHub org at once
- [ ] E-08.F-02.S-02 — Filter repos by name, language, activity
- [ ] E-08.F-02.S-03 — Review bulk scan results with diff view

**E-08.F-03: Scheduled Re-scan**
- [ ] E-08.F-03.S-01 — Configure periodic re-scanning schedule
- [ ] E-08.F-03.S-02 — Re-scan diff showing what changed
- [ ] E-08.F-03.S-03 — Re-scans never override manual relationships
- [ ] E-08.F-03.S-04 — Conflict review queue for re-scan conflicts

**E-08.F-04: Scan Resilience & Error Handling**
- [ ] E-08.F-04.S-01 — Graceful handling of malformed files
- [ ] E-08.F-04.S-02 — Respect Git provider API rate limits
- [ ] E-08.F-04.S-03 — Scan timeout retry with status reporting
- [ ] E-08.F-04.S-04 — Preserve partial scan results on failure

### E-09: Self-Service Onboarding Wizard

**E-09.F-01: Onboarding Wizard Flow**
- [ ] E-09.F-01.S-01 — Create organization via guided wizard
- [ ] E-09.F-01.S-02 — Connect Git provider as wizard step
- [ ] E-09.F-01.S-03 — Wizard scans repos and shows preview
- [ ] E-09.F-01.S-04 — Confirm import with summary
- [ ] E-09.F-01.S-05 — Progress indicators and contextual help

### E-10: Scorecards & Data Quality

**E-10.F-01: Scorecard System**
- [ ] E-10.F-01.S-01 — Define scorecard rules per category
- [ ] E-10.F-01.S-02 — Completeness score (0-100%) per entity
- [ ] E-10.F-01.S-03 — Org-wide scorecard compliance dashboard

**E-10.F-02: Nudge System**
- [ ] E-10.F-02.S-01 — Scorecard threshold notifications
- [ ] E-10.F-02.S-02 — Actionable suggestions to improve score

---

## Phase 3: Documentation (15 stories)

### E-11: Documentation Management

**E-11.F-01: Git-Synced Markdown Documentation**
- [ ] E-11.F-01.S-01 — Import and render markdown from docs/ folder
- [ ] E-11.F-01.S-02 — Auto-sync docs on git push
- [ ] E-11.F-01.S-03 — Navigation sidebar for multi-page docs

**E-11.F-02: Sync API Documentation (OpenAPI/gRPC/GraphQL)**
- [ ] E-11.F-02.S-01 — Render OpenAPI specs as interactive docs
- [ ] E-11.F-02.S-02 — Render gRPC proto files as browsable docs
- [ ] E-11.F-02.S-03 — Versioned API docs aligned with deployments

**E-11.F-03: Async API Documentation**
- [ ] E-11.F-03.S-01 — Render AsyncAPI specs (v2.x/v3.x)
- [ ] E-11.F-03.S-02 — Render CloudEvents metadata with AsyncAPI
- [ ] E-11.F-03.S-03 — Schema registry display with version history
- [ ] E-11.F-03.S-04 — Unified sync + async API view per service

**E-11.F-04: Documentation Hub per Service**
- [ ] E-11.F-04.S-01 — Documentation hub with tabbed navigation
- [ ] E-11.F-04.S-02 — Auto-generated changelog from git history

**E-11.F-05: Cross-Service Referencing & Search**
- [ ] E-11.F-05.S-01 — Auto-link service references in docs
- [ ] E-11.F-05.S-02 — Full-text search across all documentation
- [ ] E-11.F-05.S-03 — Related services suggestions per service

---

## Phase 4: Status Page (16 stories)

### E-12: Public Status Page

**E-12.F-01: Status Page Configuration**
- [ ] E-12.F-01.S-01 — Configure branding (logo, colors, CSS)
- [ ] E-12.F-01.S-02 — Custom domain setup
- [ ] E-12.F-01.S-03 — Auto SSL certificate provisioning
- [ ] E-12.F-01.S-04 — Choose exposed services and grouping
- [ ] E-12.F-01.S-05 — Internal-only (authenticated) status page

**E-12.F-02: Status Management**
- [ ] E-12.F-02.S-01 — Manually set service public status
- [ ] E-12.F-02.S-02 — Create incidents with status updates
- [ ] E-12.F-02.S-03 — Schedule maintenance windows

**E-12.F-03: Subscriber Notifications**
- [ ] E-12.F-03.S-01 — Subscribe via email, SMS, webhook, RSS
- [ ] E-12.F-03.S-02 — Notifications on status change or incident
- [ ] E-12.F-03.S-03 — Choose components for notifications

**E-12.F-04: Uptime History & Charts**
- [ ] E-12.F-04.S-01 — Historical uptime percentage per component
- [ ] E-12.F-04.S-02 — Past incident history timeline

**E-12.F-05: Status Page Infrastructure & HA**
- [ ] E-12.F-05.S-01 — Separate K8s deployment for status page
- [ ] E-12.F-05.S-02 — Data sync from main platform to status page
- [ ] E-12.F-05.S-03 — Health monitoring for status page service

---

## Phase 5: CLI, Policy & Billing (15 stories)

### E-13: CLI Tool

**E-13.F-01: CLI Core**
- [ ] E-13.F-01.S-01 — Install CLI as .NET global tool or binary
- [ ] E-13.F-01.S-02 — CLI authentication with service account JWT
- [ ] E-13.F-01.S-03 — Register or update component from CLI

**E-13.F-02: Deployment Reporting**
- [ ] E-13.F-02.S-01 — Report deployment event from CI/CD
- [ ] E-13.F-02.S-02 — Report health check results from CI/CD

**E-13.F-03: Validation & Scanning**
- [ ] E-13.F-03.S-01 — Validate catalog entry completeness via CLI
- [ ] E-13.F-03.S-02 — Trigger repository re-scan from CLI

### E-14: Policy Engine

**E-14.F-01: Policy Definition**
- [ ] E-14.F-01.S-01 — Define policies in web UI
- [ ] E-14.F-01.S-02 — Policy compliance dashboard

**E-14.F-02: CLI Policy Enforcement**
- [ ] E-14.F-02.S-01 — Run policy-check in CI/CD
- [ ] E-14.F-02.S-02 — Configurable warning vs error severity

### E-14a: Billing & Subscription Management

**E-14a.F-01: Billing Integration**
- [ ] E-14a.F-01.S-01 — User count tracking per organization
- [ ] E-14a.F-01.S-02 — Billing provider integration (Stripe)
- [ ] E-14a.F-01.S-03 — Billing dashboard for tenant admins
- [ ] E-14a.F-01.S-04 — Payment method management and invoices

---

## Phase 6: Agent & Monitoring (12 stories)

### E-15: Hybrid Agent

**E-15.F-01: Agent Deployment & Communication**
- [ ] E-15.F-01.S-01 — Deploy agent as Docker/K8s Deployment
- [ ] E-15.F-01.S-02 — Secure outbound-only mTLS communication
- [ ] E-15.F-01.S-03 — Configure agent from platform UI

**E-15.F-02: Health Checks**
- [ ] E-15.F-02.S-01 — HTTP/TCP/gRPC health probes
- [ ] E-15.F-02.S-02 — Health probes update catalog and status page

**E-15.F-03: Metrics Collection**
- [ ] E-15.F-03.S-01 — Scrape and forward Prometheus metrics

**E-15.F-04: Service Discovery**
- [ ] E-15.F-04.S-01 — Discover services in K8s cluster
- [ ] E-15.F-04.S-02 — Approval workflow for discovered services

### E-16: Monitoring Integrations

**E-16.F-01: Prometheus Integration**
- [ ] E-16.F-01.S-01 — Define uptime rules from PromQL queries
- [ ] E-16.F-01.S-02 — Uptime calculation per service per environment

**E-16.F-02: Grafana Cloud Integration**
- [ ] E-16.F-02.S-01 — Link Grafana dashboards to services

---

## Phase 7: Intelligence (13 stories)

### E-17: Service Maturity Model

**E-17.F-01: Maturity Calculation & Display**
- [ ] E-17.F-01.S-01 — Display maturity level (L1-L5) per service
- [ ] E-17.F-01.S-02 — Next-level progression path and checklist
- [ ] E-17.F-01.S-03 — Customize maturity level requirements

**E-17.F-02: Maturity Dashboards**
- [ ] E-17.F-02.S-01 — Maturity distribution dashboard

### E-18: Dependency Risk Scoring

**E-18.F-01: Risk Calculation & Heatmap**
- [ ] E-18.F-01.S-01 — Automated risk score per entity
- [ ] E-18.F-01.S-02 — Risk heatmap dashboard
- [ ] E-18.F-01.S-03 — Risk threshold alerts

### E-19: Developer Experience Score

**E-19.F-01: DX Score Calculation & Suggestions**
- [ ] E-19.F-01.S-01 — DX Score (0-100) per service
- [ ] E-19.F-01.S-02 — Actionable DX improvement suggestions
- [ ] E-19.F-01.S-03 — DX Score trends and leaderboards

### E-20: Multi-Environment Drift Detection

**E-20.F-01: Drift Monitoring & Dashboard**
- [ ] E-20.F-01.S-01 — Detect stale staging deployments
- [ ] E-20.F-01.S-02 — Environment comparison matrix
- [ ] E-20.F-01.S-03 — Config and infrastructure drift detection

---

## Phase 8: Analytics (14 stories)

### E-21: API Changelog & Breaking Change Detection

**E-21.F-01: API Version Tracking**
- [ ] E-21.F-01.S-01 — API spec version history
- [ ] E-21.F-01.S-02 — Automatic diff between API spec versions

**E-21.F-02: Breaking Change Detection & Alerting**
- [ ] E-21.F-02.S-01 — Classify changes as compatible/breaking
- [ ] E-21.F-02.S-02 — Notify consumers before breaking changes
- [ ] E-21.F-02.S-03 — CLI api-check to detect breaking changes

### E-22: Change Impact Preview

**E-22.F-01: Blast Radius Analysis**
- [ ] E-22.F-01.S-01 — CLI impact-check with tier breakdown
- [ ] E-22.F-01.S-02 — Visual blast radius graph in web UI
- [ ] E-22.F-01.S-03 — Impact report as PR comment

### E-23: Built-in Tech Radar

**E-23.F-01: Tech Radar Visualization & Governance**
- [ ] E-23.F-01.S-01 — Tech Radar auto-populated from catalog
- [ ] E-23.F-01.S-02 — Override ring placement for technologies
- [ ] E-23.F-01.S-03 — Alert on adoption of "Hold" technologies

### E-24: Cost Attribution

**E-24.F-01: Cost Tracking & Dashboards**
- [ ] E-24.F-01.S-01 — Map infrastructure costs to services
- [ ] E-24.F-01.S-02 — Monthly cost dashboard with trends
- [ ] E-24.F-01.S-03 — Cost anomaly alerts

---

## Phase 9: Advanced (stories deferred to v2.0)

### E-25: Incident Management
*(Stories deferred to v2.0 planning)*

### E-26: Plugin Architecture
*(Stories deferred to v2.0 planning)*

### E-27: AI-Powered Search
*(Stories deferred to v2.0 planning)*

### E-28: Multi-Region Deployment
*(Stories deferred to v2.0 planning)*
