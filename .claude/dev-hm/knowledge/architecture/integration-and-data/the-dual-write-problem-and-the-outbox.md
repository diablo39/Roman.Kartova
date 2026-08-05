# Integration and data: pattern selection by quality scenario — The dual-write problem and the outbox

Section of `knowledge/architecture/integration-and-data.md`.


Any "write to my database and publish to the broker" sequence is two systems without a shared
transaction: crash between the two and state diverges silently. This is the most common
integration defect in event-driven systems, and retries don't fix it — they trade lost events
for duplicates without ordering guarantees.

Transactional outbox (default fix): write the business change and the event into the same local
transaction (event into an `outbox` table), then a relay publishes committed outbox rows to the
broker and marks them sent. Relay options: polling publisher (simple, adds polling-interval
latency) or log-based CDC (Debezium-class tooling tails the WAL — lower latency, one more
operational component). Result is at-least-once publication in commit order.

At-least-once means consumers see duplicates; pair the outbox with idempotent consumers: either
natural idempotency (set-semantics updates) or an inbox — record processed event ids in the
consumer's database, in the same transaction as the consumer's own state change, and skip
already-seen ids. Design every consumer idempotent regardless of pattern choice; redelivery is
normal operation, not an edge case.

Listen-to-yourself variant, and the tempting alternative of distributed transactions (2PC/XA)
across services or service+broker, are legacy exceptions: 2PC couples availability of all
participants and modern brokers and cloud services mostly don't support it. Use only where it
already exists and works; don't introduce it into new designs.
