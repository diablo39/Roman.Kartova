# Agentic tool controls — Action gating

Section of `knowledge/security/agentic-tool-controls.md`.


Actions that are irreversible or high-impact — delete, send, pay, deploy, share outside the
tenant, change permissions — do not run on the model's say-so. Each such tool declares, at design
time, one of two gates: explicit confirmation by the human principal, or a declared allowlist the
action's parameters must match (for example, deploy targets limited to staging). The ungated path
is refused by the tool layer; a prompt instruction "ask before deleting" is a courtesy, not this
control.

Confirmation is engineered so the model cannot satisfy it. The tool call first produces a pending
action held server-side; the product UI, outside the model context, shows the principal the
exact parameters the server will execute — not the model's narration of them, which is the
trust-exploitation channel — and approval mints a single-use, expiring confirmation bound to that
pending action. A `confirmed: true` field in the tool schema is a gate the model can fill in
itself, which is no gate at all.

```typescript
// fail: the gate is a parameter — the planner can supply it
sendInvoice({ invoiceId, customerEmail, confirmed: true });
// pass: the gate is a server-side pending action approved out-of-band
const pending = await gate.propose("invoice:send", { invoiceId, customerEmail }, ctx.principal);
// UI displays pending.parameters to the principal; approval creates a one-time grant
await gate.execute(pending.id, grantFromUi);   // refused without a live grant for these exact parameters
```

Prefer reversible designs so fewer actions need gates at all: soft delete with an undo window,
draft-then-send, staged deploys with rollback. Reversibility is stated per tool in the handoff
alongside the gate decision.

Verification tests, with a stubbed model: invoking a gated tool without a grant is refused with
no side effect and emits its event; a grant for different parameters is refused; a reused or
expired grant is refused; a stub that supplies an in-band confirmation field is still refused;
for allowlist-gated tools, parameters outside the allowlist are refused and the allowlist lives
in reviewed configuration.
