# ADR-0091: RFC 7807 Problem Details for All HTTP Error Responses

**Status:** Accepted
**Date:** 2026-04-22
**Deciders:** Roman Głogowski (solo developer)
**Category:** API & Integration Architecture
**Related:** ADR-0029 (REST), ADR-0034 (OpenAPI), ADR-0058 (structured logs with tenant context)

## Context

Kartova's REST API (ADR-0029) will be consumed by:

- The React SPA (ADR-0039)
- The CLI (`dotnet tool`, ADR-0046)
- Customer integrations (webhooks, bulk API, scorecards)
- The Agent binary (ADR-0041/0042)
- Future third-party integrations

Error responses need a consistent, machine-readable shape so every client can surface diagnostic information uniformly (trace IDs, retryable vs permanent, extension fields for validation errors, etc.). Ad-hoc `{ "error": "..." }` shapes drift over time and force every client to handle each endpoint differently.

## Decision

All HTTP error responses from the Kartova API use **RFC 7807 `application/problem+json`**. Response body shape:

```json
{
  "type": "https://kartova.io/problems/<slug>",
  "title": "Short, human-readable summary",
  "status": 403,
  "detail": "Specific context about this instance",
  "instance": "/api/v1/organizations/me",
  "traceId": "00-<W3C-trace-id>-01"
}
```

- `type` — stable URL; documented per error class at `kartova.io/problems/<slug>` (a plain docs page per slug, no JSON schema endpoint required).
- `title` — stable per `type`, never includes request-specific data.
- `status` — HTTP status code.
- `detail` — human-readable, may include request-specific info (but no PII or sensitive data).
- `instance` — request path.
- `traceId` — W3C trace-context id from `Activity.Current`, for correlation with structured logs (ADR-0058).

Validation errors extend the shape with a standard `errors` field (RFC 7807 extension member convention):

```json
{
  "type": "https://kartova.io/problems/validation-failed",
  "title": "One or more validation errors occurred",
  "status": 400,
  "errors": {
    "name": ["is required", "must be under 100 characters"],
    "tags[2]": ["unknown tag"]
  },
  "traceId": "..."
}
```

Implementation: ASP.NET Core `ProblemDetailsService` + `AddProblemDetails()`, customized to always set `traceId` and resolve `type` URLs from a central registry.

## Rationale

- **Standards-based** — RFC 7807 is the only IETF standard for REST error bodies; adopted by GitHub, GitLab, Stripe-style extensions, ASP.NET Core (built-in since 2.1), and OpenAPI 3.1 natively.
- **OpenAPI friendly** — error schemas auto-derive for generated clients (ADR-0034).
- **Trace correlation** — `traceId` field pairs directly with structured logs (ADR-0058) for debugging across services.
- **Extensible** — the `errors` extension for validation is an established community pattern.
- **ASP.NET first-class** — `IProblemDetailsService` and `AddProblemDetails()` are built-in; no library to add.

## Alternatives Considered

- **Custom `{ "error": "msg", "code": "X" }` shape** — rejected; reinvents a standard with no upside and worse toolchain support.
- **JSON:API errors spec** — broader envelope with pointer-to-source semantics, but we're not JSON:API elsewhere and the envelope adds noise.
- **GraphQL-style error arrays** — we chose REST (ADR-0029); not applicable.
- **Plain text `4xx` bodies** — only viable for truly simple APIs; defeats programmatic handling.

## Consequences

**Positive**
- One error shape across SPA, CLI, Agent, and third-party clients.
- Generated OpenAPI clients deserialize errors correctly without custom code.
- Trace IDs visible to customers for support tickets.

**Negative / Trade-offs**
- Requires maintaining a small registry of `type` URLs + per-type docs pages (burden: low; one markdown file per error class).
- Slightly more verbose than ad-hoc error bodies.

**Neutral**
- Applies only to HTTP error responses; internal domain exceptions, Kafka message failures, and webhook delivery errors have their own (unrelated) shapes.

## Implementation Notes

- `services.AddProblemDetails(options => options.CustomizeProblemDetails = ctx => ctx.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id);`
- Central mapping: `Kartova.SharedKernel.AspNetCore/ProblemTypes.cs` — `const string TenantAccessDenied = "https://kartova.io/problems/tenant-access-denied";` etc.
- Docs pages at `docs/api/problems/<slug>.md` (future: published under `kartova.io/problems/<slug>`).
- Architecture test: controllers/endpoints must not throw `Ok(new { error = ... })` style bodies; all error paths must route through `Problem(...)` / exception filter.

## References

- RFC 7807: https://datatracker.ietf.org/doc/html/rfc7807
- ASP.NET Core ProblemDetails: https://learn.microsoft.com/aspnet/core/fundamentals/error-handling#problem-details
- ADR-0029 (REST), ADR-0034 (OpenAPI), ADR-0058 (structured logs)
