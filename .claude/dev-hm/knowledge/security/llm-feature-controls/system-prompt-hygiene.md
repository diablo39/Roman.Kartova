# LLM feature controls — System prompt hygiene

Section of `knowledge/security/llm-feature-controls.md`.


Our prompts contain no secrets: no API keys, connection strings, or credentials, and no
per-customer data beyond what the requesting caller may already see. Anything placed in model
context can surface in model output, so the system prompt is configuration, not a vault — and not
an access-control mechanism. Every "must not" our prompt states is either backed by a server-side
control from this file or its sibling, or consciously accepted as advisory-only and recorded as
such. Designed this way, disclosure of the prompt itself costs nothing.

Verification tests: the repository's secret scanner runs over prompt template files (same
detector set as `knowledge/security/secrets-and-keys.md`); review confirms each prompt-stated
restriction maps to an enforcing control or a recorded advisory-only decision.
