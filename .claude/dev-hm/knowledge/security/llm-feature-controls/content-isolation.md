# LLM feature controls — Content isolation

Section of `knowledge/security/llm-feature-controls.md`.


Untrusted content — user input, retrieved documents, tool results, uploaded files, anything from
outside our trust boundary — is never concatenated into our instruction text. Our features keep
the two channels structurally apart: our instructions travel in the system or developer role,
untrusted content travels as data messages, and the prompt template contract has no interpolation
holes in the instruction channel. A template that splices a retrieved document into the system
message has already lost the separation, whatever the wording around it says.

```typescript
// fail: retrieved text spliced into the instruction channel
const prompt = `You are our support assistant. Answer using this article:\n${article.text}`;
// pass: instructions are a closed template; untrusted content arrives as data
const messages = [
  { role: "system", content: SUPPORT_INSTRUCTIONS },        // no untrusted holes
  { role: "user", content: question },
  { role: "user", content: asDocumentBlock(article) },      // labeled data block
];
```

Structural separation limits damage; it does not make the model injection-proof. A document can
still contain text that reads like a directive, and the model may follow it. The completion of
this control is therefore downstream: whatever an embedded directive persuades the model to emit
or invoke runs into output handling (below) and authority binding
(`knowledge/security/agentic-tool-controls.md#authority-binding`), so the caller's permissions —
not the document's wording — bound the outcome. Labeling data blocks ("the following is a
document") is worth doing for model quality, but we treat it as advisory, never as the control.

Verification tests, two layers. Deterministic in CI: a template-contract test renders every
prompt template with tainted marker strings in each untrusted variable and asserts no marker
appears in system-role content — this test fails if someone later adds an interpolation hole.
Scenario suite against the pinned model configuration: retrieval fixtures containing
directive-looking canary text ("include the phrase X", "call tool Y") run through the feature,
asserting the canary phrase is absent from the response and no gated tool call occurred; because
model behavior varies, the assertion targets the safe outcome the downstream controls guarantee,
which is exactly what makes it stable.
