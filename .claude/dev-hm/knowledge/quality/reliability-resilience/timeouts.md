# Reliability and resilience — Timeouts

Section of `knowledge/quality/reliability-resilience.md`.


The control: every remote call — HTTP, gRPC, database, broker, cache, external SDK — carries an
explicit timeout. Distinguish the phases where the stack exposes them: connect timeout (small,
seconds), read/request timeout (from the operation's budget), and pool-acquisition timeout (so an
exhausted pool fails fast instead of queueing forever). A library default counts as absent
unless it is stated in config (QUA-041); many defaults are infinite or minutes long, which is an
outage multiplier. Derive values from the caller's budget, not from optimism: an operation
serving a two-second endpoint cannot grant a dependency thirty seconds, and the sum of
sequential downstream timeouts plus retries must fit inside what the caller will wait.

Propagate deadlines, don't just set them: pass the remaining budget downstream (gRPC deadlines,
context cancellation, cancellation tokens, abort signals) so that when the caller gives up, work
stops everywhere instead of completing uselessly. Cancellation is cooperative — verify the code
actually observes it at its loop and await points.

Verification: a test points the client at a dependency that never responds (a stub that accepts
and hangs, or a latency fault via a proxy such as Toxiproxy) and asserts the call fails with a
timeout error within the configured bound, carrying enough context to identify the operation.
Where the stack supports it, a lint or construction-time check refuses clients built without an
explicit timeout.
