# Control-test patterns: parameterized statements, encoding, parsing, telemetry, limits

Runnable patterns for the data-flow control families — the controls that keep values moving
through our code as data, never as instructions, types, or unbounded work. Each section names
the control our code provides, then a test asserting the protective behavior holds, exercised
through the layer that enforces it and phrased as behavior of our own code. The mandate, marker
convention, and fails-when-removed spot-check are defined in
`knowledge/security/control-verification-tests.md`; the harness table and access-control
families live in `knowledge/security/control-test-patterns-access.md`, browser families in
`knowledge/security/control-test-patterns-browser.md`. Control design depth:
`knowledge/security/secure-coding-review.md`, `knowledge/security/resource-protection.md`,
`knowledge/security/data-classification.md`.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| Probe corpus | `knowledge/security/control-test-patterns-dataflow/probe-corpus.md` |
| Data access: a probe value round-trips as literal data | `knowledge/security/control-test-patterns-dataflow/data-access-a-probe-value-round-trips-as-literal-data.md` |
| Data access: identifiers resolve only through the allowlist | `knowledge/security/control-test-patterns-dataflow/data-access-identifiers-resolve-only-through-the-allowlist.md` |
| Output rendering: markup in data stays text | `knowledge/security/control-test-patterns-dataflow/output-rendering-markup-in-data-stays-text.md` |
| Parsing: structured input outside the contract is rejected | `knowledge/security/control-test-patterns-dataflow/parsing-structured-input-outside-the-contract-is-rejected.md` |
| Serialization: only declared types are constructed | `knowledge/security/control-test-patterns-dataflow/serialization-only-declared-types-are-constructed.md` |
| Telemetry: planted canaries never surface in captured output | `knowledge/security/control-test-patterns-dataflow/telemetry-planted-canaries-never-surface-in-captured-output.md` |
| Resource limits: input past the cap is refused | `knowledge/security/control-test-patterns-dataflow/resource-limits-input-past-the-cap-is-refused.md` |
| Keeping probes honest | `knowledge/security/control-test-patterns-dataflow/keeping-probes-honest.md` |
