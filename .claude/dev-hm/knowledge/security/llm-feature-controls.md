# LLM feature controls

The controls our LLM-integrated features provide when untrusted content reaches a model: our
trusted instructions stay structurally separate from the content the model processes, everything
the model produces is treated as untrusted input to whatever consumes it, and prompts and
completions are handled with the classification of the data inside them. Structured against the
OWASP GenAI LLM Top 10 (edition per `knowledge/shared/versions.md`). Each section states the
control our feature provides and the test that asserts it holds; the test mandate and marker
convention live in `knowledge/security/control-verification-tests.md`. Authority and action
bounding for model-driven tools — what an agent may do, as opposed to what it may read and say —
lives in `knowledge/security/agentic-tool-controls.md`.

One design premise runs through this file: the model is not a security boundary. A model can be
steered by content it processes, so no prompt wording, delimiter, or politeness instruction is a
control by itself. The controls below all live in our code around the model — in how we assemble
context, what we let output reach, and what the surrounding service enforces — which is why each
one has a deterministic test that runs with a stubbed model.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| LLM Top 10 map | `knowledge/security/llm-feature-controls/llm-top-10-map.md` |
| Content isolation | `knowledge/security/llm-feature-controls/content-isolation.md` |
| Retrieval scoping | `knowledge/security/llm-feature-controls/retrieval-scoping.md` |
| Output handling | `knowledge/security/llm-feature-controls/output-handling.md` |
| System prompt hygiene | `knowledge/security/llm-feature-controls/system-prompt-hygiene.md` |
| Prompts and completions in telemetry | `knowledge/security/llm-feature-controls/prompts-and-completions-in-telemetry.md` |
| Consumption bounds | `knowledge/security/llm-feature-controls/consumption-bounds.md` |
