# Gate 8 — deep-review · range 9f7470f..9c5605a · 2026-09-26

Reviewed against: the slice spec (`2026-09-25-e01b-s1-platform-admin-isolation-design.md`), ADR-0118, ADR-0092, ADR-0006, ADR-0116, ADR-0090, ADR-0082, ADR-0097 and `docs/TESTING-STRATEGY.md`. The findings from gates 6 and 7, and TD-014 and TD-015, were already known and are not re-raised. I did not re-read the plan or progress files line by line: the task-by-task acceptance check was cut short by the review time budget (see the DoD note).

### Overview
The branch adds these pieces:
- the `kartova-platform` realm seed, with `platform-admin` removed from `kartova`;
- a second `JwtBearer` scheme `PlatformAdmin` and the `PlatformAdminOnly` policy (scheme + role), bound to `MapAdminModule` and `/health/detailed`;
- `GET /api/v1/admin/session/me`;
- fixture, compose and env wiring, a production runbook, and arch and seed drift rules.

The code matches spec §Components, §Data flow and §Error handling. I found no blocking defects. The remaining gaps are an ADR-0092 text that has gone stale and a few test-discrimination holes.

### Blocking-class issues
None.

### Should-fix issues

**1. ADR-0092 and ADR-0006 are not annotated with the ADR-0118 amendments, and ADR-0092 now states a role-only admin gate**
- **Evidence:** `docs/architecture/decisions/ADR-0092-rest-api-url-convention.md:52` still reads "`/api/v1/admin/*` requires `platform-admin` role". It also restricts the admin space to `/api/v1/admin/<module-slug>/`, but this slice maps the non-module slug `session` at `OrganizationAdminModule.cs:37`. `ADR-0006-keycloak-as-identity-provider.md:7` has no back-reference to ADR-0118. The only record of either amendment is the forward claim in `ADR-0118-platform-admin-isolation.md:36-37` ("Amends ADR-0006", "Amends ADR-0092") and spec line 4 ("ADR-0092 (amended), ADR-0006 (amended)").
- **Impact:** CLAUDE.md says the cited ADR is authoritative. Anyone who reads ADR-0092 by itself learns that the admin surface is gated by role alone, which is exactly the model ADR-0118 closes. That reader could add a role-only admin route, or re-derive one, and believe it complies. The `session` non-slug precedent is also recorded only in the spec (Decision 3), not in the ADR that owns URL shape. The repo already has an in-file amendment convention (for example ADR-0103, ADR-0111 and ADR-0095 carry "Amended by" notes).
- **Fix:**
  - Add an "Amended by ADR-0118 (2026-09-25)" note to ADR-0092 §Decision, next to line 52. It should say: gate = `PlatformAdminOnly`, meaning the operator-realm scheme plus the role; non-module identity groups such as `session` are allowed under `/api/v1/admin/`.
  - Add the same note to ADR-0006, covering the separate operator realm.
  - No automated test can catch this; it is a docs gate for the review. The existing `EndpointRouteRules.Every_admin_route_requires_PlatformAdminOnly` already enforces the *code* side.

### Nits
1. `docs/superpowers/specs/2026-09-25-e01b-s1-platform-admin-isolation-design.md:6`: the spec says **Status: draft**, although ADR-0118 is Accepted and the slice is implemented. Flip it to accepted or implemented.
2. The spec (line 92) names `/api/v1/catalog/applications` as the tenant route for the "platform token → tenant route 401" case. The tests use `/api/v1/organizations/me` instead (`AdminSchemeIsolationTests.cs:66-74`, `PlatformRealmLiveTokenTests.cs:96-104`, `AuthErrorTests.cs:29-41`). The substitute is equivalent but the deviation is not recorded. Note it in the spec or the ledger.
3. `AdminSessionEndpointDelegates.cs:35-39`: the invalid-`sub` branch returns 401 with no `WWW-Authenticate` header. RFC 6750 §3 expects that header on a 401. It is also the only admin 401 that cannot be told apart by the header the other isolation tests key on. Consider `Results.Challenge(authenticationSchemes: [PlatformAdminAuth.Scheme])` with the ProblemDetails body, or document that this 401 is ProblemDetails-only.
4. Stale comments now describe a tenant-realm `platform-admin` that no longer exists:
   - `tests/Kartova.SharedKernel.Identity.IntegrationTests/KeycloakAdminClientIntegrationTests.cs:176` still lists `platform-admin` among the `kartova` realm roles;
   - `SessionStartHandlerTests.cs:299-301` and `SessionStartHandler.cs:115` describe a PlatformAdmin "reaching the tenant-scoped endpoint", which ADR-0118 now makes unreachable at the scheme layer.
5. Every request carrying a platform token still runs the default (tenant) scheme in `UseAuthentication` (`Program.cs:252`) before the policy re-authenticates with `PlatformAdmin`. So each admin call logs a tenant-scheme `IDX10205`/`IDX10214` failure at Information level and validates the signature twice. This has no correctness impact. If log noise matters, consider a `ForwardDefaultSelector` or a scheme selector keyed on the token's `iss`.

### Missing tests

