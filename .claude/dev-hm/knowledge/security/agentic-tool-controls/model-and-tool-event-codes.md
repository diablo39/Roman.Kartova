# Agentic tool controls — Model and tool event codes

Section of `knowledge/security/agentic-tool-controls.md`.


Every model call, tool invocation, refusal (authorization, schema, gate, ceiling), confirmation
grant and denial, and loop stop emits a structured event with a stable code, following the scheme
in `knowledge/security/security-logging-detection.md#event-codes` — for example an
`ai.tool.denied` / `ai.gate.blocked` / `ai.loop.ceiling` family aligned to the repository's
convention. Events carry the correlation id of the originating user request, so the chain user
request → model call → tool calls → outcome is reconstructible end to end: this is what lets us
confirm the controls in this file were exercised, and it is the traceability answer to rogue-agent
and repudiation concerns. Prompt and completion content stays out of the event stream
(`knowledge/security/llm-feature-controls.md`, telemetry section).

Verification tests: every refusal test above also asserts its event code — the deny path emits
its event, per `knowledge/security/security-logging-detection.md`; a trace test drives one
request through a stubbed two-tool plan and asserts all emitted events share the request's
correlation id.

Every control above is a protective control in the sense of
`knowledge/security/control-verification-tests.md`: its test must fail when the control is
removed — detach the authorization check, bypass the gate, lift the ceiling in a scratch run, and
at least one mapped test goes red.
