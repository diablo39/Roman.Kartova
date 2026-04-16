# Phase 6: Health Monitoring & Agent

**Version:** v1.0 | **Epics:** 2 | **Features:** 6 | **Stories:** 12
**Dependencies:** Phase 1 (entities), Phase 4 (status page)

---

### Epic E-15: Hybrid Agent

> Deploy a lightweight .NET agent in customer infrastructure for health monitoring, metrics, and service discovery.

#### Feature E-15.F-01: Agent Deployment & Communication

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-15.F-01.S-01 | As a DevOps engineer, I want to deploy the Kartova agent as a Docker container or K8s Deployment so that it runs in our infrastructure | Docker image published; Helm chart available; agent starts and connects to platform; no inbound ports required |
| E-15.F-01.S-02 | As a DevOps engineer, I want the agent to communicate securely with the platform via outbound-only mTLS so that no inbound firewall rules are needed | Agent initiates connection; mTLS handshake; platform authenticates agent via certificate; data encrypted in transit |
| E-15.F-01.S-03 | As a DevOps engineer, I want to configure the agent from the platform UI so that I don't need to redeploy for config changes | Config pushed from platform; agent polls for updates; config changes applied without restart |

#### Feature E-15.F-02: Health Checks

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-15.F-02.S-01 | As a DevOps engineer, I want the agent to perform HTTP/TCP/gRPC health probes on configured endpoints so that service health is monitored | Probe types configurable per endpoint; probe interval configurable; results forwarded to platform in real-time |
| E-15.F-02.S-02 | As a DevOps engineer, I want health probe results to update the service status in the catalog and status page so that health is reflected everywhere | Probe failure -> status changes to degraded/outage; recovery -> status returns to operational; status page updated |

#### Feature E-15.F-03: Metrics Collection

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-15.F-03.S-01 | As a DevOps engineer, I want the agent to scrape Prometheus metrics endpoints and forward aggregated data so that I don't need to expose Prometheus externally | Prometheus endpoints configured per service; agent scrapes at configured interval; aggregated metrics forwarded to platform |

#### Feature E-15.F-04: Service Discovery

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-15.F-04.S-01 | As a DevOps engineer, I want the agent to discover services running in my K8s cluster so that new deployments are automatically detected | Agent scans K8s API; discovers Deployments/Services/Pods; maps to catalog entities |
| E-15.F-04.S-02 | As a DevOps engineer, I want discovered services to go through an approval workflow before being added to the catalog so that I control what enters the catalog | Discovered services shown as "pending approval"; approve/reject in UI; approved entities added to catalog |

### Epic E-16: Monitoring Integrations

> Integrate with Prometheus and Grafana Cloud for metrics and uptime data.

#### Feature E-16.F-01: Prometheus Integration

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-16.F-01.S-01 | As a DevOps engineer, I want to define uptime rules based on Prometheus queries so that uptime is calculated from real metrics | Rule editor: PromQL query, threshold, evaluation interval; rule linked to service entity |
| E-16.F-01.S-02 | As a DevOps engineer, I want uptime calculated per service per environment from Prometheus data so that historical availability is tracked | Uptime percentage computed; stored with retention policy; visible on entity detail and status page |

#### Feature E-16.F-02: Grafana Cloud Integration

| Story ID | User Story | Acceptance Criteria |
|----------|-----------|-------------------|
| E-16.F-02.S-01 | As a DevOps engineer, I want to link Grafana dashboards to services so that monitoring context is accessible from the catalog | Dashboard URL stored per service; link on entity detail page; embedded preview (optional) |
