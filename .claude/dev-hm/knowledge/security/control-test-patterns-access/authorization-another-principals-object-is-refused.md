# Control-test patterns: transport, authentication, session, authorization — Authorization: another principal's object is refused

Section of `knowledge/security/control-test-patterns-access.md`.


Our handlers verify ownership against the authenticated principal, so a well-formed request for
a record the caller does not own is refused — with a shape that does not confirm existence
where the project hides it.

```csharp
[TestMethod]
public async Task GetInvoice_OwnedByAnotherPrincipal_IsRefused()
{
    var client = _factory.AuthenticatedAs(UserA);
    var response = await client.GetAsync($"/api/invoices/{InvoiceOfUserB}");
    Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    Assert.DoesNotContain(InvoiceNumberOfUserB,
        await response.Content.ReadAsStringAsync());   // nothing about the record leaks
}
```

Run the same pattern for get, update, and delete — ownership checks present on list endpoints
and missing on the item endpoints is the classic gap. In multi-tenant code, the fixture holds
two tenants, and every test over tenant-owned data asserts the other tenant's rows never appear
in any response — including error responses.
