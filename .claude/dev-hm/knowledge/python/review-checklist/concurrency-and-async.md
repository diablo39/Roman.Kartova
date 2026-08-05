# Concurrency and async

### Async patterns
Await every coroutine — an un-awaited coroutine is a silent no-op (Python warns "coroutine
was never awaited"). Never run blocking I/O or CPU work inside an async function; offload
with `asyncio.to_thread(...)` or a run-in-executor call. Use `async with`/`async for` for
async resources and `AsyncMock` in tests. Prefer `asyncio.TaskGroup` (3.11+) over bare
`asyncio.gather` for structured concurrency and reliable cancellation; hold references to
`create_task` results so they are not garbage-collected mid-flight. Set timeouts with
`asyncio.timeout(...)`. Handle `CancelledError` by cleaning up and re-raising, not
swallowing. Pattern depth (shielding, bridging, queues/backpressure, anyio) in
`knowledge/python/async-patterns.md`; choosing threads/processes/asyncio at all in
`knowledge/python/concurrency.md`.
