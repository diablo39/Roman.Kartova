# ADR-0024: Docker Compose for Local Dev

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Platform Infrastructure
**Related:** ADR-0022 (K8s)

## Context

Local development must reproduce the platform's dependencies (PostgreSQL, Elasticsearch, KeyCloak, soon the message bus and blob storage) with minimal friction. A solo developer optimizes for time-to-`dotnet run`.

## Decision

Provide a `docker-compose.yml` at the repo root that brings up PostgreSQL, Elasticsearch, KeyCloak, and any other required dependencies. The API and frontend run on the host (or in their own containers when desired). Seed data and migrations run on startup.

## Rationale

- Fastest local onboarding — `docker compose up` and go.
- Parity with production dependency versions reduces "works on my machine."
- Docker Desktop / Podman Desktop is ubiquitous on developer machines.

## Alternatives Considered

- **DevContainers** — great for VS Code but heavier and prescriptive; Compose is still underneath.
- **Tilt / Skaffold + Kind** — reproduces K8s but is slower for tight dev loops.
- **Local binaries + hosted services** — contaminates dev cycles with environment differences.
- **.NET Aspire** — attractive and stack-consistent; re-evaluate once Aspire matures further.

## Consequences

**Positive:**
- Frictionless onboarding
- Single source of truth for local infra

**Negative / Trade-offs:**
- Compose doesn't match K8s exactly — divergence risk on networking/secrets
- Resource-hungry on lower-end dev laptops

**Neutral:**
- Can be augmented with Aspire or Tilt later without abandoning Compose

## References

- Phase 0: E-01.F-01.S-03
