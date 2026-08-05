# Agentic tool controls — Authority binding

Section of `knowledge/security/agentic-tool-controls.md`.


Every model-invocable tool is a server-side operation that enforces the requesting caller's
authentication and authorization inside the tool implementation — the same checks the equivalent
API endpoint runs (`knowledge/security/authorization-design.md`: handler checks, object-level
ownership, tenant isolation). The agent runtime holds no standing credential broader than the
caller: tool executions carry a credential derived from the caller's session (token exchange,
on-behalf-of, or the session itself), scoped to the tools the feature registers. The model
chooses which tool to call; it never chooses as whom.

The confused-deputy rule makes this concrete: any call that would be refused when the user makes
it directly must be refused when the model makes it on the user's behalf. A tool running under a
privileged service account turns every prompt-injection attempt upstream into that service
account's authority — binding to the caller is what makes goal hijack survivable.

```python
# fail: tool executes with the runtime's service credential — every caller becomes admin
def delete_report(report_id: str):
    return reports_admin_client.delete(report_id)
# pass: caller identity flows in; the tool authorizes like any handler
def delete_report(ctx: ToolContext, report_id: str):
    report = reports.get(report_id, tenant=ctx.principal.tenant)   # tenant predicate from session
    authorize(ctx.principal, "report:delete", report)              # ownership + role check inside the tool
    return reports.delete(report.id)
```

Tool registration is least-privilege: a feature registers only the tools it needs, and read and
write variants are separate tools so read-only features carry no write registration at all.
Sub-agents and downstream agent services receive the same caller-scoped credential, never a
broader one — an agent-to-agent hop authenticates like any service call
(`knowledge/security/transport-protection.md`), with the originating caller's authority attached.

Verification tests, with a stubbed model emitting the tool call: a caller without the permission
invoking each tool through the agent path is refused per the tool's error contract with no side
effect; caller A's session with caller B's object id in the arguments is refused (the
cross-object pattern from `knowledge/security/control-test-patterns-access.md`); calling the tool
layer directly, bypassing the model, yields the same refusal — proving the tool, not the prompt,
enforces it; a registration test asserts the feature's tool list matches its declared set.
