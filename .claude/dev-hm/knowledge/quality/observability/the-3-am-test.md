# Observability — The 3 a.m. test

Section of `knowledge/quality/observability.md`.


The acceptance question for the observability of any change, worth asking in every review:
when this fails in production, can the responder find the failing request (trace), see why it
failed (structured log with context at the right level), and tell how many users are affected
(metric feeding an SLI)? A change that cannot answer all three has shipped a debugging session
to a future colleague at the worst possible hour; the controls above exist so the answer is
always yes.
