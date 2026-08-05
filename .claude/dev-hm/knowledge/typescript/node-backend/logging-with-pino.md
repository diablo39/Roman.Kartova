# Node backend services: structure, shutdown, logging, streams, workers — Logging with pino

Section of `knowledge/typescript/node-backend.md`.


Structured JSON to stdout; the platform (container runtime, collector) ships it. Never
`console.log` in request paths — it is synchronous, unstructured, and unleveled.

```ts
import crypto from "node:crypto";

const app = fastify({
  logger: {
    level: config.LOG_LEVEL,
    redact: ["req.headers.authorization", "req.headers.cookie", "*.password", "*.token"],
  },
  genReqId: () => crypto.randomUUID(),
});

app.log.info({ event: "startup", port: config.PORT }, "listening");
// inside a handler: req.log is a child logger carrying the request id
req.log.info({ event: "user_created", userId: user.id });
```

- Log events with stable names and structured fields, not interpolated prose — queries and alerts
  key on fields. The OWASP logging vocabulary row in `knowledge/shared/versions.md` names the
  standard event vocabulary for security events.
- Pass errors as `{ err }` so pino serializes message and stack; never log token/secret values or
  full request bodies (`security.md`) — the `redact` list is a backstop, not the control.
- `pino-pretty` is a dev-only transport; production stays raw JSON. Pino transports run in a worker
  thread, keeping formatting and shipping off the event loop.
- In Express, `pino-http` provides the same request-scoped child logger.
