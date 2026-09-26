# Gate 5 — `/simplify` (advisory)

The review covered the code-only branch diff `9f7470f..bb0c3f3`, from four angles run in parallel: reuse, simplification, efficiency and altitude. Every finding was vetted by the author and none was auto-applied (CLAUDE.md gate 5).

| # | Angle | Finding | Decision |
|---|---|---|---|
| S1 | simplification | `TestAuthenticationExtensions.UseTestJwtSigner` contains two near-identical `PostConfigure<JwtBearerOptions>` blocks. | **Applied.** Extracted a private helper, mirroring the production `ConfigureBearer`. |
| S2 | simplification | `TestJwtSigner.PlatformAudience` and `RealmSeedConstants.PlatformApiAudience` hold the same literal, declared independently. | **Applied.** `PlatformAudience = RealmSeedConstants.PlatformApiAudience`. |
| S3 | simplification | `AdminBypassTests` builds the Bearer client inline, while the sibling class has a `ClientWith` helper. | **Rejected.** The file already has its own consistent inline convention; cosmetic. |
| R1 | reuse | `AdminSessionEndpointDelegates.GetMe` re-implements claim mapping found in `HttpContextCurrentUser`. | **Rejected.** `HttpContextCurrentUser`/`ICurrentUser` is coupled to the tenant (team memberships via `ITenantContext`). A shared extraction reaches outside the diff (reject-by-default), and the spec fixes the name→email fallback. |
| E1 | efficiency | Two `UntilHttpRequestIsSucceeded` waits in the KeyCloak fixture run one after the other. | **Rejected.** Both realms import in the same pass, so the second poll succeeds on its first try. Negligible. |
| E2 | efficiency | `PlatformRealmLiveTokenTests` uses `[TestInitialize]`, so the host and migrations are rebuilt for each test. | **Rejected.** The `Authentication__*` env vars are process-wide and other classes change them, so resetting them per test is safer. This matches `HealthCheckEndpointTests`. |
| A1 | altitude | `SeedRolesAndSchemaAsync` is not idempotent, so four callers each carry a `DuplicateObject` guard. | **Deferred → TD-014.** It touches a shared helper and callers outside the diff; S1 kept the T5 local-guard ruling. |
| A2 | altitude | `/health/detailed` is invisible to the `EndpointRouteRules` sweep because it is mapped in `Program.cs`. | **Deferred → TD-015.** It needs a rework of the arch-test composition root; the gap is documented in `Program.cs`. |

**Correctness catches:** 0, consistent with this gate's advisory role.
