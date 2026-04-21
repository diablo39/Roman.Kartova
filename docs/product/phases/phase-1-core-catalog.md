# Phase 1: Core Catalog

**Version:** v1.1 | **Epics:** 6 | **Features:** 22 | **Stories:** 55
**Dependencies:** Phase 0 (foundation infrastructure)

---

### Epic E-02: Entity Registry

> Enable CRUD operations for all catalog entity types with proper lifecycle management.

> **ADRs (epic-level):** [ADR-0064](../../architecture/decisions/ADR-0064-entity-taxonomy-nine-fixed-plus-jsonb-custom-entity-phased.md) — nine fixed types + JSONB custom_attributes (MVP), Custom Entity type planned for Phase 2

#### Feature E-02.F-01: Application Entity Management

> **ADRs (feature-level):** [ADR-0069](../../architecture/decisions/ADR-0069-required-minimum-fields-enforcement.md) (required fields, S-05), [ADR-0073](../../architecture/decisions/ADR-0073-enforced-entity-lifecycle-states.md) (lifecycle states, S-04)

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-02.F-01.S-01 | As a developer, I want to register a new application in the catalog so that it becomes discoverable to the organization | API endpoint creates application; required fields enforced (name, owner, description); returns created entity |
| E-02.F-01.S-02 | As a developer, I want to view an application's detail page showing metadata, relationships, and documentation so that I understand what this application does | Detail page renders all metadata; relationships displayed; linked documentation shown; deployment history visible |
| E-02.F-01.S-03 | As a developer, I want to edit an application's metadata so that the catalog stays up to date | Edit form pre-filled with current data; save updates entity; audit log entry created |
| E-02.F-01.S-04 | As a team admin, I want to change an application's lifecycle status (active -> deprecated -> decommissioned) so that teams know which applications are being retired | Status transitions enforced (can't skip steps); deprecated entities show warning badge; decommissioned entities are archived |

#### Feature E-02.F-02: Service Entity Management

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-02.F-01.S-05 | As a platform, I enforce required minimum fields (name, owner/team, description) on all entity types during creation so that data quality is guaranteed from the start | Creation rejected with clear error if required fields missing; applies to all entity types (Application, Service, API, Infrastructure, Broker, Queue/Topic); import also enforced |
| E-02.F-02.S-01 | As a developer, I want to register a service with its endpoints and protocol so that other teams can discover how to interact with it | Service created with endpoints, protocol type; health status defaults to "unknown"; required minimum fields enforced |
| E-02.F-02.S-02 | As a developer, I want to view a service's detail page with its health status and consumers so that I know its current state and who depends on it | Detail page shows real-time health; consumer list displayed; dependency graph snippet visible |

#### Feature E-02.F-03: API Entity Management (Sync & Async)

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-02.F-03.S-01 | As a developer, I want to register a sync API (REST/gRPC/GraphQL) with its spec URL so that consumers can discover and use it | API entity created; spec URL stored; consumers/providers linkable |
| E-02.F-03.S-02 | As a developer, I want to register an async API with its AsyncAPI spec, protocol, and channels so that event consumers can discover it | Async API entity created; protocol, channels, schemas stored; producer/consumer relationships tracked |
| E-02.F-03.S-03 | As a developer, I want to see both sync and async APIs for a service in a unified view so that I understand the complete interface surface | Service detail page shows tabbed view: Sync APIs / Async APIs; each with spec details |

#### Feature E-02.F-04: Infrastructure & Broker Entity Management

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-02.F-04.S-01 | As a DevOps engineer, I want to register infrastructure components (databases, caches, cloud resources) so that dependencies on them are tracked | Infrastructure entity created with provider, type, region; linkable to services |
| E-02.F-04.S-02 | As a DevOps engineer, I want to register message brokers with their queues and topics so that async communication patterns are visible | Broker entity with type and connection info; queues/topics linked to broker; producers/consumers tracked |

#### Feature E-02.F-05: Environment & Deployment Tracking

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-02.F-05.S-01 | As a DevOps engineer, I want to register environments (dev, staging, prod) with their infrastructure details so that deployment targets are cataloged | Environment created with name, type, region, cluster, resource details |
| E-02.F-05.S-02 | As a DevOps engineer, I want to record a deployment (app version, environment, config, deployer) so that the deployment history is tracked | Deployment record created; linked to application and environment; timestamp and deployer recorded; replicas and resources stored |
| E-02.F-05.S-03 | As a developer, I want to see which version of an application is deployed in each environment so that I know the current state of deployments | Environment matrix shows app x environment with version; config differences highlightable |

