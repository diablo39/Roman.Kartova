# DoD Ledger — KeyCloak refresh-token rotation + reuse-revocation (E-01.F-04.S-06a)

**Slice:** `2026-09-16-keycloak-refresh-rotation` · **Branch:** `master` · **HEAD:** `<pending commit>`
**PR:** direct-to-local-master (docs/config; owner workflow) · **Last updated:** 2026-09-16
**Spec:** none — bounded slice (in-chat design, per brainstorming bounded path); ADR-0116 is the governing decision
**Plan:** none — bounded slice, no plan document

> Records the Definition of Done from `CLAUDE.md`. Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A · WAIVER.

## Scope

Interim auth hardening from ADR-0116 (defers BFF / E-01.F-04.S-05). Enable KeyCloak **refresh-token rotation** (`revokeRefreshToken=true`) + **reuse-revocation** (`refreshTokenMaxReuse=0`) so a stolen refresh token self-destructs (whole session revoked) on first replay. Access-token TTL already short (900s) — unchanged.

**Diff:** 28 lines. `deploy/keycloak/kartova-realm.json` (+2 realm flags) · `tests/Kartova.ArchitectureTests/KeycloakRealmSeedRules.cs` (+1 drift-sentinel test). No C# production-logic change. No frontend change (`oidc-client-ts` stores rotated refresh token automatically). CSP = separate slice S-06b.

**Prod action (owner, outside repo):** KeyCloak → Realm Settings → Tokens → *Revoke Refresh Token* ON, *Refresh Token Max Reuse* = 0. Realm import covers dev + fresh installs only. Documented (settings, values, UI/`kcadm`/REST/IaC application, verify): [`deploy/README.md`](../../../../deploy/README.md) → "Production KeyCloak token hardening".

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ✅ PASS | 2026-09-16 |
| 2 Per-task subagent review | ✅ PASS | 2026-09-16 |
| 3 Full suite (+ real-seam) | ✅ PASS | 2026-09-16 |
| 4 Container build (images CI) | N/A | 2026-09-16 |
| 5 `/simplify` | N/A | 2026-09-16 |
| 6 `requesting-code-review` | WAIVER (owner, 2026-09-17) | 2026-09-17 |
| 7 `review-pr` | WAIVER (owner, 2026-09-17) | 2026-09-17 |
| 8 `deep-review` | ✅ PASS | 2026-09-16 |
| Terminal re-verify (build + suite) | ✅ PASS | 2026-09-16 |
| 9 Visual / API verification (ADR-0084) | ⏳ PENDING (owner — running stack) | — |
| 10 CI green on PR (`ci-local.sh`) | ⏳ PENDING (owner) | — |

## Gate detail

### 1 — Build (`TreatWarningsAsErrors=true`)
**Status:** ✅ PASS — `dotnet build Kartova.slnx -c Debug -p:TreatWarningsAsErrors=true` → `Build succeeded. 0 Warning(s) 0 Error(s)`.

### 2 — Per-task subagent review
**Status:** ✅ PASS — `csharp-code-reviewer` on the diff: **clean, no findings**. Confirmed `revokeRefreshToken`/`refreshTokenMaxReuse` are genuine top-level `RealmRepresentation` fields (camelCase, matching siblings); `=true` + `=0` = single-use refresh + revoke-on-replay (ADR-0116 intent); test reads correct JSON path/types/values, fails loudly on drift (flag removed / flipped false / reuse raised >0).

### 3 — Full test suite (+ real-seam)
**Status:** ✅ PASS
**Evidence:**
- Architecture: `dotnet test Kartova.ArchitectureTests` → 71/71 (incl. new `RealmSeed_RotatesRefreshTokens_AndRevokesOnReuse`; confirmed RED before the realm edit, GREEN after — TDD).
- **Real-seam:** `dotnet test Kartova.SharedKernel.Identity.IntegrationTests` → 8/8 against **real KeyCloak booted from the edited realm JSON** — confirms token issuance/admin flows unaffected by the rotation flags.

### 4 — Container build (images CI job)
**Status:** N/A — diff touches no `Dockerfile`, no `COPY`/`ADD` build input, no restore surface (`*.csproj` / `Directory.Packages.props` / `nuget.config`). Per CLAUDE.md gate-4 N/A rule.

### 5 — `/simplify`
**Status:** N/A — no business logic to simplify; diff = 2 declarative JSON flags + one assertion-only arch test.

### 6 — `requesting-code-review`
**Status:** WAIVER (owner, 2026-09-17) — no-logic config slice; gate 2 (`csharp-code-reviewer`) + gate 8 (`deep-review`) already lensed the full 28-line diff clean. Waiver, not green.

### 7 — `review-pr`
**Status:** WAIVER (owner, 2026-09-17) — standing set (type-design / pr-test / code-reviewer) has near-zero surface on a declarative config + assertion diff (no new types, no error handling). Waiver, not green.

### 8 — `deep-review`
**Status:** ✅ PASS — reviewed the full diff against ADR-0116 intent. No Blocking / Should-fix. Notes: (a) rotation flags are realm-level (correct — they are not per-client in the KeyCloak realm representation); (b) `refreshTokenMaxReuse=0` is the strict setting (any reuse → session revoked); (c) drift sentinel asserts both, catching silent relaxation. Nits: none actionable.

### Terminal re-verify (build + full suite)
**Status:** ✅ PASS — no code-mutating gate (5–8) applied changes; build + arch + real-seam already green on the final diff.

### 9 — Visual / API verification (running system)
**Status:** ⏳ PENDING (owner) — requires re-importing the realm into a running KeyCloak and driving the SPA (browser MCP unavailable this session). Verify: log in, let `automaticSilentRenew` fire (>15 min or forced), confirm no forced re-login (rotation transparent); optionally confirm a replayed old refresh token is rejected. Not blocking the config/test correctness already proven in gates 1/3/8.

### 10 — CI green on PR (`ci-local.sh` = pre-push mirror)
**Status:** ⏳ PENDING (owner) — owner direct-merges to local master (not pushed); no PR runner. If pushed, `scripts/ci-local.sh` is the pre-push mirror.