1. **Issuer and audience are each enforced on their own, at the real seam.** The criterion is spec §Data flow, "rejected by issuer/audience". Every fixture cross-realm token today differs in **both** `iss` and `aud`. `TestJwtSigner` also uses one RSA key for both realms (`TestJwtSigner.cs:19,28`), so at fixture level the only barrier is the issuer and audience strings. If `ValidateIssuer` alone regressed on the `PlatformAdmin` scheme (for example through a future `TokenValidationParameters` override), the audience would still reject the token, and the reverse holds too. The unit asserts at `JwtAuthenticationExtensionsTests.cs:227-228,404-405` pin the flags on the options object, not the runtime behaviour.
   - **Should exist:** `Kartova.Organization.IntegrationTests / AdminSchemeIsolationTests`:
     - `Token_with_platform_issuer_but_tenant_audience_gets_401`;
     - `Token_with_tenant_issuer_but_platform_audience_gets_401`.
   - **Needs:** a `TestJwtSigner.Build` overload exposing `issuer`/`audience`.
   - **Assertions:** both hit `/api/v1/admin/session/me` with the role `platform-admin`, and assert 401 plus `WWW-Authenticate` containing `invalid_token`.

2. **Only the dev/test client may enable the password grant in the operator realm.** The criterion is the spec §Identity row, "`directAccessGrantsEnabled` only on a dev/test client `kartova-admin-test`". `KeycloakPlatformRealmSeedRules.cs` asserts `false` for `kartova-admin-web` and `kartova-admin-api` only. A third client added later with the password grant switched on would pass.
   - **Should exist:** `Kartova.ArchitectureTests / KeycloakPlatformRealmSeedRules.Only_kartova_admin_test_enables_direct_access_grants`.
   - **Assertion:** the set of `clientId`s with `directAccessGrantsEnabled == true` equals `{ "kartova-admin-test" }`.

3. **`/health/detailed` returns 200 with a platform token at fixture level.** The live 200 case exists in `HealthCheckEndpointTests.cs:112-120`. The fixture matrix in `AdminSchemeIsolationTests.cs:96-117` covers only 401 and 403 for this route. This one is optional because the live test covers the happy path.
   - **Should exist:** `AdminSchemeIsolationTests.Platform_token_gets_200_on_health_detailed`.
   - **Assertion:** status 200 and the body contains the `entries` key.

### What looks good
- `src/Kartova.SharedKernel.AspNetCore/JwtAuthenticationExtensions.cs:295-296,323-340`: both schemes share one `ConfigureBearer`, so validation (ClockSkew 30 s, `MapInboundClaims=false`, the `RequireHttpsMetadata` default) cannot drift between realms. `PlatformAdmin` is never the default scheme, which is ADR-0118 §Decision 2 exactly.
- `src/Kartova.SharedKernel.AspNetCore/AuthorizationExtensions.cs:236-240` together with `ModuleRouteExtensions.cs:368`: the policy pins `AddAuthenticationSchemes(PlatformAdmin)`, so a tenant token never reaches the role check. `EndpointRouteRules.cs` (`Every_admin_route_combines_to_exactly_the_PlatformAdmin_scheme`) then checks the **combined** runtime policy rather than just the policy name.
- `tests/Kartova.Api.IntegrationTests/PlatformRealmLiveTokenTests.cs`: real KeyCloak proof. It checks the real issuer and audience mapper (200), cross-realm rejection in both directions, and a forged token that uses the right `iss` and `aud` but the wrong key (401 plus `invalid_token`). This meets the real-seam rule in `docs/TESTING-STRATEGY.md`.
- `src/Modules/Organization/Kartova.Organization.IntegrationTests/AdminSchemeIsolationTests.cs:41-52`: the key regression test uses a GUID `sub`, so a scheme regression cannot hide behind `GetMe`'s invalid-sub 401. Every multi-cause 401 asserts the `WWW-Authenticate` discriminator, as CLAUDE.md's "discriminative isolation tests" rule requires.
- `tests/Kartova.ArchitectureTests/KeycloakRealmSeedRules.cs:183-200` and `KeycloakPlatformRealmSeedRules.cs`: drift sentinels on both realm seeds. They check for no `platform-admin` in `kartova`, no service accounts in `kartova-platform`, PKCE S256, the audience mapper pointing at an existing bearer-only client, and hardening parity. Together they make ADR-0118 §Decision 1 enforceable.

---

**DoD note (not findings):**
- Every ledger row in `dod.md` is still ⏳. The controller fills them at close. Evidence for gates 5 and 6 is committed (`simplify.md`, `requesting-code-review.md`); gate 7 evidence (`review-pr.md`) is still **uncommitted** and must be committed before the ledger cites it.
- Gates 9 and 10 and the terminal re-verify have not run yet.
- Gate 4: `docker-compose.yml` changes only volume and env (no Dockerfile or restore surface), and the spec (lines 108–109) elects to run the `images` job anyway. Record it as run, not as N/A.
- E2E impact: `e2e/` contains no reference to `/api/v1/admin` or `platform-admin` (grep, 2026-09-26), so this is N/A. Record that in the ledger.
- Helm: `deploy/helm` carries no `Authentication*` env (grep confirmed), so it is unchanged, consistent with the spec.
- The per-plan-task acceptance check against `.superpowers/sdd/.../progress.md` was not completed within this review's time budget. The spec-level mapping above covers every spec §Components row.
