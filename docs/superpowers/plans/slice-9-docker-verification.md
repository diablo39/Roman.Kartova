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
