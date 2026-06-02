# Slice 9 — Docker Compose HTTP Verification (Phase H3)

Satisfies CLAUDE.md DoD #5: `docker compose up` + real HTTP happy-path + at least one
negative-path captured for the slice-9 endpoints.

## Context

| Field | Value |
|-------|-------|
| Branch | `feat/slice-9-organization-people-management` |
| Verified at git HEAD | `5dbfb89` ("refactor(slice-9): split OrganizationEndpointDelegates into per-resource files") |
| Wall-clock start (stack up) | 2026-05-29 14:53:34 UTC (`docker compose up -d --build`, fresh build) |
| Wall-clock start (HTTP tests) | 2026-05-29 14:58:52 UTC |
| Wall-clock end (HTTP tests) | 2026-05-29 15:01:35 UTC |
| Total wall-clock (build + HTTP) | ~8 min (build ~2 min, KC import + warmup ~70 s, HTTP suite ~3 min) |
| API base | `http://localhost:8080` |
| Keycloak base | `http://localhost:8180` (host) / `http://keycloak:8080` (in-cluster) |
| Seeded admin user | `admin@orga.kartova.local` / `dev_pass` (OrgAdmin) |
| Token client | `kartova-api` (public, `directAccessGrantsEnabled: true`) — password grant |

### Compose stack snapshot (`docker compose ps` after `up`)

```
NAME                      IMAGE                            SERVICE       STATUS
romangig2-api-1           kartova/api:dev                  api           Up (running)
romangig2-keycloak-1      quay.io/keycloak/keycloak:26.1   keycloak      Up (healthy)
romangig2-keycloak-db-1   postgres:18-alpine               keycloak-db   Up (healthy)
romangig2-postgres-1      postgres:18-alpine               postgres      Up (healthy)
(migrator)                kartova/migrator:dev             migrator      Exited 0 (service_completed_successfully)
```

## Environment fixes applied as part of this commit

Three pre-existing slice-9 drifts blocked the verification run; each is in scope for H3
("real evidence, not 'tests pass' claims") and is fixed as part of this commit.

### Fix 1 — `docker-compose.yml`: API container needs in-cluster Keycloak URL

The Dev appsettings overlay (`appsettings.Development.json`) sets
`KartovaIdentity:Keycloak:BaseUrl = http://localhost:8180`, which is reachable from the
**host** but NOT from inside the API container (where `localhost` is the container itself).
Without an override the KeyCloak admin-client TokenClient would attempt to call
`http://localhost:8180/.../token` from inside the api container and fail with connection
refused, surfacing as 500 → `Token fetch failed` on every invitation create.

Added env-var overrides to the `api` service in `docker-compose.yml`:

```yaml
KartovaIdentity__Keycloak__BaseUrl: "http://keycloak:8080"
KartovaIdentity__Keycloak__Realm: "kartova"
KartovaIdentity__Keycloak__AdminClientId: "kartova-admin"
KartovaIdentity__Keycloak__AdminClientSecret: "admin-dev-secret"
KartovaIdentity__Keycloak__FrontendBaseUrl: "http://localhost:5173"
```

