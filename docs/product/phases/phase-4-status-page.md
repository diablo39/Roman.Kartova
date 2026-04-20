# Phase 4: Status Page

**Version:** v1.0 | **Epics:** 1 | **Features:** 5 | **Stories:** 16
**Dependencies:** Phase 1 (entity catalog)

> **Note:** Automated health-driven status updates (from hybrid agent health probes) will be connected in Phase 6 (E-15.F-02.S-02). During Phase 4, all status updates are manual.

---

### Epic E-12: Public Status Page

> Provide tenants with a customizable, public-facing status page for communicating service availability.

#### Feature E-12.F-01: Status Page Configuration

| Story ID | User Story | Acceptance Criteria | ADRs |
|----------|-----------|-------------------|------|
| E-12.F-01.S-01 | As a tenant admin, I want to configure a public status page with my branding (logo, colors, custom CSS) so that it matches our company identity | Branding editor in admin panel; logo upload; color picker; CSS override; live preview | |
| E-12.F-01.S-02 | As a tenant admin, I want to set up a custom domain (status.mycompany.com) for my status page so that it looks professional | Custom domain input; CNAME instructions; domain verification | [0052](../../architecture/decisions/ADR-0052-custom-domains-with-auto-ssl.md) |
| E-12.F-01.S-03 | As an operator, I need automatic SSL certificate provisioning and renewal for custom status page domains so that HTTPS works without manual intervention | Certificate auto-provisioned on domain verification; renewal before expiry; fallback to platform domain on failure | [0052](../../architecture/decisions/ADR-0052-custom-domains-with-auto-ssl.md) |
| E-12.F-01.S-04 | As a tenant admin, I want to choose which internal services are exposed on the public page and how they're grouped so that I control external visibility | Service selector; drag-and-drop grouping; group naming; public names can differ from internal names | |
| E-12.F-01.S-05 | As a tenant admin, I want to configure a status page as internal-only (authenticated) so that sensitive service status is only visible to my organization | Internal flag toggle; page requires KeyCloak authentication when internal; RBAC respected; public visitors see 403 or redirect to login | [0010](../../architecture/decisions/ADR-0010-internal-status-page-auth-via-keycloak.md) |

#### Feature E-12.F-02: Status Management

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-12.F-02.S-01 | As a DevOps engineer, I want to manually set a service's public status (Operational, Degraded, Partial Outage, Major Outage, Maintenance) so that we communicate issues to customers | Status dropdown per component; change immediately reflected on public page; audit log entry |
| E-12.F-02.S-02 | As a DevOps engineer, I want to create an incident with status updates so that customers see a timeline of what's happening | Incident creation: title, affected components, initial status; add updates with timestamps; resolve incident |
| E-12.F-02.S-03 | As a DevOps engineer, I want to schedule maintenance windows so that customers are informed in advance | Maintenance creation: title, description, start/end time, affected components; displayed on status page before and during window |

#### Feature E-12.F-03: Subscriber Notifications

> **ADRs (feature-level):** [ADR-0051](../../architecture/decisions/ADR-0051-multi-channel-status-page-subscribers.md)

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-12.F-03.S-01 | As a status page visitor, I want to subscribe to notifications (email, SMS, webhook, RSS) so that I'm alerted when status changes | Subscribe form on page; email confirmation; SMS via phone number; webhook URL input; RSS feed URL available |
| E-12.F-03.S-02 | As a subscriber, I want to receive notifications when a component's status changes or an incident is updated so that I'm kept informed | Email/webhook triggered on status change; contains component name, old/new status, incident details |
| E-12.F-03.S-03 | As a subscriber, I want to choose which components I receive notifications for so that I only get relevant alerts | Component selector during subscription; preferences editable; unsubscribe link in every notification |

#### Feature E-12.F-04: Uptime History & Charts

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-12.F-04.S-01 | As a status page visitor, I want to see historical uptime percentage per component (daily/weekly/monthly) so that I can assess reliability | Uptime bar chart per component; green/yellow/red segments; percentage displayed; configurable time range |
| E-12.F-04.S-02 | As a status page visitor, I want to see past incident history so that I can understand the service's track record | Incident timeline: date, title, duration, affected components; expandable for update details |

#### Feature E-12.F-05: Status Page Infrastructure & High Availability

> **ADRs (feature-level):** [ADR-0053](../../architecture/decisions/ADR-0053-status-page-99-99-sla-target.md) (99.99% SLA)

| Story ID | User Story | Acceptance Criteria | ADRs |
|----------|-----------|-------------------|------|
| E-12.F-05.S-01 | As an operator, I need the status page deployed as a separate K8s cluster/namespace independent from the main platform so that it remains available even when the main platform is down (99.99% SLA) | Separate deployment; independent database replica or cache; serves from cache if main platform unavailable; no dependency on main platform for page rendering | [0005](../../architecture/decisions/ADR-0005-independent-data-replica-for-status-page.md), [0023](../../architecture/decisions/ADR-0023-status-page-as-separate-k8s-cluster.md) |
| E-12.F-05.S-02 | As an operator, I need data sync from the main platform to the status page service (status updates, incidents, subscriber list) so that the status page is always current | Async replication from main platform; eventual consistency acceptable (< 30s lag); status page continues serving last known state during main platform outage | [0005](../../architecture/decisions/ADR-0005-independent-data-replica-for-status-page.md) |
| E-12.F-05.S-03 | As an operator, I need health monitoring and alerting for the status page service itself so that its 99.99% SLA is measurable and maintained | Independent health checks; uptime tracking; alert on status page degradation; SLA compliance dashboard | |
