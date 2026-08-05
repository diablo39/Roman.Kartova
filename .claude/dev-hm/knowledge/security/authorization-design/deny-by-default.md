# Authorization design — Deny by default

Section of `knowledge/security/authorization-design.md`.


Protection is inherited, not remembered. Our entry points register under the
authentication/authorization layer by construction:

- Middleware attaches at the router or application root, so every new route is protected the day
  it is added. Per-route guards added from memory are the failure mode this wiring removes — the
  route someone forgets is exactly the one that ships open.
- Opt-outs are explicit, few, and centrally declared: a health endpoint, a metrics endpoint, the
  login route itself. Each opt-out is visible in code as a deliberate annotation and declared in
  the handoff with its reason, so the gate reviews the exceptions, not the rule.
- Every entry point counts, not only HTTP routes: message consumers, scheduled jobs, RPC
  handlers, and internal admin endpoints each resolve a principal (a user, a service identity
  per `knowledge/security/transport-protection.md#peer-identity`, or a declared system context)
  before doing protected work.
- The deny path is the error path too: when the policy check throws or times out, the outcome is
  refusal (SEC-071). An authorization layer that fails open under load is a control that removes
  itself exactly when someone is probing.

```python
# fail: protection is opt-in — the next route added without the decorator is open
@app.get("/api/orders")
@require_auth
def list_orders(): ...

# pass: protection is opt-out — the router carries the guard, exemptions are declared once
app.include_router(api_router, dependencies=[Depends(require_principal)])
PUBLIC_ROUTES = {"/health", "/metrics"}   # reviewed at the gate
```

The wiring gets its own control test: router introspection asserting the set of unprotected
routes is exactly the declared exemption list, so a route added outside the guard fails the
build (`knowledge/security/control-test-patterns-access.md`).
