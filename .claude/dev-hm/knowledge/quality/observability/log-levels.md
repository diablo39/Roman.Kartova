# Observability — Log levels

Section of `knowledge/quality/observability.md`.


The control: levels are an alerting contract, not decoration. Error means a person should act:
an operation failed and was not recovered, data is at risk, an invariant broke. Warn means
degraded but coping: a retry eventually succeeded, a fallback is active, a deprecated path was
taken. Info marks state changes worth an audit trail: started, config loaded, job finished.
Debug is diagnostic detail, off in production. The consequence rules (QUA-044): a failure path
that ends in an unhandled 5xx or dropped work logs at error with the operation and correlation
ID; a coping path logs at warn — logging handled situations as errors trains responders to
ignore the error stream, which is how the real incident scrolls past unread. Every error entry
answers four questions: what operation, on what entity, why it failed, and which request
(correlation ID). Log an exception once, at the site that handles it, with its stack — not at
every propagation hop, which inflates counts and pages people for one fault five times.

Verification: failure-path tests assert both the outcome and the emitted event — level, message
shape, presence of operation and correlation fields. Log-based alerts key on error-level
events, so these tests are what keeps the pager honest.
