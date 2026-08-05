# .NET debugging and diagnostics

A workflow for diagnosing production-grade .NET issues: capture evidence with the diagnostic CLI
tools first, form a hypothesis from that evidence, then confirm. Reproduce before concluding; do not
guess from a stack trace alone. Tool versions are in `knowledge/shared/versions.md`.

## Diagnostic tool set

Install as global tools (`dotnet tool install -g dotnet-<name>`). They attach to a live process by
PID (`dotnet-<tool> ps` lists .NET processes) or run against a dump.

| Tool | Answers | Typical invocation |
|---|---|---|
| `dotnet-counters` | Is it CPU, memory, GC, thread-pool, exceptions? (first-level triage) | `dotnet-counters monitor -p <pid> --counters System.Runtime,Microsoft.AspNetCore.Hosting` |
| `dotnet-trace` | Where is time spent? which code paths are hot? | `dotnet-trace collect -p <pid> --profile cpu-sampling` |
| `dotnet-dump` | What is every thread doing? object/heap state at a moment | `dotnet-dump collect -p <pid>` then `dotnet-dump analyze <file>` |
| `dotnet-gcdump` | What is on the managed heap, by type and retention? | `dotnet-gcdump collect -p <pid>` |
| `dotnet-stack` | Managed call stacks of all threads, quickly | `dotnet-stack report -p <pid>` |

On the current LTS these tools also surface lightweight GC/JIT/assembly-load events, so counters and
traces carry more runtime detail. In containers, install the tools in the image or use a sidecar
sharing the process namespace, and collect to a mounted volume.

## Triage order

1. `dotnet-counters monitor` for 30–60s under load. Read the symptom class: CPU-bound (high
   cpu-usage, low GC), allocation/GC pressure (high alloc rate, gen-2/LOH growth, high `% Time in GC`),
   thread-pool starvation (queue length climbing, thread count rising), or exception storm.
2. Branch on the symptom to the matching section below.
3. Capture the artifact (trace, dump, or gcdump) while the symptom is live — post-mortem on a healthy
   process tells you nothing.

## Exception analysis

- Read the innermost exception and its stack first; for `AggregateException` and wrapped exceptions,
  walk `InnerException`. Preserve stacks on rethrow with `throw;` (bare) or
  `ExceptionDispatchInfo.Capture(ex).Throw()`, never `throw ex;` which resets the stack.
- First-chance vs unhandled: enable first-chance exception tracing when an exception is swallowed
  somewhere — `dotnet-trace` with the exception provider, or a dump on the throw.
- In a dump: `dotnet-dump analyze`, then `clrstack -a` on the faulting thread and `pe` (print
  exception) to see the exception object and its stack. `dumpheap -type Exception` finds exception
  objects on the heap.
- `TaskCanceledException`/`OperationCanceledException` from a timeout usually means a downstream call
  exceeded its deadline — correlate with `dotnet-counters` request timing, not with the cancellation
  site.

## Async hangs and deadlocks {#async-hangs}

Symptoms: requests stop completing, thread count climbs, `dotnet-counters` shows thread-pool queue
length growing while CPU is low.

- Classic sync-over-async deadlock: `.Result`/`.Wait()`/`GetAwaiter().GetResult()` on a `Task` that
  needs the captured context. Find it with a dump: threads blocked in `Monitor.Wait`/`WaitOne` sitting
  under a `Task` await. Fix is to make the path async end to end, not to add `ConfigureAwait` on top.
- Thread-pool starvation: blocking calls occupy pool threads faster than the pool grows, so ready
  continuations never run. In a dump, `threadpool` shows a long work queue and all worker threads
  blocked. Root cause is blocking I/O or a lock held across an await — remove the blocking, do not
  raise `ThreadPool.SetMinThreads` as a fix.
- Deadlock on locks: `dotnet-dump` → `clrstack` across threads reveals a cycle (A holds lock 1 waiting
  lock 2, B the reverse). `syncblk` lists held sync blocks and owning threads.
- Missing `CancellationToken` propagation lets work run past its deadline and pile up; confirm tokens
  are forwarded (see `knowledge/csharp/platform.md`).

## Memory leaks and GC pressure {#memory}

Symptoms: working set grows unbounded, gen-2/LOH rising in `dotnet-counters`, eventual OOM.

1. Take two `dotnet-gcdump` snapshots minutes apart under steady load. Diff them (open both in
   Visual Studio or PerfView) to find the type whose instance count grows.
2. Inspect retention paths — what roots the growing objects. Common roots:
   - `static` collections/caches with no eviction.
   - Event handlers / `event` subscriptions never unsubscribed (publisher outlives subscriber).
   - `IDisposable` not disposed (`HttpClient` created per call, undisposed streams/connections),
     or a `DbContext`/connection captured beyond its scope.
   - Captured closures holding large graphs; timers/`CancellationTokenRegistration` not disposed.
3. Large-object-heap growth points at large arrays/strings/buffers — pool them (`ArrayPool<T>`,
   `RecyclableMemoryStream`) or stream instead of buffering.

For allocation churn (frequent gen-0 GC, not a leak): `dotnet-trace` with allocation-tick or the GC
provider identifies the hottest allocating call sites. Reduce with `Span`/`Memory`, pooling, and by
removing string interpolation in logging (see `knowledge/csharp/source-generators.md#logging`).

## CPU and latency profiling

- `dotnet-trace collect --profile cpu-sampling` under load, then open the trace in Visual Studio,
  PerfView, or SpeedScope. Read the hot path top-down; look for unexpected time in serialization,
  reflection, regex compilation, or logging — each has a source-generator fix.
- For request-level latency, correlate `dotnet-counters` ASP.NET Core hosting counters
  (requests/sec, request queue length) with the trace.
- Regressions: capture a trace on the good and bad builds and compare hot paths, rather than reading
  the diff.

## Dump analysis reference {#dumps}

`dotnet-dump analyze <dump>` opens an SOS session. Frequent commands:

| Command | Shows |
|---|---|
| `clrthreads` | All managed threads and their state |
| `clrstack -a` | Managed stack with arguments/locals for the current thread |
| `parallelstacks` / `pstacks` | Threads grouped by shared stack — spots contention fast |
| `dumpheap -stat` | Heap object counts and sizes by type |
| `dumpheap -type <T>` | Instances of a type; `gcroot <addr>` traces what roots one |
| `pe` | Print the exception on the current thread |
| `syncblk` | Held sync blocks and owning threads (lock analysis) |
| `dumpasync` | Pending async state machines — the async equivalent of a thread dump |

`dumpasync` is the key command for a hung async service: it reconstructs the await chains so you can
see which continuations are stuck and on what.

## Source generators

To debug generated code and generator execution (emitting generated files, attaching to the
compiler), see `knowledge/csharp/source-generators.md#debugging`.

## Handoff

Report the symptom class, the artifact that proved it (trace/dump/gcdump), the root cause with a
`file:line` where it lives, and the minimal fix. Attach the decisive command output, not the whole
dump session.
