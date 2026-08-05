# Agentic tool controls — Agentic Top 10 map

Section of `knowledge/security/agentic-tool-controls.md`.


Where each risk's control lives. This file covers authority and action bounding; content
isolation and supply chain have their own files.

| Risk | Control | Depth |
|---|---|---|
| ASI01 Agent goal hijack | A redirected goal cannot exceed the caller's authority or skip a gate: isolation upstream, binding and gating here | `knowledge/security/llm-feature-controls.md#content-isolation`; this file |
| ASI02 Tool misuse | Schema-validated, least-expressive tool inputs with per-tool caps | this file, tool inputs |
| ASI03 Identity and privilege abuse | Tool authority bound to the requesting caller; no ambient service credentials | this file, authority binding |
| ASI04 Agentic supply chain | Tools, MCP servers, models, and adapters are dependencies: pinned, reviewed, provenance-verified | `knowledge/security/supply-chain.md` |
| ASI05 Unexpected code execution | No execution sink reachable from model output; sandboxed interpreter where execution is the product | this file, execution surface |
| ASI06 Memory and context poisoning | Memory scoped per caller, treated as data, provenance recorded | this file, memory isolation |
| ASI07 Insecure inter-agent communication | Agent-to-agent calls authenticated like any service call | `knowledge/security/transport-protection.md` peer identity; this file, authority binding |
| ASI08 Cascading failures | Ceilings on steps, depth, and cost; bounded retries and timeouts on tool chains | this file, bounded autonomy; `knowledge/quality/reliability-resilience.md` |
| ASI09 Human-agent trust exploitation | Confirmation UI shows the action the server will execute, not the model's narration | this file, action gating |
| ASI10 Rogue agents | Hard ceilings, per-tool kill switch, complete correlated event trail | this file, bounded autonomy and event codes |
