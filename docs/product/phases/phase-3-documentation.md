# Phase 3: Documentation

**Version:** v1.0 | **Epics:** 1 | **Features:** 5 | **Stories:** 15
**Dependencies:** Phase 2 (git provider integration)

---

### Epic E-11: Documentation Management

> Import, render, and cross-reference documentation from Git repositories.

#### Feature E-11.F-01: Git-Synced Markdown Documentation

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-11.F-01.S-01 | As a developer, I want markdown documentation from my repo's docs/ folder to be imported and rendered in Kartova so that documentation is centralized | docs/ folder detected; markdown files imported; rendered with GFM support (tables, code blocks, images) |
| E-11.F-01.S-02 | As a developer, I want documentation to auto-sync when I push changes to git so that Kartova docs are always current | Webhook-triggered sync; changes reflected within 60 seconds; sync status visible on entity page |
| E-11.F-01.S-03 | As a developer, I want a navigation sidebar for multi-page documentation so that I can browse docs like a documentation site | Sidebar generated from folder structure; pages ordered by filename; nested folders = nested navigation |

#### Feature E-11.F-02: Sync API Documentation (OpenAPI/gRPC/GraphQL)

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-11.F-02.S-01 | As a developer, I want OpenAPI/Swagger specs to be rendered as interactive API documentation so that I can explore endpoints and try requests | Spec rendered with endpoint list, request/response schemas, parameters; "Try it" functionality |
| E-11.F-02.S-02 | As a developer, I want gRPC proto files rendered as browsable API documentation so that I can understand available RPC methods | Service/method list; message type definitions; field descriptions |
| E-11.F-02.S-03 | As a developer, I want API documentation to be versioned and aligned with deployments so that I see the right docs for each environment | Version selector; docs match deployed version per environment; diff between versions available |

#### Feature E-11.F-03: Async API Documentation (AsyncAPI/CloudEvents/Schema Registry)

| Story ID | User Story | Acceptance Criteria | ADRs |
|----------|-----------|-------------------|------|
| E-11.F-03.S-01 | As a developer, I want AsyncAPI specs (v2.x and v3.x) rendered as interactive documentation showing channels, operations, and schemas so that I can understand event-driven interfaces | Channels listed with pub/sub operations; message schemas rendered; protocol bindings shown | |
| E-11.F-03.S-02 | As a developer, I want CloudEvents metadata rendered alongside AsyncAPI docs so that I understand the event envelope format | CloudEvents attributes (type, source, subject, dataschema) displayed per event; linked to channel | |
| E-11.F-03.S-03 | As a developer, I want schema registry schemas (Confluent, Apicurio) pulled and displayed with version history so that I see live schema evolution | Registry connected; schemas fetched per topic; version list with diffs; compatibility mode shown | [0037](../../architecture/decisions/ADR-0037-schema-registry-integrations.md) |
| E-11.F-03.S-04 | As a developer, I want a unified view showing both sync and async APIs for a service side by side so that I see the complete interface surface | Tabbed or split view: Sync APIs (REST/gRPC/GraphQL) \| Async APIs (AsyncAPI/CloudEvents); consistent layout | |

#### Feature E-11.F-04: Documentation Hub per Service

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-11.F-04.S-01 | As a developer, I want each service to have a documentation hub with tabs for markdown docs, sync API reference, async API reference, and changelog so that all docs are in one place | Hub page with tabbed navigation; each tab populated from respective sources; empty tabs hidden |
| E-11.F-04.S-02 | As a developer, I want a changelog auto-generated from git history so that I can see what changed and when | Changelog derived from git commits/tags; grouped by version/date; commit messages as entries |

#### Feature E-11.F-05: Cross-Service Referencing & Documentation Search

| Story ID | User Story | Acceptance Criteria | ADRs |
|----------|-----------|-------------------|------|
| E-11.F-05.S-01 | As a developer, I want references to other services in my docs to become clickable links automatically so that navigation between related services is seamless | Pattern detection (service names, URLs); auto-linked to entity detail pages; link preview on hover | |
| E-11.F-05.S-02 | As a developer, I want to search across all documentation, API specs, and runbooks so that I can find information regardless of which service it belongs to | Full-text search via Elasticsearch; results show document title, service, snippet; filters by doc type | [0002](../../architecture/decisions/ADR-0002-elasticsearch-for-search.md) |
| E-11.F-05.S-03 | As a developer, I want "Related services" suggestions on each service page based on the dependency graph so that I discover relevant context | Related services section; based on direct and transitive dependencies; sorted by relevance | |