### Epic E-03: Organization & Team Management

> Implement the hybrid organization structure (hierarchy + tags) with multi-ownership support.

#### Feature E-03.F-01: Organization Management

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-03.F-01.S-01 | As an org admin, I want to configure my organization's profile (name, logo, settings) so that the platform reflects our identity | Org profile editable; logo uploadable; settings persisted per tenant |
| E-03.F-01.S-02 | As an org admin, I want to invite users to my organization with specific roles so that access is controlled | Invitation sent via email; role assigned on acceptance; invitation expires after 7 days |

#### Feature E-03.F-02: Team Management

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-03.F-02.S-01 | As a team admin, I want to create and manage my team profile (name, description, members, contact info) so that ownership is clear | Team created with metadata; members added/removed; contact info (Slack channel, email) stored |
| E-03.F-02.S-02 | As a team admin, I want to assign components to my team so that ownership is tracked | Components linkable to team; team page shows all owned components; component detail shows owning team(s) |
| E-03.F-02.S-03 | As a developer, I want to see a team page listing all their owned components, health status, and scorecard so that I can assess a team's portfolio | Team page renders member list, component list with health badges, aggregate scorecard |

#### Feature E-03.F-03: System Grouping

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-03.F-03.S-01 | As a team admin, I want to create a System (logical grouping) and assign components to it so that related entities are organized together | System created under team; components assignable to system; system has description and diagram |
| E-03.F-03.S-02 | As a developer, I want to browse the catalog by Organization -> Team -> System -> Component hierarchy so that I can navigate the service landscape | Tree navigation in UI; expandable nodes; entity count per level; breadcrumb trail |

#### Feature E-03.F-04: Tag System

> **ADRs (feature-level):** [ADR-0072](../../architecture/decisions/ADR-0072-tag-taxonomy-predefined-plus-custom.md)

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-03.F-04.S-01 | As an org admin, I want to define tag taxonomies (domain, criticality, compliance, tech stack) so that entities can be classified consistently | Tag categories created; predefined values per category; custom values allowed where configured |
| E-03.F-04.S-02 | As a developer, I want to tag entities with multiple tags so that they appear in cross-cutting views | Multiple tags assignable per entity; tags filterable in search; tag-based views available |
| E-03.F-04.S-03 | As a developer, I want to filter the catalog by any combination of tags so that I can find entities matching specific criteria | Multi-tag filter in UI; AND/OR logic; results update in real-time; filter state shareable via URL |

#### Feature E-03.F-05: Multi-Ownership

