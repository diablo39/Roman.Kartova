# Modern .NET platform patterns — API surface: minimal APIs vs controllers

Section of `knowledge/csharp/platform.md`.


Both are first-class and can coexist in one app. Choose by shape, not fashion.

| Choose | When |
|---|---|
| Minimal APIs | Focused services, microservices, gateways, serverless, AOT targets; few endpoints; lowest startup and request overhead; native OpenAPI |
| Controllers (MVC) | Large APIs, many endpoints, several contributors, convention-based filters/model binding, heavy content negotiation |

Minimal API guidance: group with `MapGroup`, share cross-cutting concerns via endpoint filters,
return `TypedResults` (typed, testable, OpenAPI-accurate), inject services as parameters. Request
validation uses a source-generator-based validator (reflection-free, AOT-friendly) — annotate DTOs
with data-annotation attributes and enable minimal-API validation. Document with the built-in
OpenAPI package (`Microsoft.AspNetCore.OpenApi`); it emits OpenAPI 3.1 including YAML.

Keep endpoint delegates thin: parse/validate, delegate to an injected service, map the result.
Business logic lives in services, not in the endpoint or controller.
