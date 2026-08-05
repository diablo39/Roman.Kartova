# Correctness and Pythonic idioms

### Mutable default arguments
A default argument is evaluated once at definition time, so a `[]`, `{}`, or `set()`
default is shared across calls and accumulates state. Use `None` and build inside.
```python
def add(item, bucket=None):        # not bucket=[]
    bucket = [] if bucket is None else bucket
    bucket.append(item)
    return bucket
```
Severity S1 — a real aliasing bug. Also flagged by ruff B006 (gate QUA-PY).

### EAFP over LBYL
Prefer "easier to ask forgiveness than permission": attempt the operation and handle the
specific exception, rather than pre-checking with a race-prone `if`. EAFP avoids TOCTOU
gaps on files and dict/attr access. LBYL is fine for cheap, non-racy guards.

### Resource handling with context managers
Files, sockets, locks, DB sessions, and Testcontainers must be acquired with `with` (or
`async with`) so they close on every path including exceptions. Flag manual
`open()`/`close()` pairs and resources leaked on error branches. Use
`contextlib.contextmanager` or `ExitStack` for composite cleanup.

### Iterators, generators, comprehensions
Use generators/`yield` for large or streamed data to bound memory; materialize a list
only when you need random access or multiple passes. Comprehensions beat manual
accumulation loops, but a comprehension with side effects or deep nesting should be a
loop. Prefer `enumerate`, `zip`, `any`, `all`, and `str.join` over index bookkeeping and
`+=` string building in loops.

### pathlib and f-strings
Use `pathlib.Path` for filesystem paths over `os.path` string munging. Use f-strings over
`%` and `str.format` for readability. Compare to `None`/`True`/`False` with `is`.
