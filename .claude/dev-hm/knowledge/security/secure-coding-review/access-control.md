# Secure code review by vulnerability class — Access control

Section of `knowledge/security/secure-coding-review.md`.


Supports SEC-010, SEC-011, SEC-012. Authorization is the class where reviewers add the most
value, because the vulnerable code looks clean — it is what is absent that fails.

```python
# fail SEC-011: possession of an ID is treated as a right to the object
@app.get("/invoices/{invoice_id}")
def get_invoice(invoice_id: int, user: User = Depends(current_user)):
    return db.get(Invoice, invoice_id)
# pass: ownership verified against the authenticated principal
    inv = db.get(Invoice, invoice_id)
    if inv is None or inv.owner_id != user.id:
        raise HTTPException(404)
    return inv
```

Review method: list every added/changed route, handler, and message consumer; for each, name the
authentication mechanism (SEC-010) and the authorization check (SEC-011/012). "The frontend does
not show the button" and "the ID is a UUID nobody can guess" both fail — checks live server-side
at the handler. Watch for authorization done on list endpoints but missed on the corresponding
get/update/delete, and for background jobs and consumers that re-use request-scoped logic without
the request's identity.
