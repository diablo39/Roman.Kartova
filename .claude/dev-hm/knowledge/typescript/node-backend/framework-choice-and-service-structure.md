# Node backend services: structure, shutdown, logging, streams, workers — Framework choice and service structure

Section of `knowledge/typescript/node-backend.md`.


For a new service, Fastify is the default: per-route schemas make boundary validation (SEC-TS-005)
the path of least resistance, structured pino logging is built in, and plugin encapsulation scopes
each module's decorations and hooks. Express stays where a codebase already uses it — do not
rewrite a working service — but there you wire schema validation and pino (`pino-http`) explicitly,
because Express ships neither.

Keep domain logic out of the framework:

```
src/
├── app.ts        # buildApp(): registers plugins + routes, returns the instance (testable)
├── server.ts     # entry point: config, buildApp, listen, shutdown wiring
├── config.ts     # env schema — the only file that reads process.env
├── plugins/      # infrastructure: db pool, auth, outbound clients (with onClose hooks)
├── routes/       # thin handlers: parse → call domain → map result to a status
└── domain/       # pure functions and services; no req/res types in their signatures
```

- Handlers stay thin: schema-validated input in, one domain call, response mapping out. Domain
  functions take plain typed arguments so they test without HTTP.
- Separate `buildApp()` from `listen()` so tests drive the app in-process (Fastify `app.inject`,
  Supertest) without opening a port.
- Wire dependencies explicitly — pass the pool/client into route registration or decorate the
  instance in the plugin that owns it. Module-level singletons make tests share state.

```ts
// routes/users.ts — validation at the boundary, domain call behind it
import type { FastifyInstance } from "fastify";
import { z } from "zod";

const CreateUser = z.object({ email: z.email(), name: z.string().min(1).max(200) });

export function userRoutes(app: FastifyInstance) {
  app.post("/users", async (req, reply) => {
    const parsed = CreateUser.safeParse(req.body);
    if (!parsed.success) return reply.code(400).send({ issues: parsed.error.issues });
    const user = await app.userService.create(parsed.data);
    return reply.code(201).send(user);
  });
}
```

A Zod type provider can move that parse into Fastify's route `schema` so the handler receives typed
`req.body` directly; either way the schema runs before any handler logic (SEC-TS-005).
