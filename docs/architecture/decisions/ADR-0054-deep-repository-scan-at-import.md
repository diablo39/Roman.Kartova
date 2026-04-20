# ADR-0054: Deep Repository Scan at Import-Time

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Scan / Import Architecture
**Related:** ADR-0035 (git first-class), ADR-0055 (scan resilience), ADR-0056 (manual precedence), ADR-0057 (OAuth connection)

## Context

Kartova's key differentiator versus Backstage is that it does *not* require every repository to maintain a hand-edited `catalog-info.yaml` (PRD §4.2.2). Instead, the platform auto-discovers as much as possible from the actual code and config. For this to deliver the promised "zero-friction onboarding," the scan must go well beyond filenames — it needs to understand what the repo is, what it produces, and what it talks to.

## Decision

Import-time and scheduled re-scans perform a **deep repository scan** that extracts:

- **Language & framework**: via file patterns, `package.json`, `csproj`, `pom.xml`, `go.mod`, `Cargo.toml`, `requirements.txt`/`pyproject.toml`, etc.
- **Container & infra artifacts**: `Dockerfile`, `docker-compose.yml`, `Helm` charts, `kustomize`, `Terraform` (.tf), Bicep, Pulumi.
- **API definitions**: OpenAPI/Swagger, gRPC `.proto`, GraphQL SDL.
- **Async messaging**: AsyncAPI, CloudEvents schemas, Confluent/Apicurio schema-registry references (see ADR-0037).
- **Queue/topic names**: extracted from config files and (best-effort) source code string literals.
- **Data & environment references**: DB connection-string *patterns* (never values — ADR-0078, pending), env var *names*.
- **Documentation**: README, `docs/` directory, ADRs, CHANGELOGs.

Outputs populate entity metadata, relationships (producers/consumers of APIs and topics), and documentation links.

## Rationale

- Descriptor-file-only models (Backstage) place ongoing editorial burden on developers — the exact friction Kartova exists to remove.
- The information is already in the repository; not extracting it is leaving value on the table.
- Combining source code, config, and docs gives a multi-signal view that no single source provides on its own.
- Produces actionable relationships (ADR-0067: origin = auto) that tenants can then review (ADR-0045) or supersede (ADR-0056).

## Alternatives Considered

- **Descriptor-file-only (Backstage `catalog-info.yaml`)** — rejected as the primary model; high friction kills adoption. Can be *supported* as an optional override.
- **Shallow metadata-only scan (name, language)** — insufficient signal; no discovered relationships.
- **AST-based semantic scan** — interesting but language-specific and expensive; reserved for future phases where value justifies the cost.
- **Purely runtime-discovered via agent** — complementary, not a replacement; agent visibility is bounded to what is actually deployed.

## Consequences

**Positive:**
- Major UX differentiator: useful catalog on day one, no boilerplate demanded of developers.
- Cross-signal extraction (code + config + docs) yields richer relationships than any single source.
- Discovered data feeds scorecards (ADR-0070) and maturity levels (ADR-0071) without extra work.

**Negative / Trade-offs:**
- Scanner is a non-trivial engineering investment; each new language/framework needs a heuristic set.
- False positives in string-literal extraction must be gated by manual review (ADR-0056).
- Scan time grows with repo size — bounded by the timeout in ADR-0055.

**Neutral:**
- Scanner evolves over time; new extractors can be added without changing the overall architecture.

## References

- PRD §4.2.2
- Phase 2: Feature E-08.F-01 (feature-level)
- Related ADRs: ADR-0035, ADR-0037, ADR-0045, ADR-0055, ADR-0056, ADR-0057, ADR-0067
