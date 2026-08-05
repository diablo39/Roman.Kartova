# Python concurrency: choosing and using an execution model

How to pick between threads, processes, asyncio, subinterpreters, and the free-threaded
build, and how to use each safely. asyncio coding patterns are in
`knowledge/python/async-patterns.md`; runtime diagnosis in
`knowledge/python/debugging.md`. Interpreter lines and the free-threading support status
are anchored in `knowledge/shared/versions.md`.

## Decision guide

| Workload | Default choice | Notes |
|---|---|---|
| Many concurrent I/O waits (sockets, HTTP, queues) | asyncio | Thousands of in-flight operations on one thread; requires async-capable libraries end to end |
| Moderate I/O concurrency, blocking libraries | Threads (`ThreadPoolExecutor`) | GIL releases during I/O; no library rewrite needed |
| CPU-bound work | Processes (`ProcessPoolExecutor`) | Sidesteps the GIL; pay serialization cost at the boundary |
| CPU-bound, heavy shared read-mostly data | Free-threaded build (measured) or subinterpreters | See the sections below; both newer, verify ecosystem support first |
| Mixed: async service with CPU spikes | asyncio + process pool via `run_in_executor` | Keep the loop responsive; bridge per `async-patterns.md` |
| NumPy/Polars/PyTorch-style array math | Threads or the library's own parallelism | Native code releases the GIL; extra processes often just copy data |

Two rules of thumb: the GIL only bites CPU-bound pure-Python code — I/O and native-code
workloads parallelize fine on threads; and serialization dominates process-based designs —
if the task is short or its data large, the pool can be slower than a loop.

Picking the concurrency model for a whole service — or migrating one (sync to async,
threads to processes, adopting free-threading) — is a structural one-way door: it dictates
library choices, testing strategy, and failure modes. Run it as a
`knowledge/shared/three-framing-analysis.md` decision, not an incidental choice inside one
work package.

## Threads

`concurrent.futures.ThreadPoolExecutor` is the default API; raw `threading.Thread` only
for long-lived dedicated workers.

```python
with ThreadPoolExecutor(max_workers=16) as pool:
    futures = {pool.submit(fetch, u): u for u in urls}
    for fut in as_completed(futures):
        handle(futures[fut], fut.result())    # .result() re-raises worker exceptions
```

- The GIL never made compound operations atomic: check-then-act, read-modify-write
  (`d[k] += 1`), and cross-structure invariants race on any build. Guard shared mutable
  state with `threading.Lock`, or better, share nothing and pass messages via
  `queue.Queue` (bounded, for backpressure).
- Acquire locks with `with lock:`; establish a global lock order to avoid deadlocks;
  prefer one lock per invariant over many fine-grained locks you must compose.
- Mark background threads that may be abandoned at exit as `daemon=True`, and shut pools
  down deliberately (`pool.shutdown(cancel_futures=True)`) on service stop.
- `functools.cache` and module import are thread-safe; most other stdlib mutables are not
  guaranteed for compound use.

## Processes and multiprocessing

Process workers sidestep the GIL for CPU-bound work. Default API:
`concurrent.futures.ProcessPoolExecutor`; drop to `multiprocessing` for shared memory,
managers, or custom topologies.

### Start methods

| Method | How the child starts | Properties |
|---|---|---|
| `spawn` | Fresh interpreter, imports your module, receives pickled state | Safe with threads; slowest start; default on Windows and macOS |
| `forkserver` | Clean single-threaded server process forks children on demand | Safe with threads, near-fork speed; default on Linux/POSIX on the current line (3.14+) |
| `fork` | `os.fork()` of the parent, inheriting everything | Fast but unsafe the moment the parent has threads — locks and connections copy in undefined states |

The current line changed the Linux default from `fork` to `forkserver` (3.14). Practical
consequences, which also make code portable to macOS/Windows:

- Everything submitted (functions, args, initializer state) must be picklable; lambdas
  and closures are not — use top-level functions.
- Children re-import the main module: guard entry points with
  `if __name__ == "__main__":` and keep module import free of side effects.
- Module-level state mutated after startup is no longer silently inherited — pass state
  explicitly, or rebuild it per worker via `initializer=`/`initargs=`.

Safe by default: leave the platform default (`spawn`/`forkserver`) and write
pickle-clean, import-clean worker code. Legacy — `fork` only where it already underpins
pre-existing code that relies on inherited state, the parent is verifiably
single-threaded at fork time, and a migration path (explicit state passing) is recorded;
request it locally via `multiprocessing.get_context("fork")` rather than flipping the
global default.

### Moving data

- Prefer message passing: pool results, `multiprocessing.Queue`, `Pipe`.
- Large arrays: `multiprocessing.shared_memory` (or let NumPy/Arrow-backed libraries
  handle it) instead of pickling gigabytes per task.
- `Manager()` proxies are convenient and slow — fine for coordination flags, wrong for
  hot-path data.
- Batch small tasks (`chunksize=` in `Executor.map`) so serialization does not dominate.

## Subinterpreters (3.14+)

`concurrent.interpreters` runs multiple isolated interpreters in one process, each with
its own GIL; `concurrent.futures.InterpreterPoolExecutor` gives the familiar pool API.
Compared to processes: cheaper communication and startup, one OS process to operate.
Compared to threads: true CPU parallelism with enforced isolation — sharing is opt-in and
message-based. Constraints: objects cross the boundary by copy (pickle or shareable
types), and C extensions must support multi-phase init to load in subinterpreters. Treat
it as a candidate where you would otherwise reach for a process pool but the
serialization or memory overhead hurts; verify your dependency stack loads in a
subinterpreter before committing.

## Free-threaded build

The free-threaded interpreter (the `t`-suffixed build; support status per
`knowledge/shared/versions.md`) removes the GIL: threads run Python code in parallel.
Adopt it deliberately, not by default:

- Enable only when a measured workload shows threads blocked on the GIL and the full
  dependency set ships free-threaded wheels (`cp3XXt` tags) or is pure Python.
- Single-threaded code pays a small overhead versus the default build; benchmark both.
- Races that the GIL's coarse scheduling happened to hide become real. The discipline from
  the Threads section stops being best practice and becomes correctness-critical: audit
  every piece of shared mutable state, prefer immutable messages, and validate
  C-extension thread safety.
- Builtin containers stay memory-safe under concurrent access, but compound operations
  still race — locks remain required for invariants.

Debug-side notes live in `knowledge/python/debugging.md#gil-and-free-threading`.

## Coordination checklist (any model)

- Bound every queue and pool; unbounded buffering converts overload into memory
  exhaustion (`async-patterns.md#queues-and-backpressure` — the same logic applies to
  threads and processes).
- Propagate shutdown: a worker loop needs a stop signal (event, sentinel, queue
  shutdown), a join with timeout, and an escalation path when it does not stop.
- Timeouts on every blocking join/get/acquire in production paths; a missing timeout is a
  hang, not a slow call.
- Exceptions in workers surface only where you look for them: always consume
  `Future.result()`; log or re-raise from worker loops.
- Test concurrency claims: a stress test with the sanitizing settings you have (e.g.
  `PYTHONASYNCIODEBUG`, pytest-xdist to shake out shared state) beats an argument that
  the code "should be" safe.
