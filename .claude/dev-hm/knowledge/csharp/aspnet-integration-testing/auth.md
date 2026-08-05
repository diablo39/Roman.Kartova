# ASP.NET Core integration testing — Real authentication — no fake auth handler {#auth}

Section of `knowledge/csharp/aspnet-integration-testing.md`.


Authenticate against a real identity provider in a container, not a substitute
`AuthenticationHandler`. A test scheme bypasses the exact seam these tests exist to cover: JWT
issuer and audience validation, signing-key resolution and rotation, lifetime and clock skew,
claims transformation, and the filter-vs-binding order of the request pipeline. Those are wiring
defects, and wiring is what an integration test is for. Start Keycloak with Testcontainers, point
the app's `JwtBearer` options at the container's issuer, and acquire tokens over the real token
endpoint:

```csharp
// Fixture: real IdP + real bearer validation.
Keycloak = new KeycloakBuilder().WithRealmImportFile("test-realm.json").Build();
await Keycloak.StartAsync();

builder.UseSetting("Authentication:Authority", Keycloak.GetBaseAddress() + "realms/test");
// No ConfigureTestServices override of AddAuthentication — the production
// JwtBearer registration stays exactly as it ships.
```

```csharp
// Per-test principal: a real password-grant token for a seeded realm user.
var token = await TokenClient.GetAccessTokenAsync("alice@orga", "dev_password_12");
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
```

Rules that keep these tests honest:

- Never remove `UseAuthorization`, register a permissive fallback policy, mark the suite's
  endpoints `[AllowAnonymous]` "for testing", or swap in a test authentication scheme — each
  deletes the control the test exists to verify.
- Seed distinct realm users per role rather than minting arbitrary claim sets. A token you cannot
  obtain from the IdP is a principal the production system can never issue, so a test built on one
  proves nothing about production behavior.
- Every protected endpoint gets the negative cases as first-class tests: no token → 401, expired or
  wrong-audience token → 401, authenticated-but-unauthorized principal → 403,
  wrong-tenant/other-owner principal → 403 or 404. These are the control-verification tests the
  security oracle expects (`knowledge/security/authorization-design/verification-tests.md`).
- Boot the IdP container once per assembly alongside the database (see the fixture pattern above);
  token acquisition per test is cheap, realm startup is not.
- The cost is real — a containerized IdP adds seconds to the suite. That is the price of covering
  the seam, and it is why this tier stays separate from the fast unit loop rather than why it
  should be faked.
