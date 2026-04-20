# Phase 7: Intelligence & Governance

**Version:** v1.0 | **Epics:** 4 | **Features:** 5 | **Stories:** 13
**Dependencies:** Phase 1-2 (scorecards, entities), Phase 6 (health data)

---

### Epic E-17: Service Maturity Model

> Auto-calculated maturity levels (L1-L5) with progression paths.

> **ADRs (epic-level):** [ADR-0071](../../architecture/decisions/ADR-0071-five-level-maturity-model.md)

#### Feature E-17.F-01: Maturity Calculation & Display

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-17.F-01.S-01 | As a developer, I want each service to display its maturity level (L1-L5) so that I know how production-ready it is | Level auto-calculated from existing data; displayed on entity detail and in list views; level badge with name. **Note:** L5 requires cost tracking (Phase 8) -- maturity system gracefully handles missing signals, awarding max achievable level until all data sources are available |
| E-17.F-01.S-02 | As a developer, I want to see what actions are needed to reach the next maturity level so that I have a clear progression path | Checklist per level; completed/pending items; "X of Y requirements met for next level" |
| E-17.F-01.S-03 | As an org admin, I want to customize maturity level requirements so that they match our organization's standards | Per-org configuration; add/remove requirements per level; defaults provided |

#### Feature E-17.F-02: Maturity Dashboards

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-17.F-02.S-01 | As an engineering manager, I want a dashboard showing maturity distribution across all services so that I understand organizational readiness | Distribution chart (L1-L5); filter by team/domain; comparison between teams; trend over time |

### Epic E-18: Dependency Risk Scoring

> Automated risk assessment combining ownership, criticality, operational readiness, and dependency signals.

#### Feature E-18.F-01: Risk Calculation & Heatmap

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-18.F-01.S-01 | As a developer, I want each entity to have an automated risk score combining multiple signals so that high-risk services are identified | Risk score computed from: ownership, criticality, ops readiness, dependency fan-in, staleness, scorecard; levels: Critical/High/Medium/Low |
| E-18.F-01.S-02 | As an engineering manager, I want a risk heatmap dashboard showing all services by risk level so that I can prioritize improvement efforts | Heatmap visualization; color-coded by risk; filterable by team/domain/tier; drill-down to risk factors |
| E-18.F-01.S-03 | As a team admin, I want to receive alerts when a service's risk score crosses a threshold so that I can act before problems occur | Alert threshold configurable; notification on breach; trending alerts ("risk increased by X in 30 days") |

### Epic E-19: Developer Experience Score

> Composite DX metric measuring how easy a service is to work with.

#### Feature E-19.F-01: DX Score Calculation & Suggestions

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-19.F-01.S-01 | As a developer, I want a DX Score (0-100) on each service measuring documentation, onboarding readiness, operational maturity, and API quality so that DX is quantified | Score computed and displayed; breakdown by category; updated on data changes |
| E-19.F-01.S-02 | As a developer, I want actionable improvement suggestions ranked by impact on my DX Score so that I know what to fix first | Suggestion list: action + point impact; sorted by impact; links to relevant section to fix |
| E-19.F-01.S-03 | As an engineering manager, I want DX Score trends per team and org-wide leaderboards so that teams are motivated to improve | Trend charts; team ranking; historical comparison; celebration of improvements |

### Epic E-20: Multi-Environment Drift Detection

> Detect and alert on version/config drift between environments.

#### Feature E-20.F-01: Drift Monitoring & Dashboard

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-20.F-01.S-01 | As a DevOps engineer, I want automatic detection when a version is on staging for > X days without being promoted to prod so that stale deployments are flagged | Drift detected; configurable threshold (default 14 days); alert sent to team |
| E-20.F-01.S-02 | As a DevOps engineer, I want an environment comparison matrix (Service x Environment with versions) so that I can see drift at a glance | Matrix view; color-coded for drift; filterable by team/criticality/duration; version numbers displayed |
| E-20.F-01.S-03 | As a DevOps engineer, I want to detect config and infrastructure drift between environments so that non-version discrepancies are also tracked | Config diff beyond env-specific values; resource allocation comparison; drift flagged per category |
