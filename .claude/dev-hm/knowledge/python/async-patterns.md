# Python async patterns

Design patterns for correct asyncio code: structured concurrency, cancellation, timeouts,
bridging sync and async worlds, and backpressure. The review hooks these patterns back
(gate anchors) live in `knowledge/python/review-checklist/concurrency-and-async.md#async-patterns`; test
mechanics in `knowledge/python/testing.md`; runtime diagnosis in
`knowledge/python/debugging.md`. Choosing asyncio versus threads or processes at all is
`knowledge/python/concurrency.md`.

## Structured concurrency with TaskGroup

`asyncio.TaskGroup` (3.11+) is the default way to run tasks concurrently. Its guarantees:
no task outlives the `async with` block; the first unhandled exception cancels every
sibling; all failures arrive together as an `ExceptionGroup` handled with `except*`.

```python
async def fetch_all(urls: list[str], client: httpx.AsyncClient) -> list[Result]:
    try:
        async with asyncio.TaskGroup() as tg:
            tasks = [tg.create_task(fetch_one(client, u)) for u in urls]
    except* httpx.HTTPError as eg:
        for err in eg.exceptions:
            log.warning("fetch failed: %s", err)
        raise
    return [t.result() for t in tasks]
```

- Create tasks through `tg.create_task`, not the module-level `asyncio.create_task`, so
  they are owned by the group.
- A bare `asyncio.create_task` result must be kept referenced (e.g. in a set with a
  done-callback that discards) — the loop holds only a weak reference and an unreferenced
  task can be garbage-collected mid-flight. Inside a TaskGroup this cannot happen.
- For a long-lived background task tied to an object's lifetime, own it explicitly: store
  the task, cancel and await it in the owner's `aclose()`.

Legacy — `asyncio.gather` only in pre-existing code too costly to restructure: it has no
scope, keeps running siblings after one fails (unless cancelled by hand), and with
`return_exceptions=True` buries failures in the result list, which callers routinely
forget to check. Migrate to TaskGroup when touching such code; do not start new code on
`gather`.

## Cancellation

Cancellation is cooperative and arrives as `asyncio.CancelledError` raised at an `await`
point. The contract for every coroutine:

- Clean up (release locks, close resources — usually via `finally` / `async with`) and let
  `CancelledError` propagate. Swallowing it breaks TaskGroup and timeout semantics; if you
  must catch it to clean up, re-raise.
- Cleanup code that itself awaits can be interrupted by a second cancellation; keep
  `finally` blocks short, and bound any awaited cleanup with its own `asyncio.timeout`.
- `task.cancel()` only requests cancellation — await the task afterwards to observe
  completion.
- If a library must absorb a cancellation it triggered internally (rare), pair
  `task.uncancel()` with careful bookkeeping; application code should never need it.

### Shielding

`asyncio.shield(op)` lets `op` continue when the awaiting task is cancelled. It is a
hazard by default: the shielded operation becomes an orphan the caller no longer waits
for, and a second cancel kills it anyway. Prefer designing operations to be resumable or
idempotent. Legitimate narrow use: a commit/ack that must complete once started —
shield it, keep a reference, and ensure something still awaits it on the non-cancelled
path.

## Timeouts

Use `asyncio.timeout()` (3.11+) as the default; it cancels whatever is inside the block
and converts the cancellation to `TimeoutError` at the boundary:

```python
async with asyncio.timeout(5.0):
    reply = await service.call(req)          # any await inside is bounded
```

- `asyncio.timeout_at(when)` for absolute deadlines; `Timeout.reschedule()` to extend.
- Deadline propagation: take one budget at the entry point and pass remaining time down,
  rather than stacking independent per-layer timeouts that can multiply.
- `asyncio.wait_for(op, t)` predates block timeouts; fine where it exists, but new code
  uses the context-manager form — it composes with multiple statements and TaskGroups.
- Network libraries still need their own timeouts (QUA-PY-006): an `httpx` call without
  `timeout=` inside an `asyncio.timeout` block wastes the whole budget on one peer.

## Bridging sync and async

