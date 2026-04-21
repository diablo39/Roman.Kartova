# ADR-0060: Three-Probe Health Check Endpoints Using ASP.NET Core HealthChecks Framework

**Status:** Accepted
**Date:** 2026-04-21
**Deciders:** Roman Głogowski (solo developer)
**Category:** Observability & Monitoring
**Related:** ADR-0001 (PostgreSQL), ADR-0002 (Elasticsearch), ADR-0003 (Kafka), ADR-0004 (MinIO), ADR-0006 (KeyCloak), ADR-0008 (RBAC), ADR-0022 (K8s deployment), ADR-0027 (.NET backend), ADR-0036 (Prometheus monitoring), ADR-0053 (status page 99.99% SLA), ADR-0058 (structured logging), ADR-0059 (Prometheus metrics), ADR-0076 (platform SLA)

## Context

The Kartova platform deploys on Kubernetes (ADR-0022). Every pod needs liveness and readiness probes for self-healing and traffic routing. Additionally, the platform has several external dependencies (PostgreSQL, Elasticsearch, KeyCloak, Kafka, MinIO) whose availability must be tracked separately from the platform process itself — a dependency outage should cause the platform to stop accepting new traffic (so healthy replicas or a queueing layer can absorb load), but should **not** cause K8s to restart all pods (which would make the situation worse).

Health check endpoints are also consumed by:
- K8s kubelet (liveness, readiness, startup probes)
- Prometheus uptime rules (ADR-0036) — platform SLA tracking toward ADR-0076 (99.9% platform / 99.99% status page)
- Status page component monitoring (ADR-0053)
- External blackbox probing (optional)
- Operations team for debugging

Startup scenarios include EF Core database migrations and dependency warmup, which can exceed a normal liveness probe timeout. Kubernetes 1.16+ offers a dedicated `startupProbe` for this case.

## Decision

**Three endpoints, each served with the correct K8s semantic:**

| Endpoint | K8s probe | Purpose | Checks |
|----------|-----------|---------|--------|
| `GET /health/startup` | `startupProbe` | Initialization (migrations, warmup) | Process + all deps + DB migrations complete |
| `GET /health/live` | `livenessProbe` | Is the process alive? | Process responsive only (no dependency checks) |
| `GET /health/ready` | `readinessProbe` | Is the pod ready to serve traffic? | Process + all external dependencies |

**Implementation via ASP.NET Core HealthChecks framework (built-in to ASP.NET Core; target .NET 10 LTS).** Every check is registered with tags (`live`, `ready`, `startup`); routes filter by tag:

```csharp
builder.Services.AddHealthChecks()
    .AddNpgSql(connStr, tags: ["ready", "startup"])
    .AddElasticsearch(esOptions, tags: ["ready", "startup"])
    .AddKeycloak(kcOptions, tags: ["ready", "startup"])
    .AddKafka(kafkaOptions, tags: ["ready"])
    .AddMinio(minioOptions, tags: ["ready"])
    .AddCheck<MigrationsHealthCheck>("migrations", tags: ["startup"])
    .AddCheck<ProcessLivenessCheck>("process", tags: ["live", "ready", "startup"]);

app.MapHealthChecks("/health/live",    new() { Predicate = r => r.Tags.Contains("live") });
app.MapHealthChecks("/health/ready",   new() { Predicate = r => r.Tags.Contains("ready") });
app.MapHealthChecks("/health/startup", new() { Predicate = r => r.Tags.Contains("startup") });
```

**Critical design rule — liveness does not check dependencies.** If Elasticsearch fails, K8s should **not** restart Kartova API pods (restart does not fix Elasticsearch; it multiplies load on recovery). Instead, readiness fails, pods leave the Service endpoints, and traffic drains; when Elasticsearch recovers, readiness returns and pods rejoin automatically. This asymmetry is the standard K8s pattern and the reason three probes exist.

**Response format (JSON):**

```json
GET /health/ready
Status: 200 OK (or 503 if Unhealthy)
{
  "status": "Healthy",
  "totalDuration": "00:00:00.0432",
  "entries": {
    "postgresql":    { "status": "Healthy",  "duration": "00:00:00.0098", "tags": ["ready","startup"] },
    "elasticsearch": { "status": "Healthy",  "duration": "00:00:00.0123", "tags": ["ready","startup"] },
    "keycloak":      { "status": "Degraded", "duration": "00:00:02.001",  "tags": ["ready","startup"], "description": "Slow response (2s)" },
    "kafka":         { "status": "Healthy",  "duration": "00:00:00.0234", "tags": ["ready"] },
    "minio":         { "status": "Healthy",  "duration": "00:00:00.0156", "tags": ["ready"] }
  }
}
```

HTTP status mapping: `Healthy` → 200, `Degraded` → 200 (pod stays in Service), `Unhealthy` → 503 (pod removed from Service).

**Security model:**

| Endpoint | Exposure | Auth required? |
|----------|----------|----------------|
| `/health/live`, `/health/ready`, `/health/startup` | Public (K8s probes from kubelet, no auth context) | No |
| `/health/detailed` | Public URL but auth-gated (Bearer JWT, Operations role) | Yes |

