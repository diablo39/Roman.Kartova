# Authorization design — Verification tests

Section of `knowledge/security/authorization-design.md`.


The control tests this file's controls map to, with runnable patterns in
`knowledge/security/control-test-patterns-access.md`:

- Wrong role refused: a valid identity without the permission gets the refusal shape, and the
  operation has no effect.
- Cross-object refused: another principal's record is refused with nothing about it leaking, for
  get, update, and delete alike.
- Deny-by-default wiring: router introspection pins the unprotected set to the declared
  exemption list.
- Two-tenant isolation: the other tenant's rows appear in no response, list, export, or error.
- Fail closed: with the policy dependency erroring, the protected operation is refused.
- Policy unit suite: the decision table exercised exhaustively at unit level, including the deny
  rows.
