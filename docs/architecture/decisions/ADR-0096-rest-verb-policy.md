# ADR-0096: REST Verb Policy — PUT for Full Replacement, POST for Actions, No PATCH

**Status:** Accepted
**Date:** 2026-05-06
**Deciders:** Roman Głogowski (solo developer)
**Category:** API & Integration Architecture
**Related:** ADR-0029 (REST as primary API style), ADR-0091 (RFC 7807 error responses), ADR-0092 (REST URL convention), ADR-0073 (entity lifecycle states), ADR-0095 (cursor pagination contract).

## Context

The Catalog `Application` aggregate is the first entity to acquire an edit endpoint and lifecycle-transition endpoints (slice 5). The HTTP verb landscape gives us three options for "change something on this resource": `PUT`, `PATCH`, and named-action `POST`. ADR-0029 picked REST as the API style without freezing verb usage. The first edit slice forces the question — and whatever pattern lands here will be copy-pasted across roughly twenty edit endpoints over the rest of the catalog (Component, Service, API, Infrastructure, Broker, Environment, Deployment).

`PATCH` is attractive in theory: sparse update, send only what changes. In practice three things degrade it:

1. **Semantics drift.** RFC 7396 (JSON Merge Patch), RFC 6902 (JSON Patch), and ad-hoc "send the diff" all coexist as production patterns. Within one team it's fine; across the API surface — including future CLI / agent / webhook consumers — the format choice multiplies bug surface.
2. **Missing-vs-null ambiguity.** `{ "displayName": null }` reads as "clear the field" in JSON Merge Patch and "no change" in absent-key semantics. Codegen typings have to model this distinction (often as `T | null | undefined`) and downstream client code has to handle it. The fields where this matters (nullable foreign keys, optional metadata) are exactly the ones where bugs hide longest.
3. **Codegen support is uneven.** OpenAPI generators handle `PUT` body schemas trivially; `PATCH` schemas vary by tool and require explicit signaling that a property's absence is meaningful.

A CQRS-shaped service has a clean alternative: **named action endpoints**. "Change one thing" becomes "invoke the command that changes that thing" — `POST /applications/{id}/deprecate`, `POST /applications/{id}/restore`, `POST /applications/{id}/transfer-ownership`. Each maps 1:1 to a domain method, gets its own per-route authorization policy, and reads in OpenAPI like the ubiquitous language. Sparse updates are expressed as commands, not as wire-format gymnastics.

That leaves `PUT` for full-resource replacement on stable, small DTOs (e.g., the `EditApplicationRequest { displayName, description }` two-field shape).

## Decision

1. **`PUT /resources/{id}`** is used for idempotent full-resource replacement when the editable surface of the resource is small and stable. The request body carries every editable field; missing fields are an error, not "no change." Concurrency is enforced via `If-Match` / `ETag` (slice 5 §6 of the spec).

2. **`POST /resources/{id}/<action>`** is used for domain commands — anything that maps to a named domain method (`deprecate`, `decommission`, `restore`, `transfer-ownership`, `regenerate-token`). Each action endpoint takes a precise request DTO (often empty), has its own authorization policy, and emits its own OpenAPI operation (`deprecateApplication`, `decommissionApplication`).

3. **`PATCH` is forbidden.** Code review and an architecture test reject any new `PATCH` endpoint.

4. **Bulk operations** (E-01.F-06.S-03 — separate epic) get their own collection-action endpoints (`POST /applications/bulk/deprecate`); they are not modeled as `PATCH` on the collection.

## Rationale

- One verb (`PUT`) for "replace this whole resource." Idempotent. Body is the resource. No semantic ambiguity.
- One verb (`POST`) for "invoke this domain command." Each command is its own endpoint, its own authorization rule, its own OpenAPI op. Reads like the ubiquitous language.
- Zero verbs (`PATCH`) where semantics drift. The "I only want to change displayName" case is solved by a small two-field `PUT` body or by a future named-action endpoint if the field has command-like semantics (e.g., `POST /applications/{id}/rename` if and when slug rename is supported).
- Per-route authorization is declarative — each route is one `[Authorize(Policy = ...)]` attribute. With a discriminated `PATCH` body or a single fat `POST /lifecycle { to: ... }`, authorization becomes "if body field is X, require admin" — imperative, easy to drift, hard to assert via arch test.

## Alternatives Considered

- **`PATCH` with JSON Merge Patch (RFC 7396).** Rejected for the missing-vs-null ambiguity and codegen drift outlined above.
- **Single `POST /resources/{id}/lifecycle` with `{ to: <state>, ... }` body.** Rejected because it forces a fat handler with a `switch (cmd.To)` + per-state validation branches. Per-action endpoints scale the same number of routes but keep handlers thin and per-route authorization declarative.
- **Verb-per-state in the URL but `PATCH`-style sparse body** (e.g., `PATCH /applications/{id}` with `{ lifecycle: "deprecated", sunsetDate: "..." }`). Rejected for collapsing two distinct authorization concerns (metadata edit vs lifecycle transition) into one route.

## Consequences

**Positive:**
- Clients (TypeScript SDK, future CLI, future agents) get one operation per command — names read like the domain.
- Per-route authorization stays declarative; future RBAC retrofit (E-01.F-04.S-03) is one attribute per admin endpoint, not a refactor of branching code.
- Audit logging (when E-01.F-03.S-03 lands) keys off the route name — the route IS the action.
- OpenAPI surface self-documents. No "what does `PATCH` do here" reading required.

**Negative / Trade-offs:**
- More routes per resource. A resource with three lifecycle transitions has three routes, not one.
- "Change one nullable field" cases that don't map to a named command (genuine sparse update) cannot be expressed at all under this policy. If such a case appears, the resolution is to either expand the `PUT` body (fields stay required, send the unchanged ones explicitly) or introduce a named action. Neither has been needed yet; this ADR is amended (not violated) if and when one does.

**Neutral:**
- The architecture test that pins absence of `PATCH` (Task 3 of slice 5) costs nothing to maintain.

## Implementation notes

- Slice 5 introduces three new endpoints under this policy: `PUT /api/v1/catalog/applications/{id}`, `POST /api/v1/catalog/applications/{id}/deprecate`, `POST /api/v1/catalog/applications/{id}/decommission`. Each is the reference exemplar.
- The architecture test `RestVerbPolicyRules.No_endpoint_uses_PATCH_verb` (Task 2 of slice 5) walks the `EndpointDataSource` after `WebApplicationFactory` boot and asserts no endpoint metadata declares a `PATCH` HTTP method.

## References

- ADR-0029 (REST as primary API style)
- ADR-0091 (RFC 7807 error responses)
- ADR-0092 (REST URL convention — module slug as URL segment)
- Slice 5 spec — `docs/superpowers/specs/2026-05-06-slice-5-applications-edit-lifecycle-design.md`
