# Agentic tool controls — Bounded autonomy

Section of `knowledge/security/agentic-tool-controls.md`.


Every agent loop runs inside ceilings declared in configuration: maximum tool calls per user
request, maximum wall-clock time, a token or cost budget, and maximum sub-agent depth and
fan-out. Reaching a ceiling fails closed — the loop stops, the user gets the feature's refusal
contract, and the event is emitted. The ceilings are not model-adjustable: there is no tool that
raises a budget, and no retry-on-refusal that re-enters the loop. Tool failures inside the loop
follow bounded-retry rules (`knowledge/quality/reliability-resilience.md`). Operations can
disable a single tool or the whole feature at runtime through the project's flag mechanism — the
kill switch that bounds a misbehaving deployment without a deploy.

Verification tests: a stubbed model that requests another tool call forever observes the loop
stopping exactly at the configured cap, returning the refusal contract, emitting the ceiling
event; the same pattern one level down asserts sub-agent depth; a configuration test asserts
every registered agent loop declares its ceilings; flipping the kill-switch flag makes the next
tool call refuse.