| Situation | Tool |
|---|---|
| Program entry point | `asyncio.run(main())` — once, at the top; never inside a running loop |
| Blocking call inside async code | `await asyncio.to_thread(fn, *args)` |
| Blocking call needing a custom/shared pool | `loop.run_in_executor(pool, fn)` |
| CPU-bound work inside async code | Process pool via `run_in_executor(ProcessPoolExecutor(...), fn)` — `to_thread` does not help under the GIL (see `knowledge/python/concurrency.md`) |
| Submitting a coroutine from a foreign thread | `asyncio.run_coroutine_threadsafe(coro, loop)` → `concurrent.futures.Future` |
| One-shot callback from a foreign thread | `loop.call_soon_threadsafe(cb)` |

- Never call blocking I/O (`requests`, `time.sleep`, blocking DB drivers) directly in a
  coroutine — it stalls every task on the loop. asyncio debug mode flags callbacks over
  100 ms (`knowledge/python/debugging.md#asyncio-diagnosis`).
- `to_thread` propagates `contextvars` into the worker thread; raw executor calls do not
  (wrap with `contextvars.copy_context().run` if the callee needs them).
- A sync facade over an async library: keep one background loop in a dedicated thread and
  funnel calls through `run_coroutine_threadsafe`; do not spin up a new loop per call.
- The blocking boundary is contagious in both directions: pushing async one layer up a
  large sync codebase is a structural decision — when a codebase-wide sync-to-async
  migration is on the table, treat it per
  `knowledge/shared/three-framing-analysis.md` rather than converting incrementally
  without a decision record.

## Queues and backpressure

Unbounded buffering turns overload into memory exhaustion. Default to bounded queues so
producers slow down instead of the process growing:

```python
async def pipeline(source, sink, workers: int = 8) -> None:
    q: asyncio.Queue[Item] = asyncio.Queue(maxsize=100)   # bounded = backpressure

    async def produce() -> None:
        async for item in source:
            await q.put(item)                # blocks when full — backpressure
        q.shutdown()                         # 3.13+: wakes consumers, no sentinels

    async def consume() -> None:
        while True:
            try:
                item = await q.get()
            except asyncio.QueueShutDown:
                return
            try:
                await sink.write(item)
            finally:
                q.task_done()

    async with asyncio.TaskGroup() as tg:
        tg.create_task(produce())
        for _ in range(workers):
            tg.create_task(consume())
```

- `Queue.shutdown()` (3.13+) replaces sentinel objects and per-consumer `None` pushes;
  `shutdown(immediate=True)` also drops queued items.
- To cap concurrency without a queue, use `asyncio.Semaphore(n)` around the critical
  section — the standard pattern for "at most n in-flight requests".
- Rate limiting (n per second) is not the same as concurrency limiting; use a token-bucket
  or a library, not a bare semaphore.
- `asyncio.Queue` is not thread-safe. Between threads use `queue.Queue`; from a producer
  thread into a loop, use `call_soon_threadsafe(q.put_nowait, item)`.

## anyio

anyio implements Trio-style structured concurrency on both asyncio and trio (current line:
`knowledge/shared/versions.md`). httpx, Starlette, and FastAPI are built on it. Reach for
it when writing a library that must run on either backend, or when its primitives are
simply better shaped for the job:

- `anyio.create_task_group()` — like TaskGroup; cancellation is scope-based
  (`move_on_after`, `fail_after` return/raise on deadline, and `CancelScope` can be
  shielded or cancelled explicitly).
- Memory object streams (`anyio.create_memory_object_stream(max_buffer_size)`) are
  bounded channels with closable ends — backpressure plus clean end-of-stream semantics,
  often a better fit than `asyncio.Queue`.
- `anyio.to_thread.run_sync` / `anyio.from_thread.run` mirror the bridging table above,
  with a built-in capacity limiter for the thread pool.
- Test support: `anyio`'s pytest plugin runs the same async test on both backends;
  pytest-asyncio setup is in `knowledge/python/testing.md#async-tests`.

Do not mix anyio task groups and raw `asyncio.create_task` in the same layer — pick one
structure per component so cancellation flows through a single tree.

## Debugging hooks

Symptom-driven diagnosis lives in `knowledge/python/debugging.md#asyncio-diagnosis`. Two
current-line additions worth knowing: the asyncio CLI inspects a live process's task tree
from outside (`python -m asyncio ps <PID>` / `pstree <PID>`, 3.14+), and TaskGroup
failures arrive as `ExceptionGroup` — read the sub-exceptions, not just the group repr.
