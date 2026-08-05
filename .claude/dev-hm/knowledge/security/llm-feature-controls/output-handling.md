# LLM feature controls — Output handling

Section of `knowledge/security/llm-feature-controls.md`.


Model output is untrusted input to every downstream sink, held to the same bar as a request body
from the internet. Three obligations, in order of severity:

Never executed. No completion text reaches eval, a shell, a SQL string, a template engine, or any
interpreter. When code execution is the product's purpose, it runs inside the sandbox described
in `knowledge/security/agentic-tool-controls.md` — never in the service process.

Encoded before rendering. Completions rendered into HTML are context-encoded like any untrusted
value (the encoding rules in `knowledge/security/secure-coding-review.md` apply unchanged).
Markdown rendering goes through a sanitizer, and links and images are constrained to an allowed
URL scheme and host set — an auto-fetched image URL assembled by the model is an egress channel
for anything else in the context, so our renderers fetch images only from hosts we declare.

Validated before acting. Structured output (JSON mode, tool arguments, extracted fields) is
schema-validated exactly like an external request (`knowledge/security/api-surface.md` schema
contracts): parse against the declared schema, reject unknown fields, bound sizes and ranges. On
validation failure the feature refuses or re-asks within a bounded retry count; it never "repairs"
malformed output by evaluating it. Downstream calls are built from the validated fields, never
from raw completion text. Free-text output that informs human decisions is presented as
generated content with its sources, not as system fact.

```python
# fail: completion drives the sink directly
db.execute(completion.text)
# pass: schema gate between model and sink; the sink sees validated fields only
action = ProposedUpdate.model_validate_json(completion.text)  # raises on mismatch
orders.update(order_id=action.order_id, status=action.status)
```

Verification tests, all with a stubbed model so they are deterministic: a stub completion
containing script markup renders inert in the response body; a stub completion referencing an
image on a non-allowed host is not fetched and not rendered (assert the fetch spy was never
called); a stub completion that fails the schema yields the feature's stable error and the sink
handler is never entered; retry-on-malformed stops at the declared count.
