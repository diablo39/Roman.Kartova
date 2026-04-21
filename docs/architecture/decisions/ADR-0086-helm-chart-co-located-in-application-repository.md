# ADR-0086: Helm Chart Co-located in Application Repository

**Status:** Accepted
**Date:** 2026-04-21
**Deciders:** Roman Głogowski (solo developer)
**Category:** Deployment & Operations
**Related:** ADR-0022 (Kubernetes cloud-agnostic), ADR-0024 (Docker Compose local), ADR-0025 (CI/CD), ADR-0043 (agent Helm chart), ADR-0082 (Modular monolith), ADR-0085 (migration Job)

## Context

Kartova deploys to Kubernetes (ADR-0022). A Helm chart is needed to template the Deployment, Service, Ingress, ConfigMap, Secret, migration Job (ADR-0085), HPA, and Strimzi/NetworkPolicy resources. Two organizational choices exist:

1. **Separate repo** (`kartova-helm`) — common in large orgs; chart has its own lifecycle, versioning, and release pipeline
2. **Co-located in application repo** (`deploy/helm/kartova/`) — chart ships with the code it deploys

## Decision

The Helm chart lives in the application repository at **`deploy/helm/kartova/`**. It is versioned alongside the application, released in the same git tag, and published to an OCI-compatible chart registry on release (GHCR or equivalent) by CI.

**Repository layout:**

```
/
  src/                         # .NET modules
  web/                         # React SPA
  deploy/
    helm/
      kartova/                 # umbrella chart for the platform
        Chart.yaml
        values.yaml
        values.production.yaml
        values.staging.yaml
        templates/
          api-deployment.yaml
          web-deployment.yaml
          migrator-job.yaml    # ADR-0085
          ingress.yaml
          configmap.yaml
          secret.yaml
          hpa.yaml
          networkpolicy.yaml
        charts/                # subcharts (Strimzi Kafka, etc., if vendored)
    docker-compose.yaml        # local dev (ADR-0024)
    docker-compose.ci.yaml     # CI integration tests
  .github/workflows/
    release.yaml               # builds + publishes image and chart on tag
```

**Separate chart for the Hybrid Agent** (ADR-0043) remains in the agent's own sub-directory (`deploy/helm/kartova-agent/`) because it is installed by customers into their networks on a different lifecycle.

## Rationale

- **Single source of truth** — app code and the manifest that deploys it evolve together; no cross-repo version skew
- **Atomic changes** — adding an env var requires both a code change and a Helm value; co-location makes this one PR, one review, one tag
- **Solo-dev productivity** — one clone, one PR, one CI pipeline
- **Modular monolith fit** (ADR-0082) — the chart reflects module composition and can enable/disable modules via values flags
- **Release correlation** — `Chart.yaml:appVersion` is always the same as the git tag; deploying chart vX.Y.Z always means deploying app vX.Y.Z
- **GitOps friendly** — a GitOps controller (ArgoCD / Flux) points at the repo's `deploy/helm/` path
- **Consistent with ADR-0043** — the agent's chart is already in-repo; this extends the pattern to the platform itself

## Alternatives Considered

- **Separate `kartova-helm` repo** — common in platform engineering orgs with dedicated SRE teams; for a solo dev, adds a second CI pipeline, cross-repo PR coordination, and drift risk. Rejected.
- **Kustomize instead of Helm** — simpler templating but weaker packaging/distribution story; Helm is the de-facto K8s app distribution format and ecosystem is broader (Strimzi, KeyCloak, MinIO, Prometheus all ship Helm charts)
- **Raw manifests under `deploy/k8s/`** — no templating, no packaging, no release distribution; rejected
- **App Operator (CRD)** — heavy pattern, premature for MVP; Helm is good enough; an operator can be added later if self-service multi-tenancy on customer clusters becomes a product feature

## Consequences

**Positive:**
- One repo, one tag, one release — simplest mental model
- Chart PRs reviewed alongside code PRs; no cross-repo merge coordination
- CI can test the chart (helm lint, helm template, kind-cluster smoke test) on every PR
- Customers / partners deploying on-prem get a single tarball per release

**Negative / Trade-offs:**
- Chart changes trigger full CI pipeline even when code is unchanged (mitigated by path filters)
- Chart consumers outside the repo (hypothetical design partners) must pull from the same release cadence
- If Kartova ever splits into multiple independently-deployed services, the chart will need restructuring

**Neutral:**
- Chart publishing target (OCI registry — GHCR) decided at implementation time in Phase 0
- Subcharts for infra components (Strimzi, Apicurio) are referenced as dependencies, not vendored — reduces repo bloat

## Implementation Notes

- `Chart.yaml:version` = chart version (semver for chart schema)
- `Chart.yaml:appVersion` = application version (matches git tag)
- CI release workflow: on tag `v*`, build images → `helm package` → push chart to GHCR as OCI artifact
- `values.yaml` holds defaults; `values.production.yaml` / `values.staging.yaml` override per environment
- `helm lint` and `helm template | kubeval` run in CI as gates
- Chart README documents values schema (generated via `helm-docs`)

**Example `Chart.yaml`:**

```yaml
apiVersion: v2
name: kartova
description: Kartova service catalog and developer portal
type: application
version: 0.1.0
appVersion: "0.1.0"
dependencies:
  - name: strimzi-kafka-operator
    version: "0.40.0"
    repository: https://strimzi.io/charts/
    condition: kafka.enabled
```

## References

- Helm OCI registry support: https://helm.sh/docs/topics/registries/
- Phase 0: E-01.F-01 (scaffolding), E-01.F-02 (CI/CD)
- ADR-0022 (K8s target), ADR-0025 (CI/CD), ADR-0043 (agent chart precedent), ADR-0085 (migrator Job template)