> **ADRs (feature-level):** [ADR-0066](../../architecture/decisions/ADR-0066-multi-ownership-with-quorum-rules.md)

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-03.F-05.S-01 | As a team admin, I want to mark a component as shared/platform and assign co-owning teams so that platform components have clear multi-team ownership | Platform designation toggleable; multiple teams assignable as co-owners; all owning teams listed on component detail |
| E-03.F-05.S-02 | As a developer, I want to see all shared/platform components in a dedicated view so that I know what's available across teams | Platform components filter/view; shows all co-owners; sortable by usage/consumers |
| E-03.F-05.S-03 | As an org admin, I want co-owned components to have clear permission rules (any co-owning team admin can edit, lifecycle changes require all co-owners' agreement) so that shared ownership doesn't cause conflicts | Edit: any co-owner team admin can modify metadata; lifecycle status change (deprecate/decommission): requires approval from all co-owning team admins; notifications sent to all co-owners on changes |
| E-03.F-05.S-04 | As an org admin, I want co-owned component ownership to transfer cleanly when a co-owning team is deleted so that orphaned ownership is prevented | Team deletion workflow: reassign or remove co-ownership; if last owner removed, org admin notified to assign new owner; entities cannot become ownerless |

### Epic E-04: Entity Relationships

> Implement relationship management between entities with origin tracking and protection rules.

#### Feature E-04.F-01: Manual Relationship Management

> **ADRs (feature-level):** [ADR-0056](../../architecture/decisions/ADR-0056-manual-relationship-precedence.md), [ADR-0067](../../architecture/decisions/ADR-0067-relationship-origin-tracking.md)

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-04.F-01.S-01 | As a developer, I want to create a relationship between two entities (e.g., "Service A depends-on Service B") so that dependencies are documented | Relationship created with type; both entities updated; audit log entry created; origin set to `manual` |
| E-04.F-01.S-02 | As a developer, I want to see all relationships for an entity with visual distinction between manual and auto-discovered ones so that I know which are user-curated | Relationships listed with origin badge; manual = distinct icon/color; auto-discovered = different style |
| E-04.F-01.S-03 | As a developer, I want to promote an auto-discovered relationship to manual (pin it) so that it won't be removed by future re-scans | "Pin" action on auto-discovered relationship; origin changes to `manual`; audit trail records promotion |
| E-04.F-01.S-04 | As a developer, I want to demote a manual relationship back to auto-managed so that the system takes over maintaining it | "Unpin" action on manual relationship; origin reverts to `auto-discovered`; relationship subject to auto-updates; **warning displayed**: if next re-scan doesn't find this relationship, it will be removed |

#### Feature E-04.F-02: Relationship Visualization

> **Navigation model:** Dependency graph is accessible in two views — (1) embedded mini-graph on entity detail page "Dependencies" tab, and (2) standalone full graph explorer at `/graph`. Mini-graph shows 1 level deep with basic interactions; "Open full graph" links to standalone view with `?focus=entity-id`. Standalone is also accessible from sidebar navigation.

> **ADRs (feature-level, all stories):** [ADR-0040](../../architecture/decisions/ADR-0040-two-view-dependency-graph-navigation.md)

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-04.F-02.S-01 | As a developer, I want to see an embedded mini dependency graph on the entity detail "Dependencies" tab so that I get a quick visual overview of direct relationships | Mini-graph shows 1 level deep (direct dependencies/dependents only); entity-colored nodes with health dots; manual vs auto edge styling; clicking a node navigates to that entity; basic zoom/pan; summary stats above ("8 upstream, 12 downstream, 3 manual") |
| E-04.F-02.S-02 | As a developer, I want a relationship table below the mini-graph listing all dependencies and dependents with origin badges so that I see the full list with details | Table with tabs: Dependencies / Dependents; columns: Name, Type, Relationship Type, Origin (Manual/Auto badge), Health; sortable; "Add Relationship" button; clickable rows navigate to entity |
| E-04.F-02.S-03 | As a developer, I want an "Open full graph" button on the Dependencies tab that opens the standalone graph explorer focused on the current entity so that I can explore deeper | Button navigates to `/graph?focus={entity-id}`; standalone graph opens pre-focused on that entity |
| E-04.F-02.S-04 | As a developer, I want a standalone Dependency Graph Explorer page (`/graph`) accessible from the sidebar navigation so that I can explore the entire service landscape visually | Full-page graph; entity search/selector at top; multi-level depth (1-3 levels + all); force-directed layout; side panel on node click showing entity details; "View Entity Detail" and "Focus Graph Here" actions |
| E-04.F-02.S-05 | As a developer, I want to filter the standalone dependency graph by team, domain, criticality, entity type, and relationship origin (manual/auto) so that I can focus on relevant relationships | Filter dropdowns in top bar; toggle: Manual only / Auto only / All; "Reset filters" link; graph updates in real-time; filtered-out nodes dimmed or hidden; entity counter ("Showing 9 of 847") |
| E-04.F-02.S-06 | As an engineering manager, I want to run impact analysis from the standalone graph so that I understand blast radius visually | "Impact Analysis" button on side panel; dims graph except affected downstream path; affected nodes glow by tier; banner: "12 downstream (3x tier-1, 5x tier-2, 4x tier-3)"; "Close Analysis" returns to normal |

### Epic E-05: Search

> Provide fast, faceted search across all catalog entities.

#### Feature E-05.F-01: Entity Search

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-05.F-01.S-01 | As a developer, I want to search for entities by name with instant results so that I can quickly find what I need | Search-as-you-type with < 200ms response; results ranked by relevance; highlights matching text |
| E-05.F-01.S-02 | As a developer, I want to filter search results by entity type, team, tags, and owner so that I can narrow results | Faceted sidebar with counts; multi-select filters; filters combinable; result count updates live |
| E-05.F-01.S-03 | As a developer, I want search results to show key metadata (type, owner, team, health, tags) so that I can evaluate results without clicking into each one | Result cards show entity type icon, name, owner, team, health badge, top tags |

### Epic E-06: Dashboards & Visualizations (Core)

> Deliver key dashboards for navigating and understanding the service landscape.

#### Feature E-06.F-01: Catalog Home Dashboard

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-06.F-01.S-01 | As a developer, I want a home dashboard showing recently updated entities, my team's components, and quick search so that I have a starting point | Dashboard shows: recent activity, my team's entities, global search bar, entity counts by type |

#### Feature E-06.F-02: Team Dashboard

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-06.F-02.S-01 | As a team admin, I want a team dashboard showing all our components, their health, and our aggregate scorecard so that I have a team overview | Dashboard shows: component list with health, aggregate scorecard, recent changes, team members |

#### Feature E-06.F-03: Organization Overview Dashboard

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-06.F-03.S-01 | As an engineering manager, I want an organization overview showing entity counts, health distribution, and scorecard compliance so that I understand org-wide status | Dashboard shows: total entities by type, health breakdown (pie/bar), scorecard compliance %, team comparison |

#### Feature E-06.F-04: Environment Map Dashboard

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-06.F-04.S-01 | As a DevOps engineer, I want a standalone Environment Map dashboard showing which versions are deployed where across all services so that I have a deployment overview | Matrix: Service x Environment with version numbers; color-coded for freshness/staleness; filterable by team/domain; clickable to deployment detail |

#### Feature E-06.F-05: Status Board Dashboard

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-06.F-05.S-01 | As a developer, I want a Status Board dashboard showing health overview across all services so that I can spot issues at a glance | Grid of all services with health indicator (green/yellow/red); sortable by status; filterable by team/tier; click to entity detail |

### Epic E-06a: Notification Infrastructure

> Core notification system supporting in-app, email, webhooks, and native integrations. Required by multiple later features (scorecard nudges, risk alerts, drift alerts, breaking change alerts, status page subscribers).

> **ADRs (epic-level):** [ADR-0047](../../architecture/decisions/ADR-0047-unified-multi-channel-notification-engine.md) (dispatch engine), [ADR-0048](../../architecture/decisions/ADR-0048-native-slack-and-teams-integrations.md) (Slack/Teams), [ADR-0049](../../architecture/decisions/ADR-0049-configurable-smtp-email-provider.md) (email), [ADR-0050](../../architecture/decisions/ADR-0050-notification-log-as-mifid-ii-record.md) (MiFID record), [ADR-0033](../../architecture/decisions/ADR-0033-hmac-signed-webhooks-with-retry-dlq-idempotency-rate-limiting.md) (webhook delivery — S-04), [0003](../../architecture/decisions/ADR-0003-apache-kafka-via-strimzi-on-kubernetes.md) (message bus)

#### Feature E-06a.F-01: Notification Dispatch Engine

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-06a.F-01.S-01 | As a developer, I need a notification dispatch engine supporting multiple channels (in-app, email, webhook) so that all features can send notifications through a unified system | Dispatch accepts: recipient(s), channel(s), template, payload; routes to correct channel handler; delivery logged for MiFID II compliance |
| E-06a.F-01.S-02 | As a user, I want in-app notifications with a notification center (bell icon, unread count, list) so that I see platform events without leaving the app | Notification center in UI header; unread count badge; mark as read; notification list with timestamps; click navigates to relevant entity |
| E-06a.F-01.S-03 | As a user, I want email notifications for important events so that I'm informed even when not in the platform | Email dispatch via configured SMTP/provider; HTML templates; unsubscribe link in every email; bounce handling |
| E-06a.F-01.S-04 | As an org admin, I want outbound webhook notifications so that we can route events to Slack, Teams, PagerDuty, or custom systems | Webhook subscription per event type; HMAC-signed payloads; retry with exponential backoff; delivery status in UI |

#### Feature E-06a.F-02: Notification Preferences & Policies

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-06a.F-02.S-01 | As a user, I want to configure my notification preferences (which channels, which event types, frequency) so that I'm not overwhelmed | Preferences UI per user; toggle per event type x channel; frequency options (immediate, daily digest, weekly digest) |
| E-06a.F-02.S-02 | As an org admin, I want to define organization-level notification policies (e.g., "tier-1 status changes always email all team members") so that critical events aren't missed | Policy editor; scope by event type, entity tags, severity; overrides user preferences for mandatory notifications |

#### Feature E-06a.F-03: Native Integrations (Slack & Teams)

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-06a.F-03.S-01 | As an org admin, I want to connect Slack and receive notifications in designated channels so that teams get alerts in their workflow | Slack OAuth integration; channel selector per event type; rich message formatting; interactive buttons (acknowledge, view in Kartova) |
| E-06a.F-03.S-02 | As an org admin, I want to connect Microsoft Teams and receive notifications in designated channels so that teams using Teams get alerts | Teams webhook/bot integration; channel selector; adaptive card formatting; link to Kartova |
