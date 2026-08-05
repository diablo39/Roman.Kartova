# Node backend services: structure, shutdown, logging, streams, workers — Configuration from the environment

Section of `knowledge/typescript/node-backend.md`.


One module reads `process.env`, validates it through a schema at startup, and exports a frozen,
typed config. Everything else imports the config. A missing or malformed variable then fails the
boot with a named error instead of surfacing as `undefined` deep in a request. Secrets rules are in
`security.md#secrets`.

```ts
// config.ts — the only module that reads process.env
import { z } from "zod";

const Env = z.object({
  NODE_ENV: z.enum(["development", "test", "production"]),
  PORT: z.coerce.number().int().default(3000),
  DATABASE_URL: z.url(),
  LOG_LEVEL: z.enum(["debug", "info", "warn", "error"]).default("info"),
  SHUTDOWN_TIMEOUT_MS: z.coerce.number().int().default(10_000),
});

const parsed = Env.safeParse(process.env);
if (!parsed.success) {
  console.error("Invalid environment:", z.treeifyError(parsed.error));
  process.exit(1);
}
export const config = Object.freeze(parsed.data);
```

`@fastify/env` (JSON-schema based) is an equivalent alternative inside a Fastify plugin tree; use
one approach per service, not both.
