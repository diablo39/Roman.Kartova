# Node backend services: structure, shutdown, logging, streams, workers — Concurrency limiting and outbound calls

Section of `knowledge/typescript/node-backend.md`.


Unbounded fan-out is a self-inflicted flood: `Promise.all` over ten thousand items opens ten
thousand sockets. Bound every fan-out and every wait.

```ts
import pLimit from "p-limit";

const limit = pLimit(5); // at most 5 in flight against this dependency
const users = await Promise.all(ids.map((id) => limit(() => fetchUser(id))));
```

- Every outbound `fetch` carries a timeout: `AbortSignal.timeout(ms)`, composed with the inbound
  request's abort via `AbortSignal.any([...])` so client disconnects cancel downstream work.
- Retry only idempotent operations, with bounded attempts, exponential backoff, and jitter — and an
  overall deadline so retries cannot outlive the caller.
- Reuse connections: the built-in `fetch` pools via its undici agent; configure per-dependency
  limits there rather than opening ad-hoc clients per request. A per-dependency concurrency bound
  (bulkhead) keeps one slow upstream from consuming every worker.
- Inbound rate limiting and body-size caps are security controls — `security.md#node`.
