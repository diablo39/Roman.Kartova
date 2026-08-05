# Node backend services: structure, shutdown, logging, streams, workers — Graceful shutdown

Section of `knowledge/typescript/node-backend.md`.


On `SIGTERM`/`SIGINT`: flip readiness to failing (the orchestrator stops routing), stop accepting
connections, let in-flight requests finish under a deadline, close resources in reverse
initialization order, then exit 0 — and force-exit non-zero if the deadline passes. `close-with-grace`
implements the signal handling and deadline:

```ts
// server.ts
import closeWithGrace from "close-with-grace";

const app = await buildApp(config);
await app.listen({ port: config.PORT, host: "0.0.0.0" });

closeWithGrace({ delay: config.SHUTDOWN_TIMEOUT_MS }, async ({ signal, err }) => {
  if (err) app.log.error({ err }, "shutting down on error");
  else app.log.info({ signal }, "shutting down");
  await app.close(); // runs onClose hooks LIFO: server stops, pools drain, clients close
});
```

- Register every resource's cleanup as an `onClose` hook in the plugin that created it (DB pool
  `end`, queue consumer unsubscribe, outbound client close). `app.close()` then releases them in
  reverse order, after the HTTP server drains.
- On a bare `node:http` server, `server.close()` stops new connections but keep-alive sockets
  linger — call `server.closeIdleConnections()` and, at the deadline, `closeAllConnections()`.
- Under Kubernetes, a readiness probe plus a short `preStop` sleep prevents the race where traffic
  is still being routed when `SIGTERM` lands.
- `unhandledRejection`/`uncaughtException` remain crash-only: log and exit non-zero (`platform.md`);
  the supervisor restarts a clean process. Do not attempt to keep serving from an unknown state.
