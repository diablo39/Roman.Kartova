# Phase 2: Auto-Import

**Version:** v1.0 | **Epics:** 4 | **Features:** 11 | **Stories:** 36
**Dependencies:** Phase 0 (infrastructure), Phase 1 (entity registry)

---

### Epic E-07: Git Provider Integration

> Connect to Git providers (GitHub, Azure DevOps) for repository access and webhooks.

#### Feature E-07.F-01: GitHub Integration

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-07.F-01.S-01 | As an org admin, I want to connect my GitHub organization via OAuth so that Kartova can access our repositories | OAuth flow completes; GitHub org connected; org/repos accessible via API |
| E-07.F-01.S-02 | As an org admin, I want to see a list of all repositories in my connected GitHub org so that I can select which ones to import | Repository list fetched; shows name, language, last activity, description; filterable |
| E-07.F-01.S-03 | As a developer, I want GitHub webhook events (push, PR merge) to trigger documentation re-sync so that docs are always up to date | Webhook registered on repo; push events trigger doc sync; sync completes within 60 seconds |

#### Feature E-07.F-02: Azure DevOps Integration

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-07.F-02.S-01 | As an org admin, I want to connect my Azure DevOps organization via OAuth so that Kartova can access our repositories | OAuth flow completes; Azure DevOps org connected; projects/repos accessible |
| E-07.F-02.S-02 | As an org admin, I want to see all repositories across Azure DevOps projects so that I can select which ones to import | Repository list fetched from all projects; same filtering as GitHub |
| E-07.F-02.S-03 | As a developer, I want Azure DevOps service hooks to trigger documentation re-sync so that docs stay current | Service hook configured; push events trigger sync; sync completes within 60 seconds |

### Epic E-08: Auto-Import Engine

> Scan repositories to automatically extract and import entities, metadata, and relationships.

#### Feature E-08.F-01: Single Repository Deep Scan

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-08.F-01.S-01 | As a developer, I want to provide a repository URL and have Kartova scan it for code metadata (language, framework, dependencies) so that applications are registered automatically | Scan detects language from file extensions; framework from config files (package.json, .csproj, etc.); dependencies extracted |
| E-08.F-01.S-02 | As a developer, I want the scan to detect infrastructure definitions (Dockerfiles, Helm charts, Terraform) so that deployment patterns are cataloged | Dockerfiles detected -> container-based flag; Helm charts -> K8s deployment; Terraform -> cloud resources listed |
| E-08.F-01.S-03 | As a developer, I want the scan to detect sync API specs (OpenAPI, gRPC proto, GraphQL schemas) so that APIs are automatically registered | Spec files detected by convention and content; API entities created; linked to parent application |
| E-08.F-01.S-04 | As a developer, I want the scan to detect async API specs (AsyncAPI), CloudEvents definitions, and schema registry references so that event-driven interfaces are cataloged | AsyncAPI specs detected; channels/schemas extracted; CloudEvents metadata parsed; registry references stored |
| E-08.F-01.S-05 | As a developer, I want the scan to detect messaging configuration (queue/topic names, broker connections) so that async dependencies are mapped | Config files scanned for broker URLs, queue/topic names; message broker and queue/topic entities created |
| E-08.F-01.S-06 | As a developer, I want the scan to detect database connection strings and migration files so that data dependencies are tracked | Connection strings found (not values, just presence/type); migration files listed; database infra entities created |
| E-08.F-01.S-07 | As a developer, I want the scan to detect environment variable names (not values) so that configuration requirements are documented | Env var names extracted from code, docker-compose, Helm values; listed as configuration requirements |
| E-08.F-01.S-08 | As a developer, I want the scan to import README and docs/ folder content so that documentation is available immediately | README rendered on entity detail page; docs/ structure imported as documentation pages |
| E-08.F-01.S-09 | As a developer, I want to review all scan results before confirming the import so that I can correct any misdetections | Preview screen shows all detected entities and relationships; user can edit, remove, or add before confirming |

#### Feature E-08.F-02: Bulk Organization Scan

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-08.F-02.S-01 | As an org admin, I want to scan all repositories in a GitHub org or Azure DevOps project at once so that I can bulk-onboard my organization | Org-wide scan triggered; progress shown per repo; results aggregated for review |
| E-08.F-02.S-02 | As an org admin, I want to filter which repos to scan by naming convention, language, or last activity date so that I exclude irrelevant repos | Filter controls before scan; regex for names; language dropdown; activity date range |
| E-08.F-02.S-03 | As an org admin, I want to review bulk scan results with a diff view showing all discovered entities before confirming so that I maintain control over what enters the catalog | Bulk review screen; entity list grouped by repo; select/deselect individual entities; confirm imports selected |

