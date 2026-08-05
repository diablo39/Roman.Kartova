# Performance and caching

### Caching decorators
Use `functools.cache` (unbounded) or `functools.lru_cache(maxsize=...)` for pure,
hashable-argument functions, and `functools.cached_property` for expensive per-instance
values. Cache only side-effect-free computations; never cache on mutable or unhashable
inputs, and beware caching methods on long-lived instances (keeps `self` alive). Watch for
N+1 access patterns (DB/HTTP in loops) — batch instead. Reach for `__slots__` on classes
instantiated in large numbers.

### Network calls and timeouts
Every outbound `requests`/`httpx`/socket call sets an explicit timeout; a missing timeout
lets a slow peer hang the caller indefinitely. Add retry with backoff for idempotent
calls. Severity S2 (gate QUA-PY).
