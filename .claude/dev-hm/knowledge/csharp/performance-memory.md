# C# performance and memory

Allocation-aware C# for measured hot paths. The method comes first: budgets and load testing are
`knowledge/quality/performance-capacity.md`; finding the hot path with `dotnet-trace`/counters is
`knowledge/csharp/debugging.md`. Everything below is for code a profile has already convicted —
applied speculatively it costs readability and often performance too. Tool versions are in
`knowledge/shared/versions.md`.

## Measure first: BenchmarkDotNet {#benchmarkdotnet}

Micro-benchmarks run through BenchmarkDotNet, never a hand-rolled `Stopwatch` loop (no warmup, no
statistics, dead-code elimination lies to you).

```csharp
[MemoryDiagnoser]
public class ParserBenchmarks
{
    [Params(128, 4096)] public int Size;
    private string _input = "";

    [GlobalSetup] public void Setup() => _input = BuildInput(Size);

    [Benchmark(Baseline = true)] public Order ParseOld() => LegacyParser.Parse(_input);
    [Benchmark]                  public Order ParseNew() => SpanParser.Parse(_input);
}
```

Rules that keep results honest: run in Release from a console project (not under the test host or
a debugger); return or `Consume` results so the JIT cannot delete the work; `[MemoryDiagnoser]` on
by default — allocation deltas are usually the story; compare against a `Baseline = true` method so
the claim is a ratio, not a wall-clock number; commit the benchmark next to the optimization so the
claim stays re-checkable (`knowledge/quality/performance-capacity/benchmarks.md`). Macro effects
(GC pressure under real load) are validated with load tests and `dotnet-counters`, not
micro-benchmarks.

## Span and Memory {#span}

`Span<T>`/`ReadOnlySpan<T>` are views over existing memory — slicing, parsing, and copying without
allocating substrings or subarrays:

```csharp
ReadOnlySpan<char> line = raw;                       // string → span, no copy
var comma = line.IndexOf(',');
var id    = long.Parse(line[..comma]);               // parses a slice, no Substring
var rest  = line[(comma + 1)..];
```

- `Span` is a `ref struct`: stack-only, cannot be a field of a normal class, cannot cross `await`
  or `yield`, cannot be captured by a lambda. For async or storage, use `Memory<T>` /
  `ReadOnlyMemory<T>` and call `.Span` at the synchronous point of use.
- The BCL is span-first: `TryParse`/`TryFormat` over spans, `string.AsSpan()`, span overloads on
  streams, `IUtf8SpanFormattable` for writing numbers straight into UTF-8 buffers.
- APIs you own on hot paths: accept `ReadOnlySpan<T>` where the caller may have a slice; it
  composes with strings, arrays, and stackalloc alike.

### stackalloc — bounded, constant, guarded {#stackalloc}

```csharp
Span<byte> buffer = requiredLength <= 256
    ? stackalloc byte[256]
    : rented = ArrayPool<byte>.Shared.Rent(requiredLength);
```

Safety rules (non-negotiable): the stack allocation size is a small compile-time-ish constant
(≤ ~256–1024 bytes), never derived from input length — attacker-sized `stackalloc` is a stack
overflow, i.e. process death. The rent-fallback pattern above is the standard shape; slice the
span to the length actually used.

## ArrayPool {#arraypool}

`ArrayPool<T>.Shared` removes large short-lived buffer allocations (the LOH/Gen2 churn kind):

```csharp
var buffer = ArrayPool<byte>.Shared.Rent(size);      // returned array may be LONGER than size
try { await stream.ReadExactlyAsync(buffer.AsMemory(0, size), ct); Process(buffer.AsSpan(0, size)); }
finally { ArrayPool<byte>.Shared.Return(buffer); }
```

- Return in `finally`; never use the array after return; never return twice (double-return corrupts
  the pool and manifests as cross-request data bleed).
- The rented array is at least the requested size — always slice to the used length.
- Buffers that held secrets or personal data: `Return(buffer, clearArray: true)`, or the next
  renter can read them (`knowledge/security/data-classification.md`).
- `IMemoryOwner<T>`/`MemoryPool<T>` wrap the same idea in a disposable when ownership must transfer
  across method boundaries.

## SearchValues and string work {#searchvalues}

- `SearchValues<T>` precomputes an optimized (often vectorized) membership structure for repeated
  `IndexOfAny`-style searches — create once as `static readonly`, use everywhere:

```csharp
private static readonly SearchValues<char> Separators = SearchValues.Create(",;|\t");
var idx = input.AsSpan().IndexOfAny(Separators);
```

  String-set variants (`SearchValues.Create(strings, StringComparison.OrdinalIgnoreCase)`) cover
  multi-token scanning without regex.
- Comparisons: `string.Equals(a, b, StringComparison.OrdinalIgnoreCase)` — never
  `ToLower()`-and-compare (allocates and is culture-buggy). Default to `Ordinal` for
  machine-facing strings.
- Building: interpolated strings are fine (lowered to efficient handlers); hot-path concatenation
  in loops wants a reused `StringBuilder` or `string.Create(length, state, action)` when the length
  is known. Compile-time-known regexes are `[GeneratedRegex]`
  (`knowledge/csharp/source-generators.md#regex`).

## Collections and data shape {#collections}

- Pre-size when the count is known: `new List<T>(n)`, `new Dictionary<K,V>(n)` — growth doubling
  is the hidden allocator in load-and-fill code.
- Build-once, read-forever lookups: `FrozenDictionary`/`FrozenSet` (`.ToFrozenDictionary()`) —
  slower to build, fastest to read; ideal for static config and routing tables.
- Return `IReadOnlyList<T>`/arrays from hot APIs rather than `IEnumerable<T>` that forces callers
  to re-enumerate or defensively copy.
- LINQ and closures on hot paths allocate (delegates, boxes, iterators) — a plain loop is the
  optimization; elsewhere LINQ's clarity wins (`knowledge/csharp/review-checklist.md`, S3 row).
- Small immutable value types: `readonly record struct` avoids heap allocation and gets value
  equality; keep structs small (≲ 2–3 words) and readonly to avoid defensive copies.

## GC pressure, not GC tuning {#gc}

Allocation itself is cheap; the costs are Gen2/LOH churn (objects ≥ 85 KB go to the LOH) and
mid-life objects that survive to Gen1/2 and die. The fixes are the patterns above — pool the big
buffers, slice instead of copy, stream instead of materialize. Object pooling beyond arrays
(`ObjectPool<T>`) is for expensive-to-build, provably-hot objects only; pooled objects must be
reset on return, and a leaky reset is a correctness bug worse than the allocation. Reach for GC
mode settings (server GC is already the ASP.NET Core default) only with counter evidence
(`% time in GC`, gen sizes via `dotnet-counters`), and validate any change under load
(`knowledge/csharp/debugging.md#memory`).

## Boundaries {#boundaries}

Safe managed constructs above cover nearly every real hot path. `unsafe`, raw pointers, and
`Unsafe.*` tricks need a benchmark proving the safe version insufficient *and* reviewer sign-off —
they trade memory safety for the last few percent and are almost never warranted in service code.
Do not micro-optimize cold paths: dependency I/O dominates services, and the profile
(`knowledge/quality/performance-capacity/profiling.md`) tells you where the heat actually is.
