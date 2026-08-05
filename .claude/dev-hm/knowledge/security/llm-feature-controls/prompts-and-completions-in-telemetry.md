# LLM feature controls — Prompts and completions in telemetry

Section of `knowledge/security/llm-feature-controls.md`.


A prompt or completion inherits the classification tier of the data inside it
(`knowledge/security/data-classification.md`): a prompt carrying a Tier-3 value is Tier-3
content. Our features therefore keep raw prompts and completions out of general logs, traces,
metric labels, and third-party analytics. What the event stream records instead is metadata with
stable event codes — model identifier, token counts, latency, validation and refusal outcomes,
correlation id — per `knowledge/security/security-logging-detection.md#event-codes`; the AI event
code family is enumerated in `knowledge/security/agentic-tool-controls.md`. When evaluation or
debugging genuinely needs content, it goes to a dedicated store carrying the same tier controls
(access, encryption, retention, deletion) as the source data, with the retention period stated.

Verification tests: a log-capture test drives the feature with a marked Tier-3 value in the user
message and asserts the marker appears nowhere in captured logs, traces, or metrics — the same
harness as the data-classification telemetry test; a schema test on the model-call event asserts
it carries code, model id, outcome, and correlation id, and no content field.
