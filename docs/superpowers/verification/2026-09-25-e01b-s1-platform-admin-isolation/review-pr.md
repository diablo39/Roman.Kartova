# Gate 7 — `/pr-review-toolkit:review-pr`

- **Range:** `9f7470f..61684e0`
- **Agents:** the standing set (`code-reviewer`, `pr-test-analyzer`, `type-design-analyzer`), plus `silent-failure-hunter` because the diff adds error handling (fail-fast config throws, the invalid-`sub` 401 branch, `DuplicateObject` catches).
- **Skipped:** `comment-analyzer`, because the diff has few code comments (CLAUDE.md gate 7 conditional).

## Outcome
**0 Critical. No fail-open path in production auth.** The vetted fix wave is recorded in `dod.md` / `gate-findings.yaml`.

| Agent | Finding | Severity | Decision |
|---|---|---|---|
| code-reviewer | The arch rule checks that the policy is present but not that the combined scheme set is exactly `[PlatformAdmin]`. A route with an extra tenant-scheme `IAuthorizeData` would authenticate tenant tokens. | should-fix | **Fixed.** Added a `CombineAsync` exact-scheme arch test, and a scheme check on non-admin routes. |
| code-reviewer | The spec's `/health/detailed` "tenant token carrying `platform-admin` → 401" case is missing. | should-fix | **Fixed.** Added a fixture-level test (a live KeyCloak token can't carry the role any more). |
| pr-test-analyzer | A forged operator token (right `iss`/`aud`, wrong signing key) is untested. Rated 8. | should-fix | **Fixed.** Added a live-KeyCloak forged-token test and a fixture foreign-key signer test. |
| pr-test-analyzer | The tenant fail-fast regex `.*Authority not configured.*` also matches the PlatformAdmin message, so it could pass for the wrong reason. | should-fix | **Fixed.** Tests now assert the exact message. |
| pr-test-analyzer | The platform `RequireHttpsMetadata` default is untested. | should-fix | **Fixed.** |
| pr-test-analyzer | `PlatformAdmin:MetadataAddress` pass-through is untested. | should-fix | **Fixed.** |
| pr-test-analyzer | No role-less platform token test on `/health/detailed`. | should-fix | **Fixed.** Returns 403. |
| pr-test-analyzer | Blank PlatformAdmin Audience is untested. | nit | **Fixed.** |
| pr-test-analyzer | Expired / wrong-issuer / wrong-audience platform-token matrix. | nit | **Skipped.** Fixture validation parameters are owned by the test itself; live tests cover real validation. |
| pr-test-analyzer | The handler-level 401 has no integration test. | nit | **Skipped.** It is unit-tested. |
| pr-test-analyzer | Auth env setup is copy-pasted across 5 Api test hosts. | nit | **Skipped.** No current bug; TD candidate. |
| silent-failure-hunter | The invalid-`sub` 401 is not logged, even though it is an anomaly signal for an operator endpoint. | should-fix | **Fixed.** Logs a Warning without the raw claim value. |
| silent-failure-hunter | `PasswordGrantAsync` `EnsureSuccessStatusCode` drops the KeyCloak error body. | should-fix | **Fixed.** Now throws with authority, client, user, status and body. |
| silent-failure-hunter | A blank operator email/display name could flow into future admin audit records. | nit | **Noted for S2.** |
| silent-failure-hunter | The wait strategy is a liveness check, not an import-correctness check. | nit | **Fixed.** Comment added. |
| type-design-analyzer | The `EndpointAuth` `string[]` breaks record equality. | nit | **Skipped.** No code relies on equality. |
| type-design-analyzer | The `TestJwtSigner.Build` issuer default is undocumented. | nit | **Fixed.** Comment added. |
