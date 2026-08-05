# Observability — Structured logs

Section of `knowledge/quality/observability.md`.


The control: logs are events, not sentences. Every entry goes through the project logger as a
message plus machine-readable fields — operation, entity identifiers, duration, outcome —
never data interpolated into prose that a pipeline must regex back out. Context that applies to
a whole request (trace and request IDs, principal, tenant) is attached once through the
logger's context mechanism and appears on every line, instead of being hand-copied into some.
The single most useful record is the canonical log line: one wide event emitted at request
completion carrying everything about it — route, status, duration, retries used, fallbacks
taken, key decision points — so most debugging is one query instead of a reconstruction from
fragments. Respect volume: logs are the expensive signal; per-item logging inside hot loops
becomes cost and noise, so aggregate, sample, or demote to debug. Leftover print and
console-log residue fails QUA-040 on sight.

Verification: a log-shape test captures the logger output for a representative operation and
asserts the expected fields are present with the expected types; new fields follow the
repository's field-naming conventions so downstream parsing keeps working.