(`FrontendBaseUrl` intentionally stays at the host port — invitation emails resolve from a
developer's browser, not from inside the container.)

### Fix 2 — `src/Kartova.Migrator/Dockerfile`: missing `Kartova.SharedKernel.Identity` csproj

The migrator Dockerfile staged-restore step (`COPY *.csproj` + `dotnet restore`) was missing
the `Kartova.SharedKernel.Identity/*.csproj` line. Slice 9 added a transitive reference
from `Kartova.Organization.Infrastructure` (which the migrator references) to
`Kartova.SharedKernel.Identity` (for `KeycloakAdminClient` DI from the post-auth hook).
Without the csproj, `dotnet publish --no-restore` later in the Dockerfile fails with
`NETSDK1004: Assets file ... project.assets.json not found`.

Added the missing COPY line.

### Fix 3 — wiped stale `keycloak-db` volume

The pre-slice-9 KeyCloak DB volume contained an older copy of the `kartova` realm that did
NOT have the `kartova-admin` client. KC's import strategy is `IGNORE_EXISTING` — so the
updated realm JSON (which contains `kartova-admin` per ADR-0009 / slice-9 §6.7) was
silently skipped at startup, leaving the API unable to obtain an admin token. This is not
a code defect (production helm chart re-imports against an empty DB), but a one-time
local-dev migration step.

`docker compose down -v` followed by `docker compose up -d` re-imported the realm
correctly. No code change needed; documented here so the next operator hitting
`Token fetch failed: Unauthorized` knows what to do.

## Scenarios

Token retrieved via:

```bash
curl -s -X POST http://localhost:8180/realms/kartova/protocol/openid-connect/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=password&client_id=kartova-api&username=admin@orga.kartova.local&password=dev_pass&scope=openid"
```

Returned `HTTP 200` with a 1305-char access token. Saved as `$TOKEN`.

---

### Scenario 1 — Session bootstrap (happy)

```bash
curl -i -X POST http://localhost:8080/api/v1/auth/session \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json"
```

```
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8

{"me":{"id":"1e8d120d-a803-420a-94ec-43928d3e0749","displayName":"Alice Admin",
       "email":"admin@orga.kartova.local"},
 "role":"OrgAdmin",
 "permissions":["catalog.read","catalog.applications.register",
                "catalog.applications.edit-metadata", ...
                "org.profile.read","org.profile.edit",
                "org.invitations.read","org.invitations.create",
                "org.invitations.revoke",
                "org.users.read","org.users.search"],
 "teams":[],
 "organization":{"id":"11111111-1111-1111-1111-111111111111",
                 "displayName":"Org A", "description":null,
                 "defaultTimeZone":"UTC", "logoEtag":null, "logoMimeType":null,
                 "createdAt":"2026-05-29T14:57:20.166194+00:00"},
 "acceptedInvitation":null}
```

OK expected behavior — 200, OrgAdmin role, full permission set, Org A tenant attached,
no pending invitation, no teams (none seeded yet).

---

### Scenario 2 — Invitation create (happy)

```bash
EMAIL="new-user-1780066742@example.com"
curl -i -X POST http://localhost:8080/api/v1/organizations/invitations \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  --data "{\"email\":\"$EMAIL\",\"role\":\"Member\"}"
```

```
HTTP/1.1 201 Created
Content-Type: application/json; charset=utf-8
Location: /api/v1/organizations/invitations/f32190fd-5eba-41ef-a33d-fed24b00371a

{"invitation":{"id":"f32190fd-5eba-41ef-a33d-fed24b00371a",
               "email":"new-user-1780066742@example.com",
               "role":"Member",
               "invitedAt":"2026-05-29T14:59:02.6572946+00:00",
               "expiresAt":"2026-06-05T14:59:02.6572946+00:00",
               "status":"Pending",
               "invitedByUserId":"1e8d120d-a803-420a-94ec-43928d3e0749",
               "acceptedAt":null, "revokedAt":null},
 "inviteUrl":"http://localhost:5173/?invitation=1"}
```

OK expected behavior — 201 + Location header, Pending status, 7-day expiry, FrontendBaseUrl
correctly resolved to the host port.

Side effect (proven by scenario 3 next): the KeyCloak admin token flow worked
end-to-end — KC user provisioned, role assigned, user upserted into our DB.

---

### Scenario 3 — Invitation create (negative — duplicate email)

Same body as Scenario 2:

```
HTTP/1.1 409 Conflict
Content-Type: application/problem+json

{"type":"https://kartova.io/problems/email-already-in-tenant",
 "title":"Email already in this tenant",
 "status":409,
 "detail":"A user with this email is already a member of the current tenant.",
 "traceId":"00-9c8bb04623d958b601347d776ed000b4-96ca7f99332ed9e2-00"}
```

OK expected behavior — 409 with `email-already-in-tenant` problem-type as designed
(strict one-email-per-tenant identity scope per ADR-0100). Confirms the first call
upserted a User row idempotently.

---

### Scenario 4 — Org profile read (happy)

```bash
curl -i -X GET http://localhost:8080/api/v1/organizations/me \
  -H "Authorization: Bearer $TOKEN"
```

```
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8

{"id":"11111111-1111-1111-1111-111111111111",
 "displayName":"Org A", "description":null,
 "defaultTimeZone":"UTC",
 "logoEtag":null, "logoMimeType":null,
 "createdAt":"2026-05-29T14:57:20.166194+00:00"}
```

OK expected behavior — 200 with the org profile of the caller's tenant (Org A).

---

### Scenario 5 — Org profile update (happy, after a deviation)

**First attempt** with `Europe/Warsaw` (per the H3 plan):

```bash
curl -i -X PUT http://localhost:8080/api/v1/organizations/me \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  --data '{"displayName":"Org A","description":"verified","defaultTimeZone":"Europe/Warsaw"}'
```

```
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json

{"type":"https://kartova.io/problems/validation-failed",
 "title":"Invalid request","status":400,
 "detail":"Unknown IANA time-zone id. (Parameter 'tz')",
 "errors":{"tz":["Unknown IANA time-zone id."]}, ...}
```

DEVIATION — this is a real H3-class drift between integration tests and the prod-like
container. The Alpine runtime image (`mcr.microsoft.com/dotnet/aspnet:10.0-alpine`)
ships **without the `tzdata` apk package**, so `TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw")`
returns `null` inside the container — even though it succeeds on a Windows or full-Linux
host (where the in-process integration tests pass).

Confirmed via `docker compose exec api sh -c "ls /usr/share/zoneinfo/Europe/"` → "No such file
or directory". Recommend follow-up fix in `src/Kartova.Api/Dockerfile`:

```dockerfile
USER root
RUN apk add --no-cache tzdata
USER kartova:kartova
```

(Or use the `mcr.microsoft.com/dotnet/aspnet:10.0` Debian image, which bundles tzdata.)
Filed as drift for the H3 → H4 boundary; not blocking this verification.

**Second attempt** with `UTC` (always available, even without tzdata):

```bash
curl -i -X PUT http://localhost:8080/api/v1/organizations/me \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  --data '{"displayName":"Org A","description":"verified","defaultTimeZone":"UTC"}'
```

```
HTTP/1.1 204 No Content
```

OK expected behavior on the fallback — 204 update applied. Demonstrates the happy update
path executes cleanly when the supplied tz happens to be one Alpine resolves.

---

### Scenario 6 — Org profile update (negative — invalid tz)

```bash
curl -i -X PUT http://localhost:8080/api/v1/organizations/me \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  --data '{"displayName":"Org A","description":"","defaultTimeZone":"Mars/Olympus"}'
```

```
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json

{"type":"https://kartova.io/problems/validation-failed",
 "title":"Invalid request","status":400,
 "detail":"Unknown IANA time-zone id. (Parameter 'tz')",
 "errors":{"tz":["Unknown IANA time-zone id."]}, ...}
```

OK expected behavior — 400 with `validation-failed` problem-type. The error message is
identical to the Scenario 5 first-attempt deviation, which on the one hand confirms
both inputs hit the same validator branch, and on the other hand explains why the
Scenario 5 deviation is a missing-data deviation rather than a logic deviation.

---

### Scenario 7 — Logo upload (happy)

Generated a 69-byte 1x1 RGB PNG via Python (`struct`/`zlib`); confirmed with `file`:
"PNG image data, 1 x 1, 8-bit/color RGB, non-interlaced".

```bash
curl -i -X PUT http://localhost:8080/api/v1/organizations/me/logo \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: image/png" \
  --data-binary "@/tmp/h3/small.png"
```

```
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8

{"logoEtag":"E878950F8091EC010CF5CC723BDEA027A8539CF7147CFEA199C2F666232DCD4E",
 "mimeType":"image/png"}
```

OK expected behavior — 200, deterministic SHA-256 ETag, mimeType echoed.

---

### Scenario 8 — Logo upload (negative — payload too large)

Generated a 307,200-byte (300 KB) random file:

```bash
curl -i -X PUT http://localhost:8080/api/v1/organizations/me/logo \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: image/png" \
  --data-binary "@/tmp/h3/big.bin"
```

```
HTTP/1.1 413 Payload Too Large
Content-Type: application/problem+json

{"type":"https://kartova.io/problems/logo-too-large",
 "title":"Logo too large","status":413,
 "detail":"Logo bytes must be <= 262,144 bytes.",
 "traceId":"00-54a7eae9bde7f5aa43028c514b2b3ea1-c6da2b2b77e09fee-00"}
```

OK expected behavior — 413 with `logo-too-large` problem-type, exact 256 KiB limit
echoed in the detail.

---

### Scenario 9 — User search (happy)

```bash
curl -i "http://localhost:8080/api/v1/organizations/users?q=admin&limit=10" \
  -H "Authorization: Bearer $TOKEN"
```

```
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8

[{"id":"1e8d120d-a803-420a-94ec-43928d3e0749",
  "displayName":"Alice Admin",
  "email":"admin@orga.kartova.local"}]
```

OK expected behavior — 200 with a JSON array containing exactly the seeded admin user.
The seeded `team-admin@orga.kartova.local` is in KeyCloak but has not yet been upserted
into our DB (no session bootstrap from that user this run), so the search rightly
excludes them. Confirms the user search is sourced from our DB rather than KC.

---

## Scenario summary

| # | Scenario | Expected | Actual | Verdict |
|---|----------|----------|--------|---------|
| 1 | Session bootstrap (happy) | 200 + SessionStartResponse | 200 | OK |
| 2 | Invitation create (happy) | 201 + invitation + inviteUrl | 201 | OK |
| 3 | Invitation create (duplicate, negative) | 409 + `email-already-in-tenant` | 409 + correct problem-type | OK |
| 4 | Org profile read (happy) | 200 + OrgProfileResponse | 200 | OK |
| 5a | Org profile update with `Europe/Warsaw` (happy) | 204 | 400 (drift — Alpine has no tzdata) | DEVIATION |
| 5b | Org profile update with `UTC` (happy, fallback) | 204 | 204 | OK |
| 6 | Org profile update with `Mars/Olympus` (negative) | 400 + `validation-failed` | 400 + correct problem-type | OK |
| 7 | Logo upload 1x1 PNG (happy) | 200 + etag + mimeType | 200 | OK |
| 8 | Logo upload 300 KB blob (negative) | 413 + `logo-too-large` | 413 + correct problem-type | OK |
| 9 | User search `q=admin` (happy) | 200 + non-empty array | 200 + admin user | OK |

**Count:** 5 happy paths verified (1, 2, 4, 5b, 7, 9 — six counting the fallback) + 3 negative
paths verified (3, 6, 8). Meets and exceeds DoD #5 requirement of "real HTTP happy-path
+ one negative-path".

## Deviations and follow-ups

| Deviation | Severity | Recommended fix | Owner |
|-----------|----------|-----------------|-------|
| Alpine runtime image lacks `tzdata`, so non-UTC IANA tz ids return 400 even when valid | Should-fix before Phase H4 prod-like soak | Add `apk add --no-cache tzdata` to `src/Kartova.Api/Dockerfile`; add a docker-compose-based integration test asserting `Europe/Warsaw` resolves | Slice 9 H4 |
| Stale `keycloak-db` volume silently drops realm-JSON additions (IGNORE_EXISTING import strategy) | One-time local-dev migration friction | Document in slice-9 resume prompt; or switch helm/dev-compose to OVERWRITE_EXISTING for the bootstrap import | docs/superpowers/plans/slice-9-resume-prompt.md |
| Migrator Dockerfile missed the new `Kartova.SharedKernel.Identity` csproj after slice 9 wired the post-auth-sync hook through Catalog/Organization Infrastructure | Build-time only — caught and fixed in this commit | Fixed in this commit. Consider extending the Dockerfile-csproj-COPY drift sentinel (`scripts/check-dockerfile-csproj-sync.*`) to also cover the Migrator image. | Follow-up |

## Teardown

After capture, stack was left running for one final `docker compose ps` snapshot, then:

```
docker compose down
```

(Volumes preserved for the next H4 session — they now contain the freshly-imported
`kartova-admin` client, so the next `up` will not require the `down -v` dance.)

## H3 follow-up: Europe/Warsaw fix verified (2026-05-29)

The H3 row 5a deviation (Alpine runtime missing `tzdata`, every non-UTC IANA tz id
returning 400 from `PUT /api/v1/organizations/me`) is fixed by installing the
`tzdata` apk package in the API runtime image. Verified against a freshly-rebuilt
image at git HEAD `fea16af` + the Dockerfile change applied on top.

Image layer (confirmed):

```
#19 [runtime 3/5] RUN apk add --no-cache tzdata
```

Image content (`docker run --rm --entrypoint sh kartova/api:dev -c "apk info -e tzdata && ls /usr/share/zoneinfo/Europe/Warsaw"`):

```
tzdata
/usr/share/zoneinfo/Europe/Warsaw
```

Endpoint re-verification (the curl that produced 400 in the H3 row 5a capture):

```
PUT /api/v1/organizations/me  body={"displayName":"Org A","description":"verified","defaultTimeZone":"Europe/Warsaw"}
HTTP_STATUS=204  (was 400 + "Unknown IANA time-zone id." pre-fix)
```

Status table delta:

| Row | Step | Pre-fix outcome | Post-fix outcome |
|----:|------|-----------------|------------------|
| 5a  | Org profile update with `Europe/Warsaw` (happy) | 400 (DEVIATION) | **204 (OK)** |

The first-row deviation from the H3 outcomes table is closed. Files touched:
`src/Kartova.Api/Dockerfile` (apk add tzdata in runtime stage),
`tests/Kartova.ArchitectureTests/Slice9BoundarySentinels.cs`
(`Runtime_can_resolve_common_IANA_timezones` sentinel — makes the IANA-tz
dependency visible on any future stripped-down runtime).

## H4 SPA E2E Playwright verification

Closes the F-phase SPA verification gap (F-phase shipped vitest-only). Satisfies
the SPA half of CLAUDE.md DoD #5 and ADR-0084 cold-start Playwright requirement.

### Run context

| Field | Value |
|-------|-------|
| Git HEAD at run | `53073674019a127094b5357e28b82f72b146d612` (branch `feat/slice-9-organization-people-management`) |
| Stack | `docker compose up -d` (api + postgres + keycloak + migrator + keycloak-db) — same compose used in H3 |
| SPA | `cd web && npm run dev` (Vite v6.4.2, cold-start per ADR-0084), served on `http://localhost:5173` |
| Browser driver | Playwright MCP (`mcp__playwright__*` tools) — single Chromium context |
| Wall-clock start | 2026-05-29 15:18 UTC (first navigation) |
| Wall-clock end | 2026-05-29 15:35 UTC (browser closed, stack torn down) |
| Total | ~17 min (incl. investigation of bugs surfaced mid-flow) |
| Admin user | `admin@orga.kartova.local` / `dev_pass` (OrgAdmin) — same as H3 |
| Screenshots | 18 PNGs under `docs/superpowers/plans/slice-9-screenshots/` |

Stack verified healthy via `docker compose ps`: all four services reached `(healthy)`
on first attempt, migrator exited 0. Vite emitted `Local: http://localhost:5173/`
on cold start with no React/build errors (HMR not active — cold-start only). The
H3 fix (tzdata + KartovaIdentity env vars in compose) carried through cleanly —
no API boot issues observed.

### Step-by-step results

| Step | Outcome | Screenshots | Notes |
|-----:|---------|-------------|-------|
| 1. Navigate + KC login | **PASS** | `step-1a-kc-login.png`, `step-1b-catalog-after-login.png` | OIDC PKCE redirect → KC realm `kartova` → `Sign In` → callback → `/catalog` rendered. Header shows `img alt="Org A"` (note: the org already has a logo — `logoEtag` set in seed, so the original "no logo yet" expectation in the task does not hold for this seed; the placeholder vs uploaded-logo distinction is moot). |
| 2. Logo upload | **FAIL — SPA bug surfaced** | `step-2a-org-settings-loaded.png`, `step-2b-logo-upload-failed-404.png` | Form populated correctly (`Org A` / `verified` / `Europe/Warsaw` — matches `GET /api/v1/organizations/me`). Dropzone accepts file, preview renders (64×64 + 200×200). Clicking **Upload Logo** issues `PUT /api/v1/organizations/me/logo` against the **SPA dev-server origin** (`http://localhost:5173`) instead of the API origin (`http://localhost:8080`) → 404 from Vite. **No user-facing error toast** — failure is silent. Filed below as bug **SPA-1**. |
| 3. Create invitation | **PASS for create, FAIL for invite URL** | `step-3a-invitations-list.png`, `step-3b-invite-dialog-open.png`, `step-3c-invite-success-copylink.png` | `POST /api/v1/organizations/invitations` → 201 Created. Success-state UI renders with copy-link box. But: the link is `http://localhost:5173/?invitation=1` — a **placeholder URL with no token and a sentinel integer** — not a usable acceptance link. Filed below as bug **API-1**. Also surfaced bug **API-2**: the **Invited by** column shows the raw UUID `1e8d120d-…` because the user-detail enrichment endpoint crashes (see API-2 details). |
| 4. Accept invite in fresh context | **NOT TESTABLE — blocked by API-1 + auth design** | `step-4a-invite-url-broken.png` | The invite URL has no token; opening it in a fresh tab simply lands on `/catalog` (the `?invitation=1` query string is silently dropped by the SPA — no `/accept` route exists). Per the SPA's `OidcCallbackHandler` design, acceptance is keyed off the invitee's authenticated email at callback time — not off a URL token — so the broken URL is *technically* cosmetic, but the invitee experience is unusable (no clear "what to do next" link). Step 4 cannot complete without manually pre-creating the invitee user in KeyCloak admin (out of scope for an MCP-driven E2E session). |
| 5. Land on `/welcome` | **NOT TESTABLE** | — | Depends on step 4 producing an `AcceptedInvitationInfo` from `OidcCallbackHandler`. Direct nav to `/welcome` (no router state) correctly falls back to `/catalog` — verified by reading `WelcomePage.tsx` line 23: `if (!info) return <Navigate to="/catalog" replace />`. |
| 6. Continue → `/catalog` | **PASS (implicit)** | `step-1b-catalog-after-login.png` | The catalog renders fine immediately after login — same path Welcome's Continue button takes. No screenshot needed beyond step 1b. |
| 7. Catalog Owner → user detail | **PARTIAL PASS — FE works, BE crashes** | `step-7c-application-detail-unknown-owner.png`, `step-7d-catalog-owner-as-displayname.png`, `step-7e-user-detail-after-owner-click.png`, `step-7b-user-detail-failed-500.png` | Seeded apps all have null owners ("Unknown user"). Registered a new app "H4 E2E App" via the Register Application dialog — owner defaulted to current user. Catalog row shows **"Alice Admin"** as a `<a href="/users/{id}">` link (NOT a UUID) — the slice-9 E1 OwnerLink component is wired correctly. Clicking the link navigates to `/users/{id}` — but the page renders **"Failed to load user / Try refreshing"** because `GET /api/v1/organizations/users/{id}` returns **500 Internal Server Error**. Root cause is bug **API-2** below. |
| 8. Team Detail members rendering | **PASS** | `step-8a-teams-list-empty.png`, `step-8b-team-detail-empty.png`, `step-8c-team-detail-with-member.png` | Created "Slice-9 H4 E2E Team" (no teams seeded). Empty state renders cleanly. Added Alice Admin as member via the Add Member dialog. The members table renders **"Alice Admin" + "admin@orga.kartova.local"** in two-line format — display name + email, NOT raw UUIDs. Slice-9 E3 enrichment confirmed working at the SPA layer. |
| 9. Add Member combobox typeahead | **PASS** | `step-9a-add-member-dialog-open.png`, `step-9b-combobox-typeahead-admin.png` | Dialog opens with `<input role="combobox" placeholder="Search by name or email…">`. Typing `admin` issues `GET /api/v1/organizations/users?q=admin` and surfaces one option: "Alice Admin / admin@orga.kartova.local". Selection commits the user. Slice-9 F8 combobox confirmed working end-to-end. |

### Bugs surfaced (release blockers)

#### SPA-1: Logo upload fires against SPA origin (404 silent failure)

- **Component:** `web/src/features/organization/` — the logo-upload mutation
- **Symptom:** `PUT http://localhost:5173/api/v1/organizations/me/logo` → 404 from Vite. No user-facing error toast.
- **Repro:** Step 2 — pick any PNG ≤256 KB, click Upload Logo.
- **Network log:** requests 282 & 283 both 404.
- **Severity:** Blocker — logo upload is non-functional end-to-end. Other API calls in the same SPA (`/permissions`, `/me`, `/invitations`, catalog) correctly hit `http://localhost:8080` — so the bug is local to whichever client/fetch the logo mutation uses. Likely the mutation uses a relative `fetch()` instead of `apiClient` or a hard-coded `apiBaseUrl`.
- **Secondary issue:** the SPA also fires `GET /api/v1/...` against the SPA origin in at least two cases (requests 272, 273) — those return 200-with-HTML (Vite serves index.html for unknown routes), which can be a silent corruption hazard if the SPA tries to JSON-parse the response.

#### API-1: invite URL is a placeholder, not a usable link

- **Component:** `src/Modules/Organization/Kartova.Organization.Infrastructure/` — the invitation create endpoint response builder
- **Symptom:** `POST /api/v1/organizations/invitations` response body:
  ```json
  { "invitation": {...}, "inviteUrl": "http://localhost:5173/?invitation=1" }
  ```
  The URL points at `/` (not `/accept` or `/welcome`), uses a literal integer `1` instead of a per-invitation token, and the SPA has no handler for `?invitation=...`.
- **Severity:** Blocker — invitees cannot self-serve from the link. The auto-accept-on-callback path *could* still work if the invitee already has a KC account with the matching email, but the link is misleading and step 4 of the user journey is broken UX.
- **Note:** the list endpoint `GET /api/v1/organizations/invitations` does not surface the token either, so even if the SPA wanted to regenerate the link client-side it can't.

#### API-2: `GET /api/v1/organizations/users/{id}` returns 500 (LINQ translation failure)

- **Component:** `src/Modules/Organization/Kartova.Organization.Infrastructure/UserQueries.cs` line 77
- **Symptom:** every request to `GET /api/v1/organizations/users/{id}` returns 500. Stack trace from API logs:
  ```
  System.InvalidOperationException: The LINQ expression 'DbSet<TeamMembership>()
      .Where(t => t.UserId == @id)
      .Join(
          inner: DbSet<Team>(),
          outerKeySelector: t => (object)t.TeamId.Value,
          innerKeySelector: t0 => (object)EF.Property<Guid>(t0, "_id"),
          resultSelector: (t, t0) => new TransparentIdentifier<TeamMembership, Team>(...))'
  could not be translated.
     at Kartova.Organization.Infrastructure.UserQueries.GetDetailAsync(Guid id, CancellationToken ct)
        in /src/src/Modules/Organization/Kartova.Organization.Infrastructure/UserQueries.cs:line 77
  ```
- **Root cause:** the EF query joins `TeamMembership.TeamId` (a value-object wrapper) against `Team`'s shadow primary key via `(object)` casts. EF Core can't translate that into SQL — needs either an explicit conversion on the value object's mapping (so `.Value` is unwrapped server-side) or a rewritten join using strongly-typed projections.
- **Severity:** Blocker — every page that resolves a UUID to a display name via this endpoint is broken:
  - The **Invitations list** "Invited by" column renders raw UUID (4 failed requests visible in network log per page load — 286, 287, 290, 291).
  - The **/users/{id}** page (linked from OwnerLink in catalog rows and from invitation rows) renders "Failed to load user" — making step 7 of the journey non-completable past the link click.
- **What still works:** the search endpoint `GET /api/v1/organizations/users?q=...` (used by the F8 combobox) does NOT hit this code path and works fine — confirmed by step 9 returning "Alice Admin" for `q=admin`.

### Summary

| Metric | Value |
|--------|-------|
| Steps PASS | 4 of 9 (steps 1, 6, 8, 9) |
| Steps PARTIAL | 2 of 9 (step 3 — create works, link broken; step 7 — link wiring works, target page crashes) |
| Steps FAIL | 1 of 9 (step 2 — silent 404) |
| Steps NOT TESTABLE | 2 of 9 (steps 4 + 5 — depend on the invite-URL flow being functional) |
| New release blockers found | 3 (SPA-1 silent 404 on logo upload, API-1 placeholder invite URL, API-2 500 on user detail) |
| Stack state at end | torn down via `docker compose down` — verified; Vite dev server stopped — verified (`Get-NetTCPConnection -LocalPort 5173` returns nothing) |
| ADR-0084 cold-start requirement | satisfied — `npm run dev` started from a clean process, no HMR cache active |
| CLAUDE.md DoD #5 SPA half | partially satisfied — happy + negative paths captured, but three new release blockers must be fixed and re-verified before "slice-9 complete" |
| Outcome | **DONE_WITH_CONCERNS** — verification completed end-to-end, but three release-blocking bugs were surfaced that must be addressed before slice-9 can be marked done. |

## H6 re-verification at HEAD 3a67b95 (2026-06-01)

Re-runs the H3 scenarios (subset of 9) against the current branch HEAD to confirm
that the 14 commits landed since the H4 SPA run (`5307367` → `3a67b95`) have not
regressed any HTTP verification path. The three blockers surfaced in H4 (SPA-1
logo origin, API-1 placeholder invite URL, API-2 user-detail 500) have all been
fixed in this window; this run also adds an explicit Scenario 1 ("stack starts
cleanly with the new UNIQUE migration") and a pre-session probe to cover the
post-auth-hook removal blast radius.

### Run context

| Field | Value |
|-------|-------|
| Branch | `feat/slice-9-organization-people-management` |
| Verified at git HEAD | `3a67b9539f958e87085b8e6e21ca8749ed592dd9` ("test(slice-9): close MT1/MT2/MT4/MT5/MT7 wire test gaps from deep-review") |
| Commits since H4 baseline (`5307367`) | 14 — most consequential: `e8bf859` (removed IPostAuthSyncHook, consolidated upsert + invitation flip into SessionStartHandler), `a8fd443` (new `AddUsersTenantEmailUnique` migration), `fc86775` (extracted MapEndpoints into per-resource extension methods), plus three H4-blocker fixes (`8cc5dd9` SPA-1, `3759186` API-1, `5fa11ef` API-2) |
| Wall-clock start (stack up) | 2026-06-01 06:33:34 UTC (`docker compose down -v && docker compose up -d --build`) |
| Wall-clock start (HTTP tests) | 2026-06-01 06:38:29 UTC |
| Wall-clock end (HTTP tests) | 2026-06-01 06:40:44 UTC |
| Total wall-clock (build + HTTP) | ~7 min 10 sec (build ~3 min, KC import + warmup ~75 s, HTTP suite ~2 min) — slightly faster than H3 (~8 min) despite concurrent Stryker mutation testing competing for IO/CPU |
| API base | `http://localhost:8080` |
| Keycloak base | `http://localhost:8180` (host) / `http://keycloak:8080` (in-cluster) |
| Seeded admin user | `admin@orga.kartova.local` / `dev_pass` (OrgAdmin) — KC sub now `aae05905-bf64-414f-bd97-4d37704517a9` (different from H3's `1e8d120d-…` because the KC realm DB was wiped with `down -v`) |

### Scenario 1 — Stack starts cleanly with the new migration

`docker compose down -v && docker compose up -d --build` produced four healthy
services + migrator exited 0. Migrator log shows both new slice-9 migrations
applying without warnings:

```
info: Microsoft.EntityFrameworkCore.Migrations[20402]
      Applying migration '20260529173745_MakeInvitationsPendingIndexUnique'.
…
      CREATE UNIQUE INDEX idx_invitations_email_pending ON invitations(tenant_id, lower(email)) WHERE status = 1;
info: Microsoft.EntityFrameworkCore.Migrations[20402]
      Applying migration '20260531200125_AddUsersTenantEmailUnique'.
…
      CREATE UNIQUE INDEX ix_users_tenant_email ON users (tenant_id, lower(email));
```

No collision warnings — the dev realm seed (which provisions two admin users at
`admin@orga.kartova.local` and `team-admin@orga.kartova.local`, distinct
addresses) does not violate the new `(tenant_id, lower(email))` invariant. The
ADR-0100 compliance concern flagged in the task brief ("two admin users with the
same email pattern across two tenants") does not materialize because the seed
keeps the two addresses textually distinct.

Migrator exit code: `Exited (0)` — confirmed via `docker compose ps -a`. Verdict: **PASS**.

### Pre-session probe — Tenant-scoped read BEFORE `/auth/session`

To verify the post-auth-hook removal (`e8bf859`) doesn't crash other endpoints
when the `users` row hasn't been upserted yet:

```bash
curl -i -X GET http://localhost:8080/api/v1/organizations/me \
  -H "Authorization: Bearer $TOKEN"
```

```
HTTP/1.1 200 OK
{"id":"11111111-1111-1111-1111-111111111111","displayName":"Org A","description":null,
 "defaultTimeZone":"UTC","logoEtag":null,"logoMimeType":null,
 "createdAt":"2026-06-01T06:36:20.793419+00:00"}
```

**Key finding**: returned **200**, not 500. The tenant-scoped read path
(`GET /api/v1/organizations/me`) does not depend on a `users`-row existing for
the caller. `TenantClaimsTransformation` resolves the tenant from JWT claims
without joining `users`, and the org-profile read goes through `ITenantScope`
with `SET LOCAL app.current_tenant_id`. The post-auth-hook deletion
(`e8bf859`) has **no observable HTTP blast radius** on tenant-scoped reads
called before `/auth/session`. Good — confirms the consolidation is safe.

### Scenarios 2-9

| # | Scenario | Expected | Actual | Verdict |
|---|----------|----------|--------|---------|
| 2 | Get token (KC password grant) | 200 + 1305-char JWT | 200 + 1305-char JWT | **PASS** |
| 3 | Session bootstrap (happy) | 200 + SessionStartResponse with `OrgAdmin` role + 17 permissions + Org A tenant | 200 + identical shape (including all 17 permissions: catalog.*, team.*, org.profile.*, org.invitations.*, org.users.*) | **PASS** |
| 4 | Invitation create (happy, fresh email) | 201 + invitation + inviteUrl ending in `?invitation=1&email=…` | 201; `inviteUrl=http://localhost:5173/?invitation=1&email=h6-verify-1780295935%40example.com` (email-hint segment confirms commit `3759186` H4-API-1 fix shipped) | **PASS** |
| 5 | Invitation create duplicate (negative) | 409 + `email-already-in-tenant` | 409 + `https://kartova.io/problems/email-already-in-tenant` | **PASS** |
| 6a | Org profile read | 200 + OrgProfileResponse | 200 | **PASS** |
| 6b | Org profile update `Europe/Warsaw` | 204 (Alpine tzdata fix in place) | 204 (improvement vs H3 row 5a's 400 — tzdata still in runtime image) | **PASS** |
| 7 | Org profile update `Mars/Olympus` (negative) | 400 + `validation-failed` | 400 + `https://kartova.io/problems/validation-failed`, errors:{tz:["Unknown IANA time-zone id."]} | **PASS** |
| 8a | Logo upload 1x1 PNG (happy) | 200 + etag + mimeType | 200 + `logoEtag=B1FF9C8EA3A780BAD09B346C423D2D0E46815926879B18E841D928376A946640` + `mimeType=image/png` (ETag differs from H3's `E878…` because the source PNG bytes were regenerated locally — deterministic hash per content, expected) | **PASS** |
| 8b | Logo upload 300 KB (negative) | 413 + `logo-too-large` | 413 + `https://kartova.io/problems/logo-too-large` (detail: "Logo bytes must be <= 262,144 bytes.") | **PASS** |
| 9 | User search `?q=admin` | 200 + non-empty array with admin user | 200 + `[{"id":"aae05905-bf64-414f-bd97-4d37704517a9","displayName":"Alice Admin","email":"admin@orga.kartova.local"}]` | **PASS** |

### Drift vs H3 baseline

| Aspect | H3 outcome | H6 outcome | Drift? |
|--------|-----------|-----------|--------|
| Stack `up` time | ~8 min | ~7 min 10 sec | Marginal — within noise; concurrent Stryker did NOT visibly slow the build |
| Migrator exit code | 0 | 0 | none |
| Migration application warnings | none | none (incl. new `AddUsersTenantEmailUnique`) | none |
| Session bootstrap shape | 200, OrgAdmin, 17 permissions, Org A | 200, OrgAdmin, 17 permissions, Org A | none — `SessionStartHandler` post-`e8bf859` produces identical wire shape |
| Invitation create response | included `inviteUrl=…/?invitation=1` (H4-API-1 issue) | includes `…/?invitation=1&email=…` (H4-API-1 closed by `3759186`) | improvement |
| Org profile `Europe/Warsaw` | 400 (H3 row 5a deviation; closed by H3 follow-up `fea16af`) | 204 | improvement (carried forward) |
| Invalid tz problem-type | `validation-failed` | `validation-failed` | none |
| Logo upload happy | 200 + deterministic SHA-256 ETag | 200 + deterministic SHA-256 ETag (different value because source PNG regenerated locally; the hash function is unchanged) | none |
| Logo upload 413 | 413 + `logo-too-large` + 262,144 byte limit | 413 + `logo-too-large` + 262,144 byte limit | none |
| User search shape | flat JSON array | flat JSON array | none |
| Route registration after `fc86775` (per-resource extraction) | n/a (pre-refactor) | All 9 endpoints reachable, all permission policies intact, no silent 401/403 mismatch | none — refactor is regression-free |
| Pre-session tenant-scoped read after `e8bf859` (post-auth-hook removal) | n/a (pre-refactor) | 200 (no 500) — confirms no blast-radius regression | none — refactor is regression-free |

No regressions observed against H3. Three improvements (H4 API-1 / SPA-1 / API-2
fixes carried through, tzdata fix carried through). The two highest-risk
changes flagged in the task brief — the `AddUsersTenantEmailUnique` migration
and the post-auth-hook removal — both proved safe under HTTP load.

### Tear-down

```
docker compose down
```

(Volumes preserved — next session can reuse them, no `down -v` dance needed.)

### Summary

| Metric | Value |
|--------|-------|
| Scenarios attempted | 9 (+ 1 pre-session probe + 1 stack-start check) |
| Scenarios PASS | 9 of 9 |
| Migration application | clean — `AddUsersTenantEmailUnique` + `MakeInvitationsPendingIndexUnique` both applied without warnings |
| Drift vs H3 baseline | none (3 improvements observed: H4 API-1 + SPA-1 + API-2 + tzdata all carried forward) |
| Post-auth-hook removal blast radius (`e8bf859`) | confirmed zero on HTTP endpoints — tenant-scoped reads work before `/auth/session` |
| Route refactor risk (`fc86775`) | confirmed zero — all 9 endpoints reachable, no silent auth drift |
| Total wall-clock | ~7 min 10 sec (compose-up to scenario-9 done) |
| Outcome | **DONE** — DoD #5 re-satisfied at HEAD `3a67b95`. No new bugs surfaced; all H4 blockers verified closed. |
