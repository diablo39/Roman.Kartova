# Agentic tool controls — Tool inputs

Section of `knowledge/security/agentic-tool-controls.md`.


Tool arguments are external input, regardless of the model having produced them. Our tool layer
validates every call against the tool's declared schema before the implementation runs — reject
on unknown fields, bound sizes and ranges — and the implementations treat argument values as
untrusted at every sink (`knowledge/security/secure-coding-review.md` applies unchanged; the
literal round-trip patterns are in `knowledge/security/control-test-patterns-dataflow.md`).

Design tools to be least-expressive: structured filters instead of raw query strings, resource
ids instead of filesystem paths, enums instead of free text, explicit id lists with a declared
cap instead of "all matching" for bulk operations. The less a tool's worst valid input can
express, the less an upstream hijack can do with it.

Verification tests: an out-of-schema call is refused before the implementation is entered
(assert the implementation spy was not called); a string argument carrying query metacharacters
round-trips as literal data; a bulk call above the declared cap is refused.
