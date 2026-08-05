# LLM feature controls — LLM Top 10 map

Section of `knowledge/security/llm-feature-controls.md`.


Where each risk's control lives. This file covers content-handling controls; authority and
action bounding, plus supply chain, have their own files.

| Risk | Control | Depth |
|---|---|---|
| LLM01 Prompt injection | Instructions structurally separated from untrusted content; downstream controls hold even when the model is steered | this file, content isolation |
| LLM02 Sensitive information disclosure | Classification-aware prompt/completion handling; retrieval scoped to the caller | this file, telemetry and retrieval scoping; `knowledge/security/data-classification.md` |
| LLM03 Supply chain | Models, weights, and adapters treated as dependencies: pinned, provenance-verified | `knowledge/security/supply-chain.md` |
| LLM04 Data and model poisoning | Provenance of training and fine-tuning data; write control on shared retrieval corpora | `knowledge/security/supply-chain.md`; this file, retrieval scoping |
| LLM05 Improper output handling | Model output is untrusted input to every sink: validated, encoded, never executed | this file, output handling |
| LLM06 Excessive agency | Tool authority bound to the caller, gated actions, bounded loops | `knowledge/security/agentic-tool-controls.md` |
| LLM07 System prompt leakage | No secrets in prompts; every prompt rule backed by a server-side control | this file, system prompt hygiene |
| LLM08 Vector and embedding weaknesses | Retrieval filtered by the caller's authorization before context assembly | this file, retrieval scoping |
| LLM09 Misinformation | Output that drives decisions is schema-constrained and grounded; free text is presented as generated, not authoritative | this file, output handling |
| LLM10 Unbounded consumption | Per-principal budgets, token ceilings, bounded fan-out | this file, consumption bounds; `knowledge/security/resource-protection.md` |
