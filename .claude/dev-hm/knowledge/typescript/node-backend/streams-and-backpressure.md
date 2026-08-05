# Node backend services: structure, shutdown, logging, streams, workers — Streams and backpressure

Section of `knowledge/typescript/node-backend.md`.


Any payload that can be large — uploads, exports, proxied bodies — moves as a stream, never a fully
buffered value. Cap body sizes at the edge (`security.md#node`); upload handling rules are in
`security.md#file-upload`.

```ts
import { pipeline } from "node:stream/promises";
import { createReadStream } from "node:fs";
import { createGzip } from "node:zlib";

await pipeline(createReadStream(path), createGzip(), reply.raw);
```

- `pipeline` over `.pipe()` chains: it propagates errors across stages, destroys every stream on
  failure, and honours backpressure end to end. A bare `.pipe()` chain leaks the source when the
  destination errors.
- Backpressure is the contract: a `write()` returning `false` means pause until `'drain'`.
  `pipeline` and `for await (const chunk of readable)` handle this; hand-rolled write loops must.
- Web Streams interop: `Readable.toWeb`/`Readable.fromWeb` bridge Node streams and the `fetch`
  body/`ReadableStream` world; pick per boundary rather than converting repeatedly.
- Stream DB exports and report generation row by row (cursor/async iterator) instead of
  `SELECT *` into an array; tune `highWaterMark` only after measuring.
