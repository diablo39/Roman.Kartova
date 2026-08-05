# Node backend services: structure, shutdown, logging, streams, workers — CPU-bound work: worker threads

Section of `knowledge/typescript/node-backend.md`.


The event loop is shared by every in-flight request; a synchronous computation of tens of
milliseconds adds that latency to all of them (the review checklist's "blocking the event loop",
S1). Typical offenders: large `JSON.parse`/`stringify`, synchronous `zlib`/`crypto` calls, image
transforms, tight loops over big arrays.

1. First try to remove the work: smaller payloads, incremental processing, the async form of the
   same primitive (`zlib` async APIs, `crypto` async variants).
2. Persistent CPU-bound work goes to a worker pool — `piscina` over hand-rolled `worker_threads`
   management. Size the pool to the cores the container actually has; do not spawn a worker per
   request.

```ts
// transform.worker.ts — a pure function module
export default function transform(input: Uint8Array): Uint8Array { /* CPU-bound */ }

// caller
import Piscina from "piscina";
const pool = new Piscina({ filename: new URL("./transform.worker.js", import.meta.url).href });
const out = await pool.run(data, { transferList: [data.buffer] });
```

- Arguments and results are structured-cloned; transfer `ArrayBuffer`s for large binary data
  instead of copying. `SharedArrayBuffer` + `Atomics` is rarely warranted — reach for it only with
  a measured copy bottleneck.
- Once the workload grows past "a pool inside this service", the offload architecture (in-process
  pool vs a separate service vs a queue) is a consequential structural choice — run it per
  `knowledge/shared/three-framing-analysis.md`.
