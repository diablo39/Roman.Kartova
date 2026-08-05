# Observability — Traces

Section of `knowledge/quality/observability.md`.


The control: a trace is the request's call graph across services, and spans are its nodes —
one per logical operation: inbound handler, outbound call, database query, queue publish and
consume, plus manual spans for meaningful internal stages that auto-instrumentation cannot
name. Spans carry attributes per the semantic conventions, set error status on failure, and
record the exception on the span, so a failed trace is self-explanatory. Auto-instrumentation
covers the frameworks and clients; add manual spans sparingly, where a human debugging would
want a boundary. Sampling keeps cost sane without losing the interesting traffic: head
sampling caps volume, and tail-based sampling in the collector keeps the errors and slow
outliers that debugging actually needs while dropping the boring successes. The trace ID
appears in the logs (that is the correlation), and on error surfaces where the repository does
that, so a support ticket can lead straight to the trace.

Verification: an integration test with an in-memory span exporter asserts the operation
produces spans with the expected names, attributes, and parent-child structure, and that a
failing call yields a span with error status and the recorded exception.
