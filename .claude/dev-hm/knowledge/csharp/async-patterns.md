# C# async patterns

Depth behind `knowledge/csharp/platform.md`'s async baseline (async all the way down, tokens
propagated, no `async void`): the composition patterns and consumption rules that correct services
are built from. Diagnosing an async failure at runtime (hangs, starvation, deadlocks) is
`knowledge/csharp/debugging.md#async-hangs`.

## ValueTask consumption rules {#valuetask}

`ValueTask`/`ValueTask<T>` avoid a `Task` allocation when a result is often available
synchronously (cache hits, buffered reads). The saving comes with a strict consumption contract —
the instance may wrap a pooled `IValueTaskSource` that is recycled after first use:

- Await it **exactly once**, and do nothing else with it. Not twice, not concurrently, not stored
  for later re-await.
- No `.Result`/`.GetAwaiter().GetResult()` unless `IsCompletedSuccessfully` is already true.
- Need `WhenAll`, multiple awaits, or storage in a collection? Convert immediately with
  `.AsTask()` and pay the allocation — correctness beats the saved bytes.
- Violation symptoms are ugly: wrong results or `InvalidOperationException` from a recycled source,
  intermittently.

Authoring guidance (safe by default): public APIs return `Task`/`Task<T>` unless a measured hot
path shows the allocation matters and callers are under your control. Returning `ValueTask`
exports the consumption contract to every caller; do it deliberately, where the win is real
(per-item I/O, parser loops), not as a house style.

## Composing concurrent work {#composition}

### WhenAll and error aggregation {#whenall}

`await Task.WhenAll(...)` rethrows only the **first** exception; the rest are silently attached to
the task. When each branch can fail meaningfully, keep the task and read the full set:

```csharp
var all = Task.WhenAll(tasks);
try { await all; }
catch
{
    // all.Exception is the AggregateException carrying every failure, not just the first
    foreach (var ex in all.Exception!.InnerExceptions) logger.LogImportFailure(ex);
    throw;
}
```

Alternatives by shape: `Task.WhenEach` streams tasks as they complete (process results/failures
individually without `WhenAny` loops); `Parallel.ForEachAsync` for a bounded-parallelism loop over
a collection (below). Never fan out with unawaited tasks inside a `foreach` and forget the join —
that is fire-and-forget with extra steps.

### Bounded parallelism {#bounded-parallelism}

`Task.WhenAll(items.Select(DoAsync))` starts *everything at once* — a thousand items means a
thousand concurrent database calls. Bound the fan-out:

```csharp
await Parallel.ForEachAsync(items,
    new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct },
    async (item, token) => await ProcessAsync(item, token));
```

`SemaphoreSlim(n)` gating inside the selector achieves the same where you need the result list;
`Parallel.ForEachAsync` is simpler when you do not. Choose the bound from the downstream
resource's capacity, not the CPU count.

## Cancellation composition {#cancellation}

- Combine caller cancellation with a local timeout via a linked source, and dispose it:

```csharp
using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
cts.CancelAfter(TimeSpan.FromSeconds(5));
await client.SendAsync(request, cts.Token);
```

- Distinguish "caller cancelled" from "we timed out" by checking which token fired:
  `catch (OperationCanceledException) when (ct.IsCancellationRequested)` is the caller's
  cancellation; otherwise it was the local deadline — usually a timeout error, not a silent stop.
- One-off waits on tasks you cannot pass a token into: `task.WaitAsync(timeout, ct)`.
- Long-running loops check `ct.ThrowIfCancellationRequested()` at iteration boundaries; CPU-bound
  work does not observe tokens by magic.
- Prefer `cts.CancelAsync()` in async code paths; synchronous `Cancel` runs registered callbacks
  inline.
- Cancellation is cooperative cleanup, not rollback — leave state consistent on the way out
  (`finally`/`await using`), and never swallow `OperationCanceledException` to "keep going".

