# Python debugging and profiling

Tools and playbooks for diagnosing hard Python issues: interactive debugging, crash and
memory analysis, profiling, and asyncio faults. Match the tool to the symptom; instrument
before hypothesizing.

## Interactive debugging with pdb

`breakpoint()` drops into pdb at that line (honors `PYTHONBREAKPOINT`, so
`PYTHONBREAKPOINT=0` disables and `PYTHONBREAKPOINT=ipdb.set_trace` swaps debuggers).

| Command | Action |
|---|---|
| `b file:line` / `b func` | Set breakpoint (`tbreak` = one-shot) |
| `condition <n> <expr>` | Break only when expr is true |
| `c` | Continue |
| `n` / `s` | Step over / step into |
| `r` | Run to return of current function |
| `l` / `ll` | List source around / whole function |
| `w` (where) / `u` / `d` | Stack trace / move up / down frames |
| `p` / `pp expr` | Print / pretty-print |
| `interact` | Open a REPL in the current frame |
| `cl` | Clear breakpoints |

- `python -m pdb script.py` runs under the debugger; `-c continue` runs to the first
  exception then stops.
- `import pdb; pdb.pm()` starts post-mortem on the last traceback.
- Under pytest: `--pdb` enters on failure, `--trace` enters at test start.

## Crashes and stack traces

- Read tracebacks bottom-up: the last frame is where it raised; look for the boundary
  where your code calls into a library. Follow `raise ... from` chains and, for asyncio,
  `ExceptionGroup` sub-exceptions.
- `faulthandler` dumps a Python traceback on hard faults: `faulthandler.enable()`, or
  `python -X faulthandler`. Use `faulthandler.dump_traceback_later(seconds)` to catch
  hangs, and it prints a stack on `SIGSEGV`/`SIGABRT` from C extensions.
- The `traceback` module formats/inspects exceptions programmatically.
- Prefer `logging` over `print` for durable diagnostics; set levels and structured fields.

## CPU profiling

- Deterministic: `python -m cProfile -o out.prof script.py`, then inspect with `pstats`
  (`sort_stats("cumulative")`) or visualize with `snakeviz out.prof`.
- Microbenchmarks: `timeit` for isolated snippets; avoid drawing macro conclusions from it.
- Sampling in production: `py-spy` needs no code changes and low overhead.
  - `py-spy top --pid <PID>` — live per-function CPU, like `top`.
  - `py-spy record -o flame.svg -- python script.py` — flame graph of a run.
  - `py-spy dump --pid <PID>` — snapshot every thread's stack, ideal for a hung process.
- Line-level: `line_profiler` (`@profile` + `kernprof -l -v`) when a hotspot needs
  per-line attribution.

## Memory diagnosis

- `tracemalloc` (stdlib): `tracemalloc.start()`, take `snapshot()`s, and
  `snapshot.compare_to(prev, "lineno")` to find growth by allocation site. First reach for
  this on suspected leaks.
- `memray` (modern allocation profiler): tracks native + Python allocations, produces
  flame graphs and a live mode; run `memray run script.py` then `memray flamegraph`.
- `gc` module: `gc.collect()` returns unreachable counts; `gc.set_debug(gc.DEBUG_LEAK)`
  and `gc.garbage` expose reference cycles. Objects with `__del__` in a cycle historically
  blocked collection — audit finalizers. `objgraph` visualizes reference chains.
- Note: the older `memory_profiler` package is effectively unmaintained; prefer
  `tracemalloc` and `memray`.

## asyncio diagnosis

- Enable debug mode: `PYTHONASYNCIODEBUG=1`, `asyncio.run(main(), debug=True)`, or
  `loop.set_debug(True)`. It logs slow callbacks, never-retrieved exceptions, and
  never-awaited coroutines with the offending source location.
- "coroutine was never awaited": a coroutine was created but not awaited or scheduled —
  usually a missing `await` or a `create_task` result discarded.
- Blocking-the-loop stalls: debug mode warns on callbacks over 100 ms; move the blocking
  call to `asyncio.to_thread` or an executor.
- Inspect live tasks: `asyncio.all_tasks()` and `task.get_stack()` show what is pending;
  a task awaiting a lock it also holds is a classic deadlock. `gather(..., return_exceptions=True)`
  can hide failures — check results.
- "Event loop is closed" / "already running": mixing `asyncio.run` with an existing loop
  (e.g. inside a framework or Jupyter). Use the running loop instead of creating one.
- `TaskGroup` (3.11+) raises grouped failures; catch with `except*`. `aiomonitor` gives a
  live console into a running loop, and the asyncio CLI inspects one from outside:
  `python -m asyncio ps <PID>` / `pstree <PID>` (3.14+) print the live task tree.
- The correct patterns these faults deviate from (cancellation contract, timeouts,
  backpressure) are in `knowledge/python/async-patterns.md`.

## GIL and free-threading

Thread contention on CPU-bound code is expected under the GIL — use processes or offload
to native code. On the free-threaded build (the `t`-suffixed interpreter; current line in
`knowledge/shared/versions.md`), the GIL no longer serializes access, so latent data races
become real: audit shared mutable state, prefer immutable messages or explicit locks, and
validate C-extension thread safety.

## Systematic workflow

1. Reproduce reliably — capture inputs, environment (`uv run` for a locked env), and seed
   any randomness; a flaky repro first needs to be made deterministic.
2. Minimize — shrink to the smallest failing case; bisect commits with `git bisect` or
   binary-search inputs.
3. Instrument — pick the tool above for the symptom (pdb for logic, py-spy for CPU,
   tracemalloc/memray for memory, asyncio debug for concurrency).
4. Form one hypothesis, change one thing, verify against the repro.
5. Report root cause with `file:line` evidence and the minimal fix; note if a fix needs
   runtime confirmation rather than asserting it works.
