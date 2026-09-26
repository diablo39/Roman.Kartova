# Gate 6 — requesting-code-review (whole branch)

- **Reviewer:** superpowers `requesting-code-review/code-reviewer.md` template, fresh subagent (opus), read-only.
- **Range:** `9f7470f..87d6b22` (merge-base with master → HEAD), 13 commits.
- **Inputs:** spec, ADR-0118, plan, SDD ledger deferred minors.
- **Verdict:** **With fixes.** No Critical findings. Two Important findings, both fixed in the gate-6 fix wave (see `dod.md`).

## Strengths (summary)
- **Isolation holds at runtime.** `PlatformAdminOnly` authenticates only with the `PlatformAdmin` scheme, so a tenant token never reaches the role check. Tenant routes stay on the default scheme. Issuer, audience and signing key all differ between the two realms.
- **Tenant code no longer honors `platform-admin`.** The tenant service account's `realm-management` rights do not reach `kartova-platform`.
- **Missing config fails closed.** A missing or blank `PlatformAdmin:*` key throws at startup.
- **Regression tests cannot pass for the wrong reason.** A GUID `sub` means a role-only regression surfaces as 200. The `invalid_token` discriminator separates a scheme rejection from the handler's 401 and from an anonymous request. Live KeyCloak tests prove the real issuer, JWKS and audience mapper.

## Important
1. **The admin-route arch rule accepted `AllowAnonymous`** (`tests/Kartova.ArchitectureTests/EndpointRouteRules.cs`).
   - A future admin endpoint with `.AllowAnonymous()` would keep the policy metadata, pass the rule, and be anonymously reachable.
   - The prefix match was also case-sensitive, while routing is case-insensitive.
   - Fix: flag `IAllowAnonymous` on admin routes and use an `OrdinalIgnoreCase` prefix.
2. **The production runbook was incomplete** (`deploy/README.md`). Five gaps:
   - no step to create operator accounts;
   - the import file was framed as prod-importable, although it holds a known-password operator, no MFA, a password-grant client and `sslRequired: none`;
   - no deploy ordering (a missing API config key means a whole-API outage);
   - the step-5 command did not match its prose;
   - no Verify section.

   The review also flagged the "bearer-only" wording, which has no toggle in the KeyCloak 26 console.

## Minor
1. ADR-0118 says `/api/v1/admin/me`; the implementation uses `/api/v1/admin/session/me`. Fixed in the fix wave.
2. `KeycloakPlatformRealmSeedRules` lacked access-token-lifespan, brute-force and password-policy parity checks. Fixed in the fix wave.
3. Spec deviation: a live-KeyCloak "tenant token carrying `platform-admin` → 401" case is impossible because the role no longer exists in the tenant realm.
   - Covered instead by the OrgAdmin → 401 + `invalid_token` live test.
   - Also covered by signer-based `AdminBypassTests` / `AdminSchemeIsolationTests`, which mint tenant tokens carrying `platform-admin`.
   - Recorded here and in `dod.md`.
4. `/health/detailed` is outside the arch rules because it is mapped in `Program.cs`. A comment was added in the fix wave; the binding is pinned by `HealthCheckEndpointTests`.
5. The `SeedRolesAndSchemaAsync` DuplicateObject guard is duplicated across Api test inits. This was an accepted ruling (T5); a helper could come later.

## Triage of SDD deferred minors
- **Must fix before merge:** only "MapAdminModule metadata could assert no `IAllowAnonymous`". It was upgraded to Important #1 and fixed in the arch rule.
- **Warning on the T3 log-noise item:** do **not** use a `ForwardDefaultSelector` by `iss`. It would authenticate operator tokens on tenant routes, turning 401 into 403 and exposing an operator principal to `AllowAnonymous` tenant endpoints.
- **Everything else:** OK to defer.

## Declined to judge
The review set these aside:
- CORS for admin routes (S2);
- OTP in the dev import;
- the `web-admin` app (S2);
- OpenAPI `securitySchemes` (not modeled anywhere in the repo);
- JWKS refresh throttling;
- a `WWW-Authenticate` header on the handler's 401 (unreachable with real KeyCloak);
- the shared test signing key (live KC covers key separation);
- dev `sslRequired: none`;
- DoD ledger bookkeeping.
