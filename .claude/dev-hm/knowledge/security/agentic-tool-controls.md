# Agentic tool controls

The controls that bound what model-driven agent features in our products can do: every tool a
model can invoke enforces the requesting caller's authority, every input to a tool is validated,
irreversible actions pass an explicit gate, autonomy runs inside declared ceilings, and every
step leaves a correlated event. Structured against the OWASP Top 10 for Agentic Applications
(edition per `knowledge/shared/versions.md`), with LLM06 excessive agency from the LLM Top 10
landing here as well. Content handling — keeping untrusted content out of our instruction
channel and treating model output as untrusted — is the sibling file,
`knowledge/security/llm-feature-controls.md`; this file is about authority and actions. Each
section states the control our feature provides and the test that asserts it holds; the mandate
and marker convention live in `knowledge/security/control-verification-tests.md`.

The organizing idea: the model plans, our code decides. A model may propose any tool call it
likes; whether the call runs, as whom, against what, and with what ceilings is enforced by the
tool layer we build — which is why every control here is testable with a stubbed model that
emits whatever tool call the test needs.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| Agentic Top 10 map | `knowledge/security/agentic-tool-controls/agentic-top-10-map.md` |
| Authority binding | `knowledge/security/agentic-tool-controls/authority-binding.md` |
| Tool inputs | `knowledge/security/agentic-tool-controls/tool-inputs.md` |
| Action gating | `knowledge/security/agentic-tool-controls/action-gating.md` |
| Execution surface | `knowledge/security/agentic-tool-controls/execution-surface.md` |
| Memory isolation | `knowledge/security/agentic-tool-controls/memory-isolation.md` |
| Bounded autonomy | `knowledge/security/agentic-tool-controls/bounded-autonomy.md` |
| Model and tool event codes | `knowledge/security/agentic-tool-controls/model-and-tool-event-codes.md` |
