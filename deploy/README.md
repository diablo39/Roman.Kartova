# Kartova — Deployment

What lives here and the manual configuration a production environment needs beyond `helm install`.

## Layout

| Path | Purpose |
|------|---------|
| `helm/kartova/` | Helm chart — API `Deployment`, `Migrator` pre-upgrade `Job` (ADR-0085, ADR-0086), and `web` + `web-admin` Deployments (ADR-0118; default enabled — see CSP note below). |
| `keycloak/kartova-realm.json` | KeyCloak realm **import** — seeds a *new*/dev realm on first boot. It does **not** retroactively update an already-existing realm (see below). |
| `keycloak/kartova-platform-realm.json` | KeyCloak platform-realm import — **dev/CI only, never import outside dev** (see ADR-0118 section below for why). |

## KeyCloak realm: import vs. running realm

`kartova-realm.json` is applied only when KeyCloak imports a realm that does not yet exist (fresh install, local docker-compose, CI). **Changing a setting in that file does not change an already-running production realm** — KeyCloak ignores the import for a realm that already exists. Any realm change therefore has to be made twice: in `kartova-realm.json` (so fresh installs get it) **and** on the live realm (UI / `kcadm` / IaC).

**Local dev applies the same rule.** The `keycloak-db` volume keeps the realm that was imported first. After ADR-0118, a volume created before it still has `platform-admin` and `platform-admin@kartova.local` in `kartova`. The API rejects those tokens by scheme (verified in gate 9 of E-01b.F-03.S-01), but the stale realm no longer matches the seed. Run `docker compose down -v` once to re-import `kartova` and `kartova-platform`. Note that `-v` also wipes the local Postgres data.

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

Operators authenticate against a **separate** realm.

**`kartova-platform-realm.json` is dev/CI only — never import it outside dev.** It contains a
known-password operator (`platform-admin@kartova.local` / `dev_password_12`), no MFA, the
password-grant client `kartova-admin-test`, and `sslRequired: none`. Production realm setup below
is manual (UI / `kcadm` / IaC), never a fresh-install import of this file.

**Order and blast radius — follow this sequence.** Step 2 (API config keys) must land *before*
step 3 (deploy): the API refuses to start without `Authentication__PlatformAdmin__Authority` /
`__Audience` — a missing/wrong key is a **whole-API outage, tenants included**, not just an
operator-surface gap. Don't jump to step 5 (removing `platform-admin` from the tenant realm) until
step 4 (verify) is green.

1. **Create realm `kartova-platform`, its clients, and operator accounts:**
   - realm role `platform-admin`;
   - bearer-only client `kartova-admin-api` (see "No bearer-only toggle" below — KeyCloak 26's
     admin console has no such checkbox);
   - public PKCE client `kartova-admin-web`: redirect URIs = the web-admin origin (S2), and an
     audience mapper → `kartova-admin-api`.
   **Do not** create `kartova-admin-test`: it is a dev/test password-grant client.
   - **MFA:** Authentication → required actions → enable *Configure OTP* as a default action. Set
     the browser flow so OTP is **required**, not conditional.
   - **Token hardening parity:** access token 5 min; `revokeRefreshToken=true`;
     `refreshTokenMaxReuse=0`; brute-force detection on.
   - **No service accounts.** The tenant realm's `kartova-admin` service account has no role in
     `kartova-platform`, and no client in `kartova-platform` enables service accounts.
   - **Operator accounts:** create one user account per operator in `kartova-platform`, grant each
     the `platform-admin` role, and have each operator enrol OTP on first login (required action
     above forces this).

2. **API configuration (required — the API refuses to start without the first two; set this
   BEFORE deploying the new image):**

   | Key | Value |
   |---|---|
   | `Authentication__PlatformAdmin__Authority` | `https://<kc-host>/realms/kartova-platform` |
   | `Authentication__PlatformAdmin__Audience` | `kartova-admin-api` |
   | `Authentication__PlatformAdmin__MetadataAddress` | optional; in-cluster discovery URL |

3. **Deploy** the image carrying ADR-0118 (PlatformAdmin scheme + `/api/v1/admin/**` +
   `/health/detailed`).

