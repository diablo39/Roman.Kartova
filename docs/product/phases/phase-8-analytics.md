# Phase 8: Advanced Analytics

**Version:** v1.0 | **Epics:** 4 | **Features:** 5 | **Stories:** 14
**Dependencies:** Phase 5 (CLI), Phase 2 (API specs)

---

### Epic E-21: API Changelog & Breaking Change Detection

> Track API version history and automatically detect breaking changes.

#### Feature E-21.F-01: API Version Tracking

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-21.F-01.S-01 | As a developer, I want to see a version history of my API spec (OpenAPI/AsyncAPI/gRPC/GraphQL) so that I can track how the API evolved | Version list with timestamps; diff between any two versions; field-level change tracking |
| E-21.F-01.S-02 | As a developer, I want automatic diff generation between API spec versions showing added/removed/changed fields so that changes are clear | Side-by-side diff; color-coded additions/removals; change type classification |

#### Feature E-21.F-02: Breaking Change Detection & Alerting

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-21.F-02.S-01 | As a developer, I want automatic classification of API changes as backward-compatible, breaking, or deprecation so that I know the impact | Rules engine: removed endpoint = breaking, added optional field = compatible, etc.; classification displayed on diff |
| E-21.F-02.S-02 | As an API consumer, I want to be notified before breaking changes to APIs I depend on are deployed so that I can prepare | Consumer list per API; notification sent on breaking change detection; includes change details and timeline |
| E-21.F-02.S-03 | As a DevOps engineer, I want `kartova api-check` in CI/CD to detect breaking changes and optionally block deployment so that contracts are enforced | CLI command compares current spec vs. deployed; reports breaking changes; configurable block on breaking without version bump |

### Epic E-22: Change Impact Preview

> Blast radius analysis before deployments.

#### Feature E-22.F-01: Blast Radius Analysis

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-22.F-01.S-01 | As a DevOps engineer, I want `kartova impact-check` to show all downstream services affected by a change with tier breakdown so that I understand the blast radius | CLI returns: total affected count, breakdown by tier, list of affected services with paths; exit code based on configurable threshold |
| E-22.F-01.S-02 | As a DevOps engineer, I want a visual blast radius graph in the web UI so that impact is immediately understandable | Graph starting from changed service; downstream paths highlighted; tier color coding; expandable detail |
| E-22.F-01.S-03 | As a DevOps engineer, I want the impact report posted as a PR comment (GitHub/Azure DevOps) so that reviewers see blast radius during code review | Integration with PR APIs; comment formatted with blast radius summary; updated on PR update |

### Epic E-23: Built-in Tech Radar

> Auto-populated technology radar from actual usage in the catalog.

#### Feature E-23.F-01: Tech Radar Visualization & Governance

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-23.F-01.S-01 | As an engineering manager, I want a Tech Radar visualization (4 rings x 4 quadrants) auto-populated from technologies detected in the catalog so that I see actual technology adoption | Radar rendered; technologies placed in quadrants (Languages, Infrastructure, Data, Tools); ring = adoption level; click shows services using it |
| E-23.F-01.S-02 | As an org admin, I want to override ring placement for any technology (Adopt/Trial/Assess/Hold) so that the radar reflects organizational strategy | Override UI; manual placement saved; overrides take precedence over auto-placement |
| E-23.F-01.S-03 | As an org admin, I want to be alerted when new services adopt a technology marked as "Hold" so that technology governance is enforced | Alert on new service using Hold tech; policy support for Hold tech migration plans; CLI enforcement option |

### Epic E-24: Cost Attribution

> Map infrastructure costs to catalog entities.

#### Feature E-24.F-01: Cost Tracking & Dashboards

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-24.F-01.S-01 | As a DevOps engineer, I want infrastructure costs mapped to services (from K8s resources, cloud APIs, manual entries) so that I know what each service costs | Cost data ingested; aggregated per service/team/system; per-environment breakdown |
| E-24.F-01.S-02 | As an engineering manager, I want a cost dashboard showing monthly cost per service with trends so that I can track spending | Dashboard: service cost table, team rollup, trend charts, month-over-month comparison |
| E-24.F-01.S-03 | As an engineering manager, I want cost anomaly alerts when a service's cost spikes so that unexpected spending is caught early | Anomaly detection: configurable threshold (% increase); alert sent to team and org admin |
