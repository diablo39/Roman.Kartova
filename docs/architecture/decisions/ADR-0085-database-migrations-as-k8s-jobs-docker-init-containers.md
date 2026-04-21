# ADR-0085: Database Migrations Execute as K8s Jobs and Docker Init Containers, Not at Application Startup

**Status:** Accepted
**Date:** 2026-04-21
**Deciders:** Roman Głogowski (solo developer)
**Category:** Deployment & Operations
**Related:** ADR-0001 (PostgreSQL), ADR-0022 (Kubernetes), ADR-0024 (Docker Compose local dev), ADR-0025 (CI/CD), ADR-0074 (1000-tenant scale), ADR-0086 (Helm chart location)

## Context

EF Core migrations must run before application pods serve traffic. Three common patterns exist:

1. **At app startup** — `dbContext.Database.Migrate()` in `Program.cs`
2. **Pre-deploy Job** — Kubernetes `Job` runs migrations, then `Deployment` rolls out
3. **CI/CD step** — migration runs from CI runner using a DB connection string

Pattern 1 is tempting for simplicity but breaks at scale: with 3+ replicas, multiple pods race to acquire the EF migration lock; `__EFMigrationsHistory` serializes them but wastes startup time, blocks readiness probes, and conflates "deploy the app" with "change the schema" (they have different blast radius and rollback stories).

At 1000-tenant scale (ADR-0074) with rolling deployments multiple times per day, the separation of concerns matters.

## Decision

**Kubernetes production deployments:** migrations run as a pre-deployment **`Job`** (`Kartova.Migrator` container) with a Helm `pre-install` / `pre-upgrade` hook. The application `Deployment` rolls out only after the Job succeeds. The app image itself **does not** call `Migrate()` at startup.

**Docker Compose local development:** migrations run as a **dedicated init container** (`migrator` service) with `depends_on: { postgres: { condition: service_healthy } }`; app services `depends_on: { migrator: { condition: service_completed_successfully } }`.

**CI/CD (ADR-0025):** staging and production deploys invoke the migrator image (same image as K8s Job) against the target DB before promoting the app release.

**Dedicated migrator container:**

- Published as a separate image tag: `kartova/migrator:{version}`
- Runs `dotnet Kartova.Migrator.dll --module=all` (per-module migrations via modular monolith, ADR-0082)
- Exits 0 on success, non-zero on failure → Job/init-container fails → rollout aborts
- Read-only filesystem, non-root user, no network access beyond the database

## Rationale

- **No startup-time race** — single serialized migration run regardless of replica count
- **Clear rollback story** — schema change is its own event, recorded separately from app deploy; can roll back app without touching schema
- **Faster pod startup** — no `Migrate()` blocking readiness probes, better during traffic spikes
- **Aligns with Helm hooks** — `pre-upgrade` hook is idiomatic for this pattern
- **Docker parity** — init container pattern gives local dev the same ordering guarantees as production
- **Per-module execution** — each module's `DbContext` (ADR-0082) runs in sequence; a single migration Job orchestrates all of them
- **Security** — migrator container has DB credentials with DDL rights; app containers get DML-only credentials

## Alternatives Considered

- **App startup `Migrate()`** — rejected: race conditions, coupled concerns, slow startup at scale
- **EF Core bundles via CI step only** — works but loses K8s/Docker parity; local `docker-compose up` breaks without an extra manual step
- **Dedicated DBA-run migrations** — overkill for solo dev; automation prevents skew between environments
- **Flyway / Liquibase** — excellent tools but adds a non-.NET dependency; EF Core migrations are stack-native and the team (solo) knows them

## Consequences

**Positive:**
- Schema changes and app deployments are independently rollable
- No DB thrash during replica scale-up
- Local dev (`docker compose up`) and prod both honor the same migrate-then-start ordering
- Separation supports blue-green and canary patterns later

**Negative / Trade-offs:**
- Extra container image to build, scan, and version (`Kartova.Migrator`)
- Helm chart complexity increases — extra Job template + hook ordering
- Failed migrations block deploys entirely — good for safety, sometimes painful during hotfixes

**Neutral:**
- Migrator image is small (~80 MB .NET runtime + migration bundles); build cost negligible
- Per-module migrations can run in parallel later if sequential becomes a bottleneck (not expected for MVP)

## Implementation Notes

**Helm chart (per ADR-0086) ships:**

```yaml
# templates/migrator-job.yaml
apiVersion: batch/v1
kind: Job
metadata:
  name: "{{ .Release.Name }}-migrator-{{ .Release.Revision }}"
  annotations:
    "helm.sh/hook": pre-install,pre-upgrade
    "helm.sh/hook-weight": "-10"
    "helm.sh/hook-delete-policy": before-hook-creation,hook-succeeded
spec:
  backoffLimit: 1
  template:
    spec:
      restartPolicy: Never
      containers:
        - name: migrator
          image: "{{ .Values.image.repository }}/migrator:{{ .Values.image.tag }}"
          args: ["--module=all"]
          envFrom:
            - secretRef:
                name: "{{ .Release.Name }}-db-migrator-creds"
```

**Docker Compose local:**

```yaml
services:
  migrator:
    image: kartova/migrator:dev
    depends_on:
      postgres: { condition: service_healthy }
    environment:
      DB_CONNECTION: "Host=postgres;Database=kartova;Username=migrator;..."
  api:
    image: kartova/api:dev
    depends_on:
      migrator: { condition: service_completed_successfully }
```

**Migrator entry point (simplified):**

```csharp
foreach (var module in Modules.All)
{
    using var scope = host.Services.CreateScope();
    var ctx = scope.ServiceProvider.GetRequiredService(module.DbContextType) as DbContext;
    await ctx.Database.MigrateAsync();
}
```

## References

- Helm hooks: https://helm.sh/docs/topics/charts_hooks/
- EF Core migration bundles: https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying
- Phase 0: E-01.F-03 (DB foundation), E-01.F-02 (CI/CD)