4. **Verify** (expected results):

   | Check | Expected |
   |---|---|
   | Tenant-realm token → `GET /api/v1/admin/session/me` | 401 |
   | Operator token → `GET /api/v1/admin/session/me` | 200 |
   | Operator token → `GET /api/v1/organizations/me` | 401 |

5. **Only then remove `platform-admin` from the tenant realm** (`kartova`). First find who still
   holds it and deal with those accounts — deleting the role first would silently strand them:

   ```bash
   kcadm.sh get roles/platform-admin/users -r kartova
   ```

   Remove the role from each listed user, or reassign — any operator among them gets a
   `kartova-platform` account instead (step 1). Then delete the role itself:

   ```bash
   kcadm.sh delete roles/platform-admin -r kartova
   ```

   The API no longer honors `platform-admin` on tenant tokens after ADR-0118, but its presence in
   the tenant realm would suggest otherwise.

### No bearer-only toggle (KeyCloak 26)

KeyCloak 26's admin console dropped the "Bearer-only" checkbox from client settings. Create
`kartova-admin-api` as a confidential client with **every** flow disabled — standard flow, direct
access grants, and service accounts all off — which is equivalent. Alternatively, set `bearerOnly`
directly via `kcadm`/the admin REST API:

```bash
kcadm.sh update clients/<id> -r kartova-platform -s bearerOnly=true
```

## Platform-operator console (web-admin, ADR-0118)

- **Image:** `docker build -f web/Dockerfile --build-arg APP=admin -t kartova/web-admin web` → serves `admin.html` with its own CSP (`web/admin.conf.template`). The tenant image is `--build-arg APP=web` (default). One image per app, promoted across environments — no environment-specific build args.
- **Runtime config** (TD-016, read at container start, served as `/config.js`): `KARTOVA_OIDC_AUTHORITY` (`https://<kc>/realms/kartova-platform` for the console, `…/realms/kartova` for the tenant SPA), `KARTOVA_OIDC_CLIENT_ID` (`kartova-admin-web` / `kartova-web`), `KARTOVA_API_BASE_URL` (API origin — always cross-origin for the console). Helm: `webAdmin.config.*` / `web.config.*`. Absolute URLs without quote characters; unset falls back to the SPA's localhost defaults. Like `CSP_EXTRA_ORIGINS`, a change needs only a pod restart.
- **Runtime:** `CSP_EXTRA_ORIGINS` = API + KeyCloak origins (space-separated).
- **API:** `Cors:AdminAllowedOrigins` = the console origin(s). It must be disjoint from `Cors:AllowedOrigins` and must not be `*`, or the API refuses to start. Helm: `webAdmin.origin` → `Cors__AdminAllowedOrigins__0`; `web.origin` → `Cors__AllowedOrigins__0`.
- **KeyCloak:** register the console's exact `https://<admin-origin>/callback` redirect, web origin, and post-logout URI on the `kartova-admin-web` client in `kartova-platform`.
- **Helm:** `web.enabled` / `webAdmin.enabled` (default `true`); ClusterIP services only — expose them through your ingress on **different** hosts.
- **Local:** `npm run dev:admin` (5174) or compose `web-admin` (4174). After editing `deploy/keycloak/*.json`, run `docker compose down -v`: the `keycloak-db` volume survives `down`. This also wipes local Postgres; DevSeed repopulates it. Dev operators: `platform-admin@kartova.local` and `operator-norole@kartova.local` (no role → "No access"), password `dev_password_12`.

## Web container CSP origins (E-01.F-04.S-06b)

The web (nginx) container enforces a Content-Security-Policy whose cross-origin allowances (API + KeyCloak) are injected at start via the `CSP_EXTRA_ORIGINS` env var (space-separated, browser-facing origins). The Dockerfile defaults it to `""`; **every deployment must set it explicitly** or the SPA cannot reach the API/KeyCloak once CSP is enforced. The chart renders both `web` and `web-admin` Deployments (default enabled): set `web.cspExtraOrigins` / `webAdmin.cspExtraOrigins` in `values.yaml` to the browser-facing API + KeyCloak origins (e.g. `"https://api.<env> https://auth.<env>"`) — an empty value means the enforcing CSP's `connect-src 'self'` blocks every SPA request to the API/KC. Full guide: [csp-configuration.md](csp-configuration.md).
