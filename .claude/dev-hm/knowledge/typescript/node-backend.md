# Node backend services: structure, shutdown, logging, streams, workers

Service-level patterns for production Node backends. Platform basics (ESM, `node:` built-ins,
`AbortSignal`, process-level rejection handlers) are in `platform.md`; boundary validation, rate
limiting, and header hardening are in `security.md`; testing handlers through the app is in
`testing.md`. Pinned versions live in `knowledge/shared/versions.md` — check it before adding a
dependency.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| Framework choice and service structure | `knowledge/typescript/node-backend/framework-choice-and-service-structure.md` |
| Configuration from the environment | `knowledge/typescript/node-backend/configuration-from-the-environment.md` |
| Logging with pino | `knowledge/typescript/node-backend/logging-with-pino.md` |
| Graceful shutdown | `knowledge/typescript/node-backend/graceful-shutdown.md` |
| Streams and backpressure | `knowledge/typescript/node-backend/streams-and-backpressure.md` |
| CPU-bound work: worker threads | `knowledge/typescript/node-backend/cpu-bound-work-worker-threads.md` |
| Concurrency limiting and outbound calls | `knowledge/typescript/node-backend/concurrency-limiting-and-outbound-calls.md` |