## Channels: in-process producer/consumer {#channels}

`System.Threading.Channels` is the standard in-process queue between producers and consumers.
Default to a **bounded** channel — an unbounded channel converts backpressure into unbounded
memory growth and hides overload until the process dies:

```csharp
var channel = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(capacity: 512)
{
    SingleReader = true,                       // enables faster implementation when true
    FullMode = BoundedChannelFullMode.Wait,    // producer awaits — backpressure propagates
});

// producer                                    // consumer
await channel.Writer.WriteAsync(item, ct);     await foreach (var item in
channel.Writer.Complete();                     //     channel.Reader.ReadAllAsync(ct)) { ... }
```

- `FullMode` is the overload policy: `Wait` propagates backpressure upstream (default choice);
  `DropOldest`/`DropWrite` shed load where staleness is acceptable (telemetry, samples) — dropping
  user work needs an explicit decision and a metric.
- Complete the writer (`Complete`/`TryComplete(ex)`) so consumers terminate; a consumer looping on
  `ReadAllAsync` over a never-completed channel is a leak.
- Set `SingleReader`/`SingleWriter` truthfully; they are correctness contracts as well as
  optimizations.
- Cross-process or durable queues are a different tool (a real message broker); channels are for
  intra-process pipelines.

## IAsyncEnumerable and streaming {#iasyncenumerable}

`IAsyncEnumerable<T>` is pull-based: the producer runs only when the consumer awaits the next item,
so backpressure is inherent — the slow consumer paces the producer for free.

- Producers: `[EnumeratorCancellation]` on the token parameter so `WithCancellation(ct)` flows in;
  yield inside the loop, never buffer the full sequence first.
- Consumers: `await foreach (var x in source.WithCancellation(ct))`; materializing with a
  `ToListAsync`-style helper forfeits streaming — only for known-small sequences.
- ASP.NET Core streams `IAsyncEnumerable<T>` responses without buffering; EF Core exposes
  `AsAsyncEnumerable()` for row-at-a-time processing of large results.
- Decouple a bursty producer from a slow consumer (or fan out to several consumers) by bridging
  through a bounded channel — the channel adds the buffer and the explicit overload policy that
  pure pull lacks.

## Background work and fire-and-forget {#background-work}

An unawaited task's exception vanishes (observed only by `TaskScheduler.UnobservedTaskException`,
long after the fact). "Fire and forget" is almost always a design smell; the safe shapes:

- Work that must survive the request: enqueue to a bounded channel consumed by a
  `BackgroundService` (the hosted service owns lifetime, retries, and logging), or use a durable
  queue when it must survive the process.
- `BackgroundService.ExecuteAsync` gets the host's stopping token; honor it, and catch-log-continue
  around the loop body so one poison item does not kill the service silently.
- Where a genuinely detached task is justified (rare), route it through one helper that logs
  faults: `_ = SafeFireAndForget(DoAsync(), logger)` — never a bare discarded call.
- Periodic work: `PeriodicTimer` in a hosted service, injected `TimeProvider` for testability —
  not `Task.Delay` loops scattered in services, not `System.Timers.Timer` callbacks (which are
  `async void` in disguise).

## Synchronization in async code {#synchronization}

- `lock` cannot contain `await` (compiler error for a reason — the continuation may resume on
  another thread). Async mutual exclusion is `SemaphoreSlim(1,1)` with `WaitAsync`/`Release` in
  try/finally.
- Prefer restructuring over locking: immutable snapshots, single-consumer channels, or confining
  mutable state to one hosted service remove the lock entirely.
- Async-friendly one-time init: `Lazy<Task<T>>` or a cached task; do not `.Result` a lazy inside a
  request path.
- `TaskCompletionSource` bridges callback/event APIs into tasks — create with
  `TaskCreationOptions.RunContinuationsAsynchronously` to keep completers from running arbitrary
  continuations inline on their own stack (a classic deadlock source).
