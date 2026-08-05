# Resource protection — Bounded queues and concurrency

Section of `knowledge/security/resource-protection.md`.


Every queue in our code is bounded, and the bound comes with a stated rejection behavior:
when full, new work is refused quickly (backpressure to the caller) rather than absorbed into
unbounded memory. Worker pools have explicit sizes; connection pools have caps and acquisition
timeouts; expensive sections that cannot be pooled are guarded by a semaphore with a declared
limit. The recurring failure this prevents is the invisible unbounded buffer — a channel,
an executor queue, a list of pending requests — that converts overload into an out-of-memory
crash minutes later, far from the cause.

Verification tests: with the pool saturated by held test jobs, the next submission is refused
within the fast-refusal budget (429/503 path) rather than queued indefinitely; a soak test at
the queue bound shows flat memory; the acquisition-timeout path returns the declared error, not
a hang.