#### Feature E-08.F-03: Scheduled Re-scan

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-08.F-03.S-01 | As an org admin, I want to configure periodic re-scanning (daily/weekly) so that the catalog stays current without manual effort | Schedule configurable per org; options: daily, weekly, custom cron; next scan time visible |
| E-08.F-03.S-02 | As a developer, I want the re-scan to show a diff of what changed since the last scan so that I understand what's new or modified | Change detection: new repos, changed dependencies, updated specs, new/removed infra; diff view available |
| E-08.F-03.S-03 | As a developer, I want re-scans to never override manual relationships so that my curated data is protected | Manual relationships untouched by re-scan; conflicts flagged for user review; auto-discovered relationships updated normally |
| E-08.F-03.S-04 | As a developer, I want a conflict review queue where I can see all re-scan conflicts with manual relationships and resolve them so that conflicts don't pile up silently | Conflict list UI: shows conflicting relationship, manual vs auto-discovered data, resolution options (keep manual / accept auto / merge); notification sent when new conflicts detected |

#### Feature E-08.F-04: Scan Resilience & Error Handling

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-08.F-04.S-01 | As a developer, I want scans to handle malformed files gracefully (invalid YAML, broken specs) so that one bad file doesn't fail the entire scan | Malformed files logged as warnings; scan continues; partial results shown with error list per file |
| E-08.F-04.S-02 | As an operator, I need the scan engine to respect Git provider API rate limits so that we don't get blocked | Rate limit detection (429/403 responses); automatic backoff; retry after limit reset; queue management for bulk scans |
| E-08.F-04.S-03 | As a developer, I want scan timeouts and failures to be retried automatically with clear status reporting so that transient issues are handled | Timeout configurable (default 5 min per repo per PRD); retry up to 3 times with backoff; failure status visible in UI with error details |
| E-08.F-04.S-04 | As a developer, I want partial scan results preserved when a scan partially fails so that successful detections aren't lost | Scan results saved per detector; failed detectors marked; user can accept partial results or retry failed parts |

### Epic E-09: Self-Service Onboarding Wizard

> Guided flow for new organizations to set up and import their service landscape.

#### Feature E-09.F-01: Onboarding Wizard Flow

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-09.F-01.S-01 | As a new user, I want to create my organization through a guided wizard so that I can start using Kartova without documentation | Step 1: Org name, description, logo; validation; org created on completion |
| E-09.F-01.S-02 | As a new org admin, I want to connect my Git provider as the next wizard step so that repositories are accessible | Step 2: Choose GitHub or Azure DevOps; OAuth flow; success confirmation |
| E-09.F-01.S-03 | As a new org admin, I want the wizard to scan my repos and show discovered entities for review so that I can confirm what gets imported | Step 3: Auto-scan triggered; progress bar; results preview with edit/remove capability |
| E-09.F-01.S-04 | As a new org admin, I want to confirm the import and see a summary of what was added so that I know my catalog is populated | Step 4: Confirm button; import executes; summary shows entity counts by type; link to browse catalog |
| E-09.F-01.S-05 | As a new user, I want progress indicators and contextual help at each wizard step so that I know where I am and what to do | Progress bar; step descriptions; help tooltips; "back" navigation between steps |

### Epic E-10: Scorecards & Data Quality

> Implement completeness scoring and nudge system to maintain catalog quality.

#### Feature E-10.F-01: Scorecard System

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-10.F-01.S-01 | As an org admin, I want to define scorecard rules per category (Documentation, Operations, Security, Quality) so that quality expectations are clear | Rule editor: select category, define checks (e.g., "has runbook" = 10 points), weights configurable |
| E-10.F-01.S-02 | As a developer, I want to see a completeness score (0-100%) on each entity so that I know what's missing | Score displayed on entity detail and in list views; breakdown by category; color-coded (red/yellow/green) |
| E-10.F-01.S-03 | As an engineering manager, I want a dashboard showing org-wide scorecard compliance so that I can track catalog quality over time | Dashboard: average score, distribution histogram, lowest-scoring entities, trend over time |

#### Feature E-10.F-02: Nudge System

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-10.F-02.S-01 | As an org admin, I want to set scorecard thresholds that trigger notifications to owners so that teams are nudged to improve data quality | Threshold configurable per org (e.g., "notify when score drops below 60%"); notification sent to entity owner(s) |
| E-10.F-02.S-02 | As a developer, I want actionable suggestions on my entity page telling me what to add to improve my score so that I know exactly what's missing | Suggestion list: "Add a runbook (+10 pts)", "Add API spec (+15 pts)"; sorted by impact |
