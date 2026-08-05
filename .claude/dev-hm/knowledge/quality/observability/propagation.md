# Observability — Propagation

Section of `knowledge/quality/observability.md`.


The control: context propagates across every boundary, or the trace dies there and correlation
with it. Outbound HTTP and gRPC calls carry the W3C Trace Context headers (traceparent, and
tracestate where used); queue and stream publishes put the context into message headers, and
consumers restore it so their spans join the producer's trace instead of starting orphaned
ones; scheduled jobs start a fresh trace but link to what triggered them. In-process
propagation across async boundaries — thread pools, executors, task schedulers — uses the
stack's context mechanism, and this is where propagation usually breaks: work handed to a pool
without capturing context produces spans with no parent. New service-to-service calls join the
repository's propagation convention (QUA-043); where a legacy correlation-ID scheme exists,
carry it alongside the standard headers rather than inventing a third scheme. Baggage
propagates small cross-cutting values (tenant, experiment arm) to every downstream hop — it
travels on the wire, so never secrets or personal data, and keep it to a few tiny entries.

Verification: a propagation test has service A call a stub B and asserts the traceparent
header arrives and B's span is a child of A's; the queue variant publishes then consumes and
asserts the consumer span carries the producer's trace ID; an async variant asserts spans
created after a pool hop still parent correctly.
