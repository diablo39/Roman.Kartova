# Phase 5: CLI & Policy

**Version:** v1.0 | **Epics:** 3 | **Features:** 6 | **Stories:** 15
**Dependencies:** Phase 1 (entities), Phase 2 (scorecards)

---

### Epic E-13: CLI Tool

> Provide a CLI for CI/CD pipeline integration enabling registration, deployment reporting, and policy enforcement.

#### Feature E-13.F-01: CLI Core

| Story ID | User Story | Acceptance Criteria | ADRs |
|----------|-----------|-------------------|------|
| E-13.F-01.S-01 | As a DevOps engineer, I want to install the Kartova CLI as a .NET global tool or standalone binary so that I can use it in any CI/CD pipeline | `dotnet tool install kartova-cli` works; standalone binary available for Linux/macOS/Windows; `kartova --version` outputs version | [0046](../../architecture/decisions/ADR-0046-dotnet-global-tool-cli-distribution.md) |
| E-13.F-01.S-02 | As a DevOps engineer, I want to authenticate the CLI using a service account JWT token so that CI/CD pipelines can interact with Kartova securely | `kartova auth --token <jwt>` stores token; subsequent commands use stored token; expired token returns clear error | [0007](../../architecture/decisions/ADR-0007-jwt-oidc-for-api-and-cli-auth.md), [0009](../../architecture/decisions/ADR-0009-service-account-jwt-model-for-cicd.md) |
| E-13.F-01.S-03 | As a DevOps engineer, I want to register or update a component from the CLI so that catalog entries are managed from code | `kartova register --name X --type service --owner team-Y` creates/updates entity; `kartova update --name X --field value` modifies metadata | |

#### Feature E-13.F-02: Deployment Reporting

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-13.F-02.S-01 | As a DevOps engineer, I want to report a deployment event from CI/CD so that Kartova tracks what's deployed where | `kartova deploy --app X --env prod --version 1.2.3 --config sha` creates deployment record; visible in app detail |
| E-13.F-02.S-02 | As a DevOps engineer, I want to report health check results from CI/CD so that health status is updated programmatically | `kartova health --app X --status healthy --env prod` updates health; reflects in status page |

#### Feature E-13.F-03: Validation & Scanning

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-13.F-03.S-01 | As a DevOps engineer, I want to validate a catalog entry's completeness from CI/CD so that data quality is enforced in the pipeline | `kartova validate --app X` returns scorecard; exit code 0 if above threshold, 1 if below |
| E-13.F-03.S-02 | As a DevOps engineer, I want to trigger a repository re-scan from the CLI so that the catalog is updated as part of the build process | `kartova scan --repo <url>` triggers scan; results returned; new entities flagged |

### Epic E-14: Policy Engine

> Enable organizations to define and enforce policies on catalog entities via web UI and CLI.

#### Feature E-14.F-01: Policy Definition

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-14.F-01.S-01 | As an org admin, I want to define policies (e.g., "all tier-1 services must have a runbook") in the web UI so that organizational standards are codified | Policy editor: name, description, rule expression, severity (warning/error), scope (all/tag/tier filter) |
| E-14.F-01.S-02 | As an org admin, I want to see a policy compliance dashboard showing which entities pass/fail each policy so that I can track adherence | Dashboard: policy list with pass/fail counts; drill-down to non-compliant entities; trend over time |

#### Feature E-14.F-02: CLI Policy Enforcement

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-14.F-02.S-01 | As a DevOps engineer, I want to run `kartova policy-check --app X` in CI/CD so that policy violations are caught before deployment | Command evaluates all applicable policies; returns violations with severity; exit code 1 if any errors |
| E-14.F-02.S-02 | As a DevOps engineer, I want policy violations to be configurable as warnings (non-blocking) or errors (blocking) so that enforcement is gradual | Warning violations logged but exit code 0; error violations cause exit code 1; severity configured per policy |

### Epic E-14a: Billing & Subscription Management

> Four-tier pricing (Free / Starter / Pro / Enterprise) billing integration, usage metering, and subscription management. See [ADR-0061](../../architecture/decisions/ADR-0061-four-tier-pricing-model.md) for the tier structure and feature gating, [ADR-0062](../../architecture/decisions/ADR-0062-external-billing-provider.md) for the billing provider, and [ADR-0063](../../architecture/decisions/ADR-0063-user-count-metering-per-billing-period.md) for user-count metering.

#### Feature E-14a.F-01: Billing Integration

| Story ID | User Story | Acceptance Criteria | ADRs |
|----------|-----------|-------------------|------|
| E-14a.F-01.S-01 | As an operator, I need user count tracking per organization so that billing is based on actual usage | Active user count tracked per org per billing period; service accounts excluded; status page viewers excluded | [0063](../../architecture/decisions/ADR-0063-user-count-metering-per-billing-period.md) |
| E-14a.F-01.S-02 | As an operator, I need integration with a billing provider (e.g., Stripe) for subscription management and payment processing so that revenue collection is automated | Subscription created on org onboarding; monthly invoicing based on user count; payment method management; invoice history | [0062](../../architecture/decisions/ADR-0062-external-billing-provider.md) |
| E-14a.F-01.S-03 | As a tenant admin, I want a billing dashboard showing my current plan, user count, and invoice history so that I understand my costs | Dashboard: current plan tier (Free/Starter/Pro/Enterprise), current user count vs minimum seats, monthly cost, invoice list, payment method, plan details, upgrade/downgrade CTAs | [0061](../../architecture/decisions/ADR-0061-four-tier-pricing-model.md), [0062](../../architecture/decisions/ADR-0062-external-billing-provider.md), [0063](../../architecture/decisions/ADR-0063-user-count-metering-per-billing-period.md) |
| E-14a.F-01.S-04 | As a tenant admin, I want to manage my payment method and download invoices so that billing is self-service | Add/update payment method; download PDF invoices; email receipts; failed payment notifications | [0061](../../architecture/decisions/ADR-0061-four-tier-pricing-model.md), [0062](../../architecture/decisions/ADR-0062-external-billing-provider.md) |
