# Authorization design — Object-level ownership

Section of `knowledge/security/authorization-design.md`.


Possession of a valid identifier is not a right to the object it names. Every lookup by a
caller-supplied ID verifies the caller's relationship to that specific record (SEC-011):

- Ownership lives in the query predicate wherever possible: fetch by ID and owner in one
  statement, so an unauthorized request finds no rows and there is no window between fetch and
  check. A post-fetch ownership check is acceptable when the predicate cannot express the rule,
  but it is centralized in the repository layer and fails closed.
- The check applies to every verb — read, update, delete, and any state transition — and to item
  endpoints as much as list endpoints. Lists filtered by owner while item routes trust the ID is
  the classic gap; the test patterns cover get, update, and delete for that reason.
- The refusal shape is a project-wide decision: 404 where the project hides resource existence,
  403 where it does not — chosen once, used consistently, and asserted exactly in tests. The
  refusal body confirms nothing about the record.

```csharp
// fail: possession of the ID is treated as a right to the invoice
var invoice = await db.Invoices.FindAsync(id);

// pass: ownership is part of the lookup — no row, no access, no timing window
var invoice = await db.Invoices
    .SingleOrDefaultAsync(i => i.Id == id && i.OwnerId == principal.UserId);
```

The cross-object control test authenticates as one principal, requests another principal's
record, and asserts the refusal shape with nothing about the record leaking — including through
error responses.
