# API contracts: OpenAPI, tRPC, Problem Details, end-to-end typing

How a TypeScript service and its clients agree on request/response shapes without hand-maintained
parallel types. Boundary validation (the runtime control) is in `security.md#validation`; this file
covers the contract artifact and the typed plumbing around it. Tool generations are pinned in
`knowledge/shared/versions.md`.

## Choosing the contract style

| Situation | Contract |
|---|---|
| Any consumer outside this repo — other teams, other languages, partners, public | OpenAPI document |
| All consumers are TypeScript in the same repo (SPA + BFF, internal tools) | tRPC (or an RSC app's server actions — `rsc-and-server-actions.md`) |
| Fastify service that wants a spec without contract-first ceremony | route schemas → generated OpenAPI (code-first) |

The safe default when in doubt is OpenAPI: it never locks a consumer out, and a tRPC layer can sit
behind it for internal callers. Committing a product API to a contract style is a one-way door once
external consumers integrate — analyze it per `knowledge/shared/three-framing-analysis.md`.
Whichever style wins, the runtime rule is unchanged: the server schema-validates every request
(SEC-TS-005); types alone check nothing at runtime.

## OpenAPI with generated types

Default toolchain: `openapi-typescript` generates a types-only module from the spec (zero runtime),
and `openapi-fetch` wraps `fetch` with those types (tiny runtime, no codegen'd classes):

```ts
// pnpm openapi-typescript ./openapi.yaml -o src/api.gen.ts   (CI regenerates; diff = contract change)
import createClient from "openapi-fetch";
import type { paths } from "./api.gen";

const client = createClient<paths>({ baseUrl: config.API_URL });

const { data, error } = await client.GET("/users/{id}", { params: { path: { id } } });
if (error) return renderProblem(error);   // typed from the spec's error responses
return renderUser(data);                  // typed as the 2xx schema
```

- Paths, params, bodies, and each status code's shape all come from the spec; a route rename or
  field removal is a compile error in every client.
- Heavier generators (Hey API, orval — which emits TanStack Query hooks) are fine alternatives;
  pick one generator per repo.
- Code-first services: Fastify route schemas feed `@fastify/swagger`, so the spec falls out of the
  same schemas that validate requests — one source of truth. Publish the generated spec as a build
  artifact.
- Gate contract changes in CI with a spec diff (an `oasdiff`-class tool) that fails on breaking
  changes to released versions; additive evolution is the default.
- Generated types are compile-time only. A response from an upstream you don't operate still gets
  schema-validated at runtime (`security.md#validation`, SEC-TS-006) — the spec promises a shape,
  the network doesn't.

## tRPC for internal TypeScript-to-TypeScript

tRPC infers the client types directly from the server router — no spec, no codegen step, immediate
end-to-end types. Input validation is built into the procedure definition, which satisfies
SEC-TS-005 by construction:

```ts
import { initTRPC, TRPCError } from "@trpc/server";
import { z } from "zod";

const t = initTRPC.context<Context>().create();

export const appRouter = t.router({
  projectRename: t.procedure
    .input(z.object({ projectId: z.uuid(), name: z.string().min(1).max(120) }))
    .mutation(async ({ input, ctx }) => {
      if (!ctx.session) throw new TRPCError({ code: "UNAUTHORIZED" });
      await assertCanEdit(ctx.session, input.projectId);   // authz per object, in the procedure
      return ctx.projects.rename(input);
    }),
});
export type AppRouter = typeof appRouter;                  // the client imports only this type
```

- The client (`@trpc/client` + the TanStack Query integration) gets fully typed procedures; a
  server change breaks callers at compile time.
- Scope: internal, same-repo APIs. The moment a non-TypeScript or external consumer appears, that
  surface needs an OpenAPI contract — author one for that slice (or bridge the subset via the tRPC
  OpenAPI tooling) rather than teaching an external team RPC internals.
- Authorization lives in procedures/middleware exactly as in server actions: every procedure
  re-checks; context establishes identity, procedures decide access.

## Error contract: Problem Details (RFC 9457)

Errors are part of the contract. Use Problem Details (`application/problem+json`, RFC 9457 — the
successor to RFC 7807) instead of ad-hoc `{ error: string }` shapes:

```json
{
  "type": "https://api.example.com/problems/validation",
  "title": "Request validation failed",
  "status": 400,
  "detail": "email must be a valid address",
  "instance": "/users",
  "errors": [{ "path": "email", "message": "invalid address" }]
}
```

- `type` is a stable URI identifier per problem class — clients switch on it, not on prose.
  `title` is the human summary; `detail` is occurrence-specific; extension members (like `errors`
  above for field issues) carry structure.
- Map internal failures to problems at the edge in one error handler. `detail` never carries stack
  traces, SQL, or internal identifiers — that is information disclosure; log the internals with the
  request id (`node-backend.md`) and return the id in the problem for correlation.
- Declare problem responses in the OpenAPI spec per status so generated clients type them.

## End-to-end response typing

- One source of truth per API — a schema (Zod → server validation, inferred types, generated spec)
  or a spec (→ generated types both sides). Hand-written interfaces that mirror another artifact
  drift; delete them.
- Model failures as data on the client: a discriminated union
  (`{ ok: true; data } | { ok: false; problem }`) at the API-client layer, so every call site
  handles the error arm to reach the data (the same shape server actions return —
  `rsc-and-server-actions.md`).
- Never `as`-cast a response body (SEC-TS-006). Generated types apply where the contract is
  enforced end to end; runtime parsing applies where it is not.
- Version deliberately: additive changes freely; breaking changes behind a new version/path with a
  deprecation window, announced in the spec.
