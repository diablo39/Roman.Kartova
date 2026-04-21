# Kartova — Project Guide for Claude

**Product:** Kartova — SaaS service catalog + developer portal (Backstage + Compass + Statuspage hybrid)
**Owner:** Solo developer (Roman Głogowski), AI-assisted
**Stack:** .NET 10 (LTS) / ASP.NET Core + EF Core · Wolverine (CQRS mediator + outbound + outbox) · KafkaFlow (inbound Kafka consumers) · React + TypeScript · PostgreSQL 16 (RLS) · Elasticsearch · Apache Kafka (Strimzi/KRaft) · MinIO (S3) · KeyCloak (OIDC/JWT) · Kubernetes

## Where to find things

| Topic | File |
|-------|------|
| Product requirements (source of truth) | [docs/product/PRODUCT-REQUIREMENTS.md](docs/product/PRODUCT-REQUIREMENTS.md) |
| Backlog index (30 epics, 73 features, 209 stories) | [docs/product/EPICS-AND-STORIES.md](docs/product/EPICS-AND-STORIES.md) |
| Progress checklist | [docs/product/CHECKLIST.md](docs/product/CHECKLIST.md) |
| Phase files (0–9) | `docs/product/phases/phase-N-*.md` |
| ADR library (79 accepted) + keyword index | [docs/architecture/decisions/README.md](docs/architecture/decisions/README.md) |
| Individual ADRs | `docs/architecture/decisions/ADR-NNNN-*.md` |
| ADR candidates (historical) | [docs/architecture/ADR-CANDIDATES.md](docs/architecture/ADR-CANDIDATES.md) |
| Design system (tokens, nav specs) | [docs/design/DESIGN.md](docs/design/DESIGN.md) |
| Google Stitch prompts | [docs/design/STITCH-PROMPTS.md](docs/design/STITCH-PROMPTS.md) |

## Conventions

- Epic = `E-XX` · Feature = `E-XX.F-YY` · Story = `E-XX.F-YY.S-ZZ`
- ADR = `ADR-NNNN` (Michael Nygard template)
- Dates: always absolute (`2026-04-21`), never relative
- Windows shell: `cmd /c` or PowerShell wrappers for `dotnet` commands; Git Bash lacks `grep -P` (use `-E` or `Select-String`)

## Key architectural decisions

Quick lookup; full context in the ADR library keyword index.

| Area | Decision | ADR |
|------|----------|-----|
| Backend | .NET 10 (LTS) + ASP.NET Core + EF Core, Clean Architecture per module | ADR-0027, ADR-0028 |
| Solution style | Modular monolith — one csproj tree per bounded context, NetArchTest fitness functions enforce boundaries | ADR-0082 |
| Frontend | React SPA + TypeScript strict, Vite, TanStack Query | ADR-0039 |
| Database | PostgreSQL 16 with Row-Level Security (not schema-per-tenant) | ADR-0001, ADR-0012 |
| Search | Elasticsearch shared index + per-tenant routing + filtered aliases | ADR-0002, ADR-0013 |
| Messaging | Apache Kafka via Strimzi on K8s, KRaft mode (not RabbitMQ/Redpanda) | ADR-0003 |
| Kafka outbound | Wolverine with transactional outbox (EF Core + PostgreSQL) | ADR-0080 |
| Kafka inbound | KafkaFlow — per-key parallel-within-partition workers | ADR-0081 |
| CQRS mediator | Wolverine (mandatory pattern); MediatR and MassTransit **not** used | ADR-0028, ADR-0080 |
| Blob storage | S3 abstraction with MinIO default | ADR-0004 |
| Identity | KeyCloak self-hosted, OIDC/JWT | ADR-0006, ADR-0007 |
| API style | REST (not GraphQL), OpenAPI 3.x auto-generated | ADR-0029, ADR-0034 |
| Webhooks | HMAC-SHA256 + retry + DLQ + idempotency keys | ADR-0033 |
| Agent transport | HTTPS polling (not gRPC streaming) — proxy-friendly | ADR-0042 |
| Agent binary | .NET AOT, long-lived token with dual-token rotation | ADR-0041, ADR-0042 |
| CLI | `dotnet tool` + standalone AOT binaries | ADR-0046 |
| Health checks | Three K8s probes via ASP.NET Core HealthChecks | ADR-0060 |
| Entity model | 9 fixed types + JSONB `custom_attributes` (MVP); 10th Custom Entity in Phase 2 | ADR-0064 |
| Pricing | Four tiers: Free / Starter / Pro / Enterprise | ADR-0061 |
| Encryption | Narrow scope — OAuth tokens (column-level) + TLS 1.2+; **mTLS not used** | ADR-0077 |
| Deployment | Kubernetes, cloud-agnostic (no managed-service lock-in) | ADR-0022 |
| Local dev | Docker Compose | ADR-0024 |

## Phases (MVP = 0–5)

0. Foundation (E-01) — scaffolding, CI/CD, auth, compliance, observability
1. Core Catalog & Notifications (E-02..E-06, E-06a)
2. Auto-Import (E-07..E-10) — GitHub, Azure DevOps, scorecards
3. Documentation (E-11) — markdown, OpenAPI, AsyncAPI
4. Status Page (E-12)
5. CLI, Policy & Billing (E-13, E-14, E-14a)

Post-MVP: 6 Agent · 7 Intelligence · 8 Analytics · 9 Advanced

## Working agreements

- **Before architectural suggestions:** check ADR keyword index in `docs/architecture/decisions/README.md` — 79 decisions already made
- **Before adding features:** verify they're not already scoped in `EPICS-AND-STORIES.md`; map each feature to its owning module (ADR-0082)
- **Cross-module interactions:** only via Wolverine `IMessageBus` (in-process) or Kafka events; never direct references to other modules' Domain/Application/Infrastructure
- **When proposing new ADRs:** preview decision before saving (user reviews first)
- **Dates in memory/docs:** absolute (convert "Thursday" → `2026-03-05`)
- **Implementation work:** use superpowers skills; `/gsd:*` commands for phase planning
- **Compliance:** GDPR + MiFID II from day one — not bolted on later (see E-01.F-05)
