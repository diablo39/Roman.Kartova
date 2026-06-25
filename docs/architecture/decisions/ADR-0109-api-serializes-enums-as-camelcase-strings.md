# ADR-0109: REST API Serializes Enums as camelCase JSON Strings

**Status:** Accepted
**Date:** 2026-06-25
**Deciders:** Roman Głogowski (solo developer)
**Category:** API Design & Conventions
**Related:** ADR-0029 (REST as primary API style), ADR-0034 (OpenAPI 3.x), ADR-0091 (ProblemDetails error responses), ADR-0092 (REST API URL convention), ADR-0104 (payload-free outcomes are enums)

## Context

Enums are pervasive on the wire: application lifecycle, health status, entity kind, relationship type and origin, endpoint protocol, RBAC roles, and more. How an enum serializes to JSON is a contract decision with three common answers — integer ordinals, PascalCase member names, or camelCase strings — and choosing implicitly invites drift.

A wrong assumption about enum casing fails **silently at runtime** rather than at compile time, because:

- Minimal-API `[FromQuery] string` parameters (used where an endpoint parses/validates the value itself) generate in the OpenAPI document and the TypeScript client as plain `string`, not as the enum union. A caller can pass a wrong-cased value that type-checks and only mismatches at runtime.
- A hand-authored client-side union (e.g. `"Application" | "Service"`) compiles fine on its own but never equals the actual wire value.

Slice 1b (ADR-0108) hit exactly this: the frontend used PascalCase kind/type literals (`"Application"`, `"DependsOn"`) while the wire format is camelCase (`"application"`, `"dependsOn"`). The mismatch passed per-file `tsc --noEmit` checks and was only caught by the full composite build.

## Decision

All enums cross the API boundary as **camelCase strings**, via a globally configured `System.Text.Json` `JsonStringEnumConverter` with `JsonNamingPolicy.CamelCase` (multi-word members lower-camel; single-word members lowercase).

Consequently:

1. **The OpenAPI document is the single source of truth for enum values.** `@enum` entries are camelCase (e.g. `EntityKind = "application" | "service"`; `RelationshipType = "dependsOn" | … | "partOf"`; `RelationshipOrigin = "manual" | "scan" | "agent"`; `HealthStatus = "unknown" | "healthy" | "degraded" | "unhealthy"`).
2. **The generated TypeScript client carries those camelCase unions.** Frontend code MUST derive enum types from the generated client (`components["schemas"][...]`) — **never hand-author PascalCase literals** that mirror a server enum.
3. **Enum query-string parameters bind case-insensitively server-side** (`Enum.TryParse(ignoreCase: true)` + `Enum.IsDefined`, returning `400` on an unknown value), but the canonical emitted/documented form is camelCase. Loosely-typed `string` query params do not encode the enum in the client, so the client is responsible for sending the camelCase value.
4. Enum **display** is a presentation concern: the UI maps the wire value to a human label (e.g. `"manual" → "Manual"`, `"dependsOn" → "Depends on"`) rather than rendering the raw token.

## Consequences

### Positive

- One predictable, self-describing wire contract; no per-endpoint casing drift.
- The OpenAPI spec + generated client are authoritative, so codegen-derived types stay correct by construction.
- camelCase matches the JSON ecosystem and the TypeScript frontend's conventions.

### Negative / guardrails

- The casing-mismatch class is **invisible to a per-file `tsc --noEmit`** and is only caught by the full composite `tsc -b` (which the web image build / `npm run build` runs). Therefore `tsc -b` / `npm run build` is the binding frontend type gate before merge — a per-call typecheck is not sufficient.
- Hand-authored enum unions are a latent bug; reviews should reject PascalCase enum literals in favour of generated-client types.
- Renaming a C# enum member changes the wire value (camelCased) — a breaking contract change, to be treated as such once external consumers exist.

## References

- Global converter configuration in the API host (`Kartova.Api` JSON options).
- Generated client + snapshot: `web/src/generated/openapi.ts` (gitignored) / `web/openapi-snapshot.json` (committed; CI codegen fallback).
- Motivating incident: Slice 1b casing fix (`feat/catalog-relationships-ui-surface`), ADR-0108.
