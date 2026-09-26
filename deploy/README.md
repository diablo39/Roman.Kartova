# Kartova — Deployment

What lives here and the manual configuration a production environment needs beyond `helm install`.

## Layout

| Path | Purpose |
|------|---------|
| `helm/kartova/` | Helm chart — API `Deployment` + `Migrator` pre-upgrade `Job` (ADR-0085, ADR-0086). No web `Deployment` yet (served separately; see CSP note below). |
| `keycloak/kartova-realm.json` | KeyCloak realm **import** — seeds a *new*/dev realm on first boot. It does **not** retroactively update an already-existing realm (see below). |

## KeyCloak realm: import vs. running realm

`kartova-realm.json` is applied only when KeyCloak imports a realm that does not yet exist (fresh install, local docker-compose, CI). **Changing a setting in that file does not change an already-running production realm** — KeyCloak ignores the import for a realm that already exists. Any realm change therefore has to be made twice: in `kartova-realm.json` (so fresh installs get it) **and** on the live realm (UI / `kcadm` / IaC).

## Production KeyCloak token hardening (E-01.F-04.S-06a · ADR-0116)

Refresh-token rotation with reuse-revocation. Already in `kartova-realm.json` for fresh installs; **must be applied to any pre-existing production realm** or a stolen refresh token stays valid for its full lifetime.

Realm `kartova` → **Realm Settings → Tokens**:

| Setting (admin console) | Value | Realm attribute | Why |
|-------------------------|-------|-----------------|-----|
| Revoke Refresh Token | **On** | `revokeRefreshToken=true` | Refresh tokens become one-time-use — each refresh rotates to a new token and invalidates the old one. |
| Refresh Token Max Reuse | **0** | `refreshTokenMaxReuse=0` | Replaying an already-consumed refresh token revokes the whole session (reuse-detection) — a stolen refresh token self-destructs on first replay. |

Access-token lifespan is already short (`accessTokenLifespan=900`, 15 min) and needs no change.

Apply by one of:

- **Admin console:** the two toggles above, then Save.
- **`kcadm` (imperative):**
  ```
  kcadm.sh update realms/kartova -s revokeRefreshToken=true -s refreshTokenMaxReuse=0
  ```
- **Admin REST:** `PUT /admin/realms/kartova` with `{ "revokeRefreshToken": true, "refreshTokenMaxReuse": 0 }`.
- **IaC (preferred where realms are declarative):** Terraform `keycloak_realm` → `revoke_refresh_token = true`, `refresh_token_max_reuse = 0`. Persistent + auditable; keep it as the source of truth over manual toggles.

**Verify:** log in, let the SPA's silent renew run (or wait past 15 min), confirm no forced re-login; optionally confirm a replayed old refresh token is rejected. Drift on the *import* side is guarded by the arch test `KeycloakRealmSeedRules.RealmSeed_RotatesRefreshTokens_AndRevokesOnReuse`.

## Platform operator realm `kartova-platform` (ADR-0118)

Operators authenticate against a **separate** realm. `kartova-platform-realm.json` seeds it only on fresh installs, the same as the tenant realm. A running production KeyCloak needs these steps once:

1. **Create realm `kartova-platform`** with:
   - realm role `platform-admin`;
   - bearer-only client `kartova-admin-api`;
   - public PKCE client `kartova-admin-web`: redirect URIs = the web-admin origin (S2), and an audience mapper → `kartova-admin-api`.
   **Do not** create `kartova-admin-test`: it is a dev/test password-grant client.
2. **MFA:** Authentication → required actions → enable *Configure OTP* as a default action. Set the browser flow so OTP is **required**, not conditional.
3. **Token hardening parity:** access token 5 min; `revokeRefreshToken=true`; `refreshTokenMaxReuse=0`; brute-force detection on.
4. **No service accounts.** The tenant realm's `kartova-admin` service account has no role in `kartova-platform`, and no client in `kartova-platform` enables service accounts.
5. **Remove `platform-admin` from the tenant realm** (`kartova`): delete the realm role and any users holding it. The API no longer honors it there, but its presence would suggest otherwise.

   ```bash
   kcadm.sh delete roles/platform-admin -r kartova
   ```

6. **API configuration (required — the API refuses to start without the first two):**

   | Key | Value |
   |---|---|
   | `Authentication__PlatformAdmin__Authority` | `https://<kc-host>/realms/kartova-platform` |
   | `Authentication__PlatformAdmin__Audience` | `kartova-admin-api` |
   | `Authentication__PlatformAdmin__MetadataAddress` | optional; in-cluster discovery URL |

## Web container CSP origins (E-01.F-04.S-06b)

The web (nginx) container enforces a Content-Security-Policy whose cross-origin allowances (API + KeyCloak) are injected at start via the `CSP_EXTRA_ORIGINS` env var (space-separated, browser-facing origins). The Dockerfile defaults it to `""`; **every deployment must set it explicitly** or the SPA cannot reach the API/KeyCloak once CSP is enforced. There is no web `Deployment` in the Helm chart today — when one is added, it must set `CSP_EXTRA_ORIGINS` (e.g. `"https://api.<env> https://auth.<env>"`). Full guide: [csp-configuration.md](csp-configuration.md).
