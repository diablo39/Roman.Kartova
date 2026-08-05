# Control-test patterns: parameterized statements, encoding, parsing, telemetry, limits — Keeping probes honest

Section of `knowledge/security/control-test-patterns-dataflow.md`.


When production or review finds a value our code mishandles, it joins the corpus first, the
corpus test fails, and the fix lands turning it green. Three habits keep these tests meaningful:

- Assert the untouched state alongside the refusal: zero statements executed, zero handler
  invocations, zero jobs queued. A refusal with a side effect is not a refusal.
- Exercise the enforcing layer the production path uses — the real template engine, the real
  deserializer options, the real engine in a container — never a stand-in that encodes or
  quotes differently.
- Treat probe softening, corpus shrinking, and assertion broadening as control-test weakening,
  gated under `knowledge/security/control-verification-tests.md#durability`.
