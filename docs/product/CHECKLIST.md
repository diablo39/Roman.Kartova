# Kartova — Development Progress Checklist

**Last updated:** 2026-06-22

## How to use
- [ ] = Not started
- [x] = Completed
- Mark stories as you complete them during development

## Progress Summary

| Phase | Status | Progress |
|-------|--------|----------|
| Phase 0: Foundation | In Progress | 12/31 |
| Phase 1: Core Catalog & Notifications | In Progress | 16/60 |
| Phase 2: Auto-Import | Not Started | 0/36 |
| Phase 3: Documentation | Not Started | 0/15 |
| Phase 4: Status Page | Not Started | 0/16 |
| Phase 5: CLI, Policy & Billing | Not Started | 0/15 |
| Phase 6: Agent & Monitoring | Not Started | 0/12 |
| Phase 7: Intelligence | Not Started | 0/13 |
| Phase 8: Analytics | Not Started | 0/14 |
| Phase 9: Advanced | Not Started | 0/0 |
| **Total** | | **23/212** |

---

## Phase 0: Foundation (31 stories; 2 dropped — ADR-0106)

### E-01: Project Foundation & Infrastructure

**E-01.F-01: Project Scaffolding**
- [x] E-01.F-01.S-01 — .NET solution structure with clean architecture
- [x] E-01.F-01.S-02 — React frontend project with TypeScript
- [x] E-01.F-01.S-03 — Docker Compose for local development
- [x] E-01.F-01.S-04 — Dev-stack seed data (Org A organization row matching realm tenant_id) (slice-4-cleanup — PR #18, 2026-05-01)

**E-01.F-02: CI/CD Pipeline**
- [ ] E-01.F-02.S-01 — CI pipeline (build, test, lint)
- [ ] E-01.F-02.S-02 — CD pipeline to staging
- [ ] E-01.F-02.S-03 — End-to-end test infrastructure (checked-in Playwright suite)

**E-01.F-03: Database Foundation**
- [x] E-01.F-03.S-01 — Multi-tenant database schema with tenant isolation
- [x] E-01.F-03.S-02 — Database migration framework (satisfied by ADR-0085 impl, verified 2026-06-19: `Kartova.Migrator` per-module `MigrateAsync` loop + `__kartova_metadata`, RLS/REVOKE DDL carried in EF migrations across all modules, DevSeed prod-guard, docker-compose `migrator` init service, Helm `pre-install/pre-upgrade` Job, CI image build, migration integration tests). Follow-ups (optional, not blocking): DDL/DML least-privilege credential split → with deploy hardening (E-01.F-04.S-05); CD-time migrator invocation → E-01.F-02.S-02; documented `--module=<name>` selective flag is unwired (Program.cs always runs all modules)
- [x] E-01.F-03.S-03 — Append-only audit log table (security forensics + GDPR accountability; MiFID II driver dropped per ADR-0106, log retained on security grounds) — Phase 1 foundation (audit-log-foundation, 2026-06-12): Kartova.Audit module, insert-only/RLS audit_log table (DB-enforced REVOKE + tenant_isolation policy), IAuditWriter (sync in-transaction, fail-closed), per-tenant SHA-256 hash chain + AuditChainVerifier (ADR-0018). Event wiring = Phase 2. Phase 2 (audit-event-wiring, 2026-06-17): 10 Organization mutations wired to IAuditWriter (member role-change/offboard, team CRUD + membership, invitation.created, org.profile_updated); actor_display snapshot from JWT. Catalog events + System-actor/expiry-sweep deferred. Phase 2 follow-up (audit-system-actor-sweep, 2026-06-18): IAuditWriter.AppendSystemAsync (System actor, null actor_id, "System" display) + invitation-expiry sweep refactored to per-tenant ITenantScope txn writing one System invitation.expired row per expiry (RLS app-role + hash chain). Phase 2 follow-up (audit-catalog-event-wiring, 2026-06-19): 7 Catalog application mutations wired to IAuditWriter via the direct-dispatch delegates — application.registered/edited/team_assigned + a single application.lifecycle_changed (from/to/sunsetDate in data) across deprecate/decommission/reactivate/un-decommission. Audit event-wiring fully closed.

**E-01.F-04: Authentication & Authorization**
- [x] E-01.F-04.S-01 — KeyCloak configured with OIDC
- [x] E-01.F-04.S-02 — JWT validation middleware in API
- [x] E-01.F-04.S-03 — RBAC with five roles (slice 7 — PR #24, 2026-05-22; granular permission model with role→permission map; ServiceAccount deferred to Phase 5). Refined by ADR-0101 (2026-06-09): the `TeamAdmin` realm role + its `team.*` mutation claims were removed — team-admin authority is now a per-team `Admin` membership via the `TeamAdminOfThis` resource gate (realm roles: Viewer/Member/OrgAdmin). Eliminates the silent-403 footgun where a realm-Member promoted to team Admin couldn't manage their team.
- [x] E-01.F-04.S-04 — SSO login via web UI (slice 7 — PR #24, 2026-05-22; existing OIDC redirect flow satisfies the story; signed-out landing page deferred to §15.9)
- [ ] E-01.F-04.S-05 — BFF cookie-session auth (security hardening, post-MVP)

**E-01.F-05: Data Retention & Compliance Infrastructure**
- [ ] E-01.F-05.S-01 — Data retention engine (flat 180-day purge, all tenants — ADR-0106)
- [~] ~~E-01.F-05.S-02 — Tenant-level MiFID II compliance flag~~ — DROPPED (ADR-0106: no regulatory tier)
- [ ] E-01.F-05.S-03 — Data export in JSON/CSV (GDPR portability)
- [ ] E-01.F-05.S-04 — Full data deletion on account termination
- [ ] E-01.F-05.S-05 — GDPR consent flows during registration
- [ ] E-01.F-05.S-06 — Breach notification workflow (72-hour)
- [~] ~~E-01.F-05.S-07 — Notification retention as communication records~~ — DROPPED (ADR-0106: operational log only)
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
- [x] E-01.F-08.S-03 — Row-level security for tenant isolation

**Cross-cutting: API Contract Infrastructure**
- [x] **Cross-cutting: cursor-pagination contract.** ADR-0095 + reference impl on Applications list. (2026-05-04)

---

## Phase 1: Core Catalog & Notifications (54 stories)

### E-02: Entity Registry

**E-02.F-01: Application Entity Management** *(+ list filter (displayName search) + displayName-asc default sort + FilterBar collapsible panel — list-filter-surface-catalog, 2026-06-22; + lifecycle & team multi-select filters — PR #41, 2026-06-24: replaces the `includeDecommissioned` checkbox; reusable `MultiSelect` control + `multiFilters` repeated-param URL axis; pulled the team/lifecycle facets forward from E-05)*
- [x] E-02.F-01.S-01 — Register new application in catalog (slice 3 — PR #10, 2026-04-30; UI surface added in slice 4 — PR #17, 2026-04-30; TimeProvider on Application.Create — slice 6, PR #22, 2026-05-07; slice-10 amendment 2026-06-10: required owning team (`TeamId`), created-by provenance (`CreatedByUserId` immutable), membership-gated registration — ADR-0103)
- [x] E-02.F-01.S-02 — Application detail page with metadata (slice 4 — PR #17, 2026-04-30; header + metadata only, tabs deferred)
- [x] E-02.F-01.S-03 — Edit application metadata (slice 5 — PR #21, 2026-05-06; PUT /api/v1/catalog/applications/{id} with If-Match/ETag optimistic concurrency, ADR-0096)
- [x] E-02.F-01.S-04 — Application lifecycle status transitions (slice 5 — PR #21, 2026-05-06; ADR-0073 Active → Deprecated → Decommissioned linear forward, sunsetDate strict; admin override + audit + notifications deferred to follow-up slices; default-view filter — slice 6, PR #22, 2026-05-07; backward transitions (Reactivate, UnDecommission) — slice 7, PR #24, 2026-05-22; sunset-date admin override remains follow-up §15.1)
- [x] E-02.F-01.S-06 — Field-level ProblemDetails errors (slice-4-cleanup — PR #18, 2026-05-01)
- [x] E-02.F-01.S-07 — Move kebab-case Name validation into `Application.Create` domain invariant (slice-4-cleanup — PR #18, 2026-05-01)

**E-02.F-02: Service Entity Management** *(+ list filter (displayName search) + displayName-asc default sort + FilterBar collapsible panel — list-filter-surface-catalog, 2026-06-22; + team & health multi-select filters — on master, 2026-06-24: mirrors the Applications team filter; Services have no lifecycle so no default-hide — empty filters show all; `health` reserved-but-live infra (write path lands E-15/E-16))*
- [x] E-02.F-01.S-05 — Required minimum fields on all entity types (slice 3 — PR #10, 2026-04-30; enforced as `Application.Create` invariants for the first entity)
- [x] E-02.F-02.S-01 — Register service with endpoints and protocol (catalog-service-entity, 2026-06-20: `Service` aggregate sibling to `Application` in the Catalog module; `0..50` protocol-typed endpoints persisted as a `jsonb` owned collection (`OwnsMany().ToJson()`); `Health` defaults `Unknown` (no write path — agent feeds it later, E-15); POST/GET-by-id/cursor-list at `/api/v1/catalog/services`; required owning team + membership gate (ADR-0103); new `catalog.services.register` permission (Member+OrgAdmin) + TS parity; `service.registered` audit. No Lifecycle/edit/UI this slice. Mutation 90.10%.)
- [x] E-02.F-02.S-02 — Service detail page with health and consumers (catalog-service-ui-surface, 2026-06-20: full Services frontend surface — list page (`/catalog/services`, default sort `displayName desc` — later flipped to `displayName asc`, list-filter-surface-catalog, 2026-06-22) + Register-Service dialog with 0..50 endpoints editor + read-only detail page (`/catalog/services/:id`); Services nav promoted from disabled. Frontend-only — S-01 backend/permission/audit + real-seam tests already on master. Health renders a read-only `Unknown` badge (no write path until E-15/E-16); **consumers deferred to E-04**. Mirrors the Application UI surface (useCursorList/useListUrlState/DataTable per ADR-0095, Untitled UI per ADR-0094). Codegen client regenerated. 477 frontend tests green; all DoD gates green.)

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
- [x] E-03.F-01.S-01 — Configure organization profile (slice 9 — PR #TBD, 2026-05-29; bytea logo with 256 KiB cap + SVG sanitization + ETag/304 + CSP sandbox, Description, DefaultTimeZone with IANA validation, Alpine `tzdata` runtime fix)
- [x] E-03.F-01.S-02 — Invite users with specific roles (slice 9 — PR #TBD, 2026-05-29; KeyCloak admin client with `username` field, copy-link UX, three-way 409 conflict model, UNIQUE partial index closes race, hourly expiry sweep via PostgresAdvisoryLock leader election; plus accept-invitation set-password flow (opaque tokenized link + Kartova-hosted set-password page; slice-9 sub-slice, 2026-06-01))
- [x] E-03.F-01.S-03 — View user details (slice 9 — PR #TBD, 2026-05-29; `GET /users/{id}` with teams + memberships via two-query client-side join over RLS-scoped Npgsql)
- [x] E-03.F-01.S-04 — User search for team-member add (slice 9 — PR #TBD, 2026-05-29; `GET /users?q=...&limit=...` typeahead with case-insensitive substring match across DisplayName + Email; `UserSearchCombobox` SPA component replaces raw UUID input)
- [x] E-03.F-01.S-05 — Members directory (slice 10 — 2026-06-10; cursor-paginated `GET /users` with role filter + search; displayName/email/role/teamCount/lastSeenAt columns; OrgAdmin-only row actions)
- [x] E-03.F-01.S-06 — Change member role (slice 10 — 2026-06-10; `PUT /users/{id}/role` writes through to KeyCloak + `realm_role` projection; last-OrgAdmin guard; takes effect on next token refresh)
- [x] E-03.F-01.S-07 — Offboard member (slice 10 — 2026-06-10; `DELETE /users/{id}` no successor; hard-delete per ADR-0102; team retains owned apps, created-by is immutable history — ADR-0103; last-OrgAdmin + self-offboard guards; slice-10 amendment 2026-06-10: no app reassignment, no IApplicationOwnerReassigner port)

**E-03.F-02: Team Management**
- [x] E-03.F-02.S-01 — Create and manage team profile (slice 8 — PR #TBD, 2026-05-26; `teams` table + `DisplayName`/`Description`; OrgAdmin creates, TeamAdmin renames own team)
- [x] E-03.F-02.S-02 — Assign components to team (slice 8 — PR #TBD, 2026-05-26; `PUT /applications/{id}/team`; team-scoped Catalog mutations via `KartovaTeamPolicies.ApplicationTeamScoped` resource handler)
- [~] E-03.F-02.S-03 — Team page with components and scorecard (slice 8 — PR #TBD, 2026-05-26; team detail page with members + assigned application IDs; scorecard deferred to E-10)

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
- [x] E-04.F-01.S-01 — Create relationship between entities (backend Slice 1a — PR #42, 2026-06-24: `POST /catalog/relationships`, depends-on/part-of, origin=manual, source-side auth, RLS + audit; Slice 1b catalog-relationships-ui-surface, 2026-06-25; Dependencies/Dependents section on Application+Service detail pages; either-endpoint authority — ADR-0108)
- [x] E-04.F-01.S-02 — View relationships with origin distinction (backend Slice 1a — PR #42: `GET /catalog/relationships?entityKind&entityId&direction` CursorPage + `DELETE`; origin in response; Slice 1b catalog-relationships-ui-surface, 2026-06-25; Dependencies/Dependents section on Application+Service detail pages; either-endpoint authority — ADR-0108)
- [ ] E-04.F-01.S-03 — Promote auto-discovered to manual (pin)
- [ ] E-04.F-01.S-04 — Demote manual to auto-managed (unpin)

**E-04.F-02: Relationship Visualization**
- [x] E-04.F-02.S-01 — Embedded mini dependency graph (catalog-dependency-mini-graph, 2026-06-26; read-only 1-hop React Flow graph above the Dependencies/Dependents tables on Application + Service detail pages; reuses the 1b relationship endpoint; standalone /graph explorer + S-03–S-06 deferred)
- [ ] E-04.F-02.S-02 — Relationship table below mini-graph
- [x] E-04.F-02.S-03 — "Open full graph" button linking to standalone (catalog-graph-explorer, 2026-06-27; link in the mini-graph header on Application + Service detail pages → `/graph?focus=<kind>:<id>`)
- [x] E-04.F-02.S-04 — Standalone Dependency Graph Explorer (/graph) (catalog-graph-explorer, 2026-06-27; new RLS-scoped BFS endpoint `GET /catalog/graph` — depth-annotated, cycle-safe, depth 1–4/node-cap 200/`truncated`; URL-driven `/graph?focus&expand` explorer with dagre layout + read-only React Flow, expand/collapse, open-detail links. ADR-0040 second view. Filters S-05 + impact analysis S-06 deferred. On PR — CI is the authoritative full-suite/container gate; gate-6 mutation deferred (env 10-min cap). Verified end-to-end in-browser (ADR-0084))
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
