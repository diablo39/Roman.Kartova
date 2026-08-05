# LLM feature controls — Consumption bounds

Section of `knowledge/security/llm-feature-controls.md`.


Model calls are metered like any expensive operation our service performs. Per-principal request
budgets cover model-backed endpoints (`knowledge/security/api-surface.md` rate limits); each call
sets an output token ceiling and applies a declared input-size policy (truncate, summarize, or
refuse — chosen per feature and stated in its contract); retries on model errors are bounded with
backoff; and one user request may trigger at most a declared number of model calls — fan-out and
loop ceilings for agentic features live in
`knowledge/security/agentic-tool-controls.md`. Expensive batch inference runs under per-principal
quotas (`knowledge/security/resource-protection.md`).

Verification tests: a caller past its budget observes the refusal contract while another
principal still succeeds; the call configuration test asserts the output token ceiling is set;
an input above the declared size observes the declared policy, not an unbounded call.

Every control above is a protective control in the sense of
`knowledge/security/control-verification-tests.md`: its test must fail when the control is
removed, and refusals emit their security event
(`knowledge/security/security-logging-detection.md#event-codes`).
