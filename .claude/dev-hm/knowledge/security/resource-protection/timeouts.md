# Resource protection — Timeouts

Section of `knowledge/security/resource-protection.md`.


Work is bounded in time everywhere it can wait. Every request handler has a deadline; every
outbound call — HTTP, database, queue, cache — has connect and read timeouts shorter than the
budget of whoever is waiting for us, so a slow dependency degrades into a fast, well-shaped
error instead of a pile-up of stuck threads. Deadlines propagate: a handler with 2 s left does
not issue a 30 s downstream call. On the inbound side the server enforces header-read,
body-read, and idle timeouts, so a client that opens a connection and stops sending is
disconnected at the configured deadline instead of holding a slot indefinitely. Database
statement timeouts back the application-level deadline for queries. What happens after the
timeout — retry, fallback, fail fast — is resilience design:
`knowledge/quality/reliability-resilience.md`.

Verification tests: with a dependency stubbed to never respond, the handler returns the
declared failure shape within its deadline; a test client that completes headers and then goes
silent observes the connection closed at the read timeout; the statement-timeout configuration
has a snapshot test.
