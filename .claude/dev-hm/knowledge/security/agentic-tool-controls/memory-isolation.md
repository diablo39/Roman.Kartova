# Agentic tool controls — Memory isolation

Section of `knowledge/security/agentic-tool-controls.md`.


Agent memory — conversation history, long-term memory stores, learned preferences — is scoped to
the caller and tenant that produced it, and one principal's memory never enters another's
assembled context. Writes to memory are validated like any input, recorded with provenance (which
session, which source), and read back as data, not instructions — memory is one more untrusted
content channel under `knowledge/security/llm-feature-controls.md#content-isolation`, which is
what keeps a poisoned memory entry from becoming a persistent instruction. Derived artifacts
(summaries, embeddings) inherit the classification tier of their sources
(`knowledge/security/data-classification.md`).

Verification tests: memory written under principal A is absent from the context assembled for
principal B, asserted deterministically at the context-assembly layer; a memory entry containing
directive-looking text lands in a data block, never in system-role content (the template-contract
test from the sibling file covers memory variables too).