Public probe endpoints return per-check status but no secrets (no connection strings, no hostnames exceeding what's already in config). The detailed endpoint adds version info, full dependency URIs, timing percentiles, and recent error samples for operations debugging.

**Out of scope:** ASP.NET Core HealthChecks UI is not exposed publicly. Operations use the auth-gated `/health/detailed` endpoint or internal dashboards backed by ADR-0059 Prometheus metrics.

## Rationale

- **Three probes match K8s best practice** — `startupProbe` handles slow init (migrations, warmup) without false liveness failures; `livenessProbe` detects hung processes; `readinessProbe` gates traffic based on dependency availability
- **Liveness excludes dependencies** — prevents cascade restart loops when an external dependency fails; this is the single most common K8s health-check mistake and this ADR makes the intent explicit
- **ASP.NET Core HealthChecks framework** — built-in, mature, zero external dependencies, supports tagging out of the box, produces structured JSON; integrates with every dependency via existing NuGet packages (AspNetCore.HealthChecks.NpgSql, .Elasticsearch, .Keycloak, .Kafka, .Minio)
- **Structured JSON response** — machine-parseable by Prometheus uptime rules (ADR-0036), status page (ADR-0053), and operations dashboards; per-dependency status enables precise SLA attribution
- **Degraded status** — allows pods to continue serving while flagging slow dependencies, without triggering K8s actions; this matches PostgreSQL/ES intermittent latency spikes that don't warrant removing pods from Service
- **Auth-gated detailed endpoint** — keeps sensitive debug info out of public response without blocking K8s probes

## Alternatives Considered

- **Single combined `/health` endpoint:** K8s still needs separate liveness and readiness semantics — a single endpoint forces workarounds (different HTTP codes, query parameters). Anti-pattern since K8s 1.16. Rejected.
- **Two endpoints (live + ready only, no startup):** Works but migration run can exceed liveness timeout and cause false restart loops; startup probe exists precisely to avoid this. Rejected.
- **ASP.NET Core HealthChecks framework + public HealthChecks UI dashboard:** UI is convenient for dev but publicly exposing it leaks dependency topology (hostnames, types, versions) that aids attackers. Acceptable only behind auth, which duplicates the existing `/health/detailed` pattern. Rejected.
- **External blackbox probing only (Prometheus blackbox_exporter):** Does not satisfy K8s probe requirements; pods cannot self-heal or gate traffic without kubelet-callable endpoints. Could complement but not replace this design. Rejected as sole strategy.
- **Custom minimal handlers (no framework):** Saves one NuGet dependency but requires re-implementing timeout handling, tagged registration, JSON serialization, and per-dependency check logic. Framework code is production-battle-tested; custom code would take longer to harden. Rejected.
- **Liveness that includes dependencies:** Would cause cascade restarts across all Kartova pods on any dependency failure, overloading the dependency on recovery and multiplying outage duration. Classic anti-pattern. Rejected explicitly in the Decision.

## Consequences

**Positive:**
- Matches K8s 1.16+ best practice; behavior is predictable and debuggable
- Liveness isolation prevents cascade restart during dependency outages
- Structured JSON enables automated uptime tracking (ADR-0036) and status page wiring (ADR-0053)
- Per-dependency status aids SLA attribution toward ADR-0076 (platform 99.9%)
- Framework code is maintained by .NET team — security and correctness come free
- Adding new dependency checks is one-line registration
- Startup probe decouples migration timing from liveness timeout

**Negative / Trade-offs:**
- Three endpoints instead of one — slightly more K8s manifest configuration per service
- Developers must understand which checks belong on which probe — documentation and code review catch misplacements
- Public probe responses include dependency names (e.g., "postgresql", "elasticsearch") — low-sensitivity information leakage, mitigated by limiting response detail vs `/health/detailed`
- `/health/detailed` adds an authenticated endpoint that operations team must know exists and is allowed to access (RBAC per ADR-0008)

**Neutral:**
- Prometheus metrics (ADR-0059) remain the primary monitoring surface; health endpoints are complementary, not substitutes
- Health check implementations become a small module in the API codebase; can be reused by the status page service (ADR-0023) for its own probes
- Kafka and MinIO were not in the original phase-0 story; added here because they are dependencies per ADR-0003 and ADR-0004 — phase-0 story will be amended accordingly

## Implementation Notes

**Kubernetes probe configuration:**

```yaml
spec:
  containers:
  - name: kartova-api
    startupProbe:
      httpGet: { path: /health/startup, port: 8080 }
      periodSeconds: 10
      failureThreshold: 30
    livenessProbe:
      httpGet: { path: /health/live, port: 8080 }
      periodSeconds: 10
      timeoutSeconds: 2
      failureThreshold: 3
    readinessProbe:
      httpGet: { path: /health/ready, port: 8080 }
      periodSeconds: 5
      timeoutSeconds: 3
      failureThreshold: 2
```

**Custom liveness check (process-only, no deps):**

```csharp
public class ProcessLivenessCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext ctx, CancellationToken ct)
        => Task.FromResult(HealthCheckResult.Healthy("Process alive"));
}
```

**Migrations check (startup only):**

```csharp
public class MigrationsHealthCheck : IHealthCheck
{
    private readonly KartovaDbContext _db;
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext ctx, CancellationToken ct)
    {
        var pending = await _db.Database.GetPendingMigrationsAsync(ct);
        return pending.Any()
            ? HealthCheckResult.Unhealthy($"{pending.Count()} migrations pending")
            : HealthCheckResult.Healthy("Up to date");
    }
}
```

## References

- PRD §7.2 (availability SLAs), §7.3 (compliance)
- Phase 0 E-01.F-07.S-01 (health endpoints)
- Kubernetes probes: https://kubernetes.io/docs/tasks/configure-pod-container/configure-liveness-readiness-startup-probes/
- ASP.NET Core HealthChecks: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks
- AspNetCore.Diagnostics.HealthChecks community package: https://github.com/Xabaril/AspNetCore.Diagnostics.HealthChecks
