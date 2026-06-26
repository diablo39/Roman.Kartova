# Team-Admin Membership Authority — Docker Compose HTTP Evidence (ADR-0101, DoD #5)

Satisfies CLAUDE.md DoD #5: `docker compose up` + real HTTP happy-path + at least one
negative-path captured for the `feat/team-admin-membership-authority` branch.

## Context

| Field | Value |
|-------|-------|
| Branch | `feat/team-admin-membership-authority` |
| Verified at git HEAD | `f4386aa` ("fix(team-admin): wrap DevSeed RLS-disable in try so FORCE is always restored") |
| Wall-clock start (stack up) | 2026-06-09 17:34:xx UTC (`docker compose down -v && docker compose up -d --build`) |
| Wall-clock start (HTTP tests) | 2026-06-09 17:38:xx UTC |
| Wall-clock end (HTTP tests) | 2026-06-09 17:39:xx UTC |
| Total wall-clock (build + HTTP) | ~6 min (build ~2 min, KC import + warmup ~70 s, HTTP suite ~1 min) |
| API base | `http://localhost:8080` |
| Keycloak base | `http://localhost:8180` (host) / `http://keycloak:8080` (in-cluster) |
| Token A user | `team-admin@orga.kartova.local` / `dev_password_12` (realm `Member`, Admin of Demo Team) |
| Token B user | `admin@orga.kartova.local` / `dev_password_12` (OrgAdmin) |
| Token client | `kartova-api` (public, `directAccessGrantsEnabled: true`) — password grant |

### What this branch changed (ADR-0101)

Team-admin authority is now a per-team `Admin` membership via the `TeamAdminOfThis`
resource gate. The `TeamAdmin` realm role and all `team.*` mutation claims were removed.
The dev seed now seeds a **Demo Team**
(`id = dddddddd-0001-0001-0001-000000000001`, tenant Org A
`11111111-1111-1111-1111-111111111111`) and makes `team-admin@orga` an **Admin** member
of it. The realm seed re-assigned `team-admin@orga` to the realm role `Member` and
pinned its KeyCloak user `id = aaaabbbb-0001-0001-0001-000000000001` (so the JWT `sub`
matches the seeded `team_members.user_id`).

### Volume wipe requirement

Because the realm JSON changed (removed the `TeamAdmin` role, pinned the KC user ID),
`docker compose down -v` was run before `up --build` to force KeyCloak to re-import
the realm from scratch. KeyCloak's import strategy is `IGNORE_EXISTING` — a stale
`keycloak-db` volume would retain the OLD realm and the pinned ID / role removal would
NOT take effect.

## Compose stack snapshot

```
NAME                      IMAGE                            SERVICE       STATUS
romangig2-api-1           kartova/api:dev                  api           Up 3 minutes
romangig2-keycloak-1      quay.io/keycloak/keycloak:26.1   keycloak      Up 4 minutes (healthy)
romangig2-keycloak-db-1   postgres:18-alpine               keycloak-db   Up 4 minutes (healthy)
romangig2-migrator-1      kartova/migrator:dev             migrator      Exited (0) 4 minutes ago
romangig2-postgres-1      postgres:18-alpine               postgres      Up 4 minutes (healthy)
```

## Migrator seed confirmation

```
migrator-1  |       Applying migrations for module 'organization'...
migrator-1  |       Applying migration '20260423080230_InitialOrganization'.
migrator-1  |       Applying migration '20260526081916_AddTeamsTable'.
migrator-1  |       Applying migration '20260526081952_AddTeamMembersTable'.
migrator-1  |       Applying migration '20260526142414_AddTeamMembersForeignKeyCascade'.
migrator-1  |       Applying migration '20260527182222_EnablePgTrgmExtension'.
migrator-1  |       Applying migration '20260527182257_AddOrganizationProfileColumns'.
migrator-1  |       Applying migration '20260527182349_AddUsersTable'.
migrator-1  |       Applying migration '20260527182445_AddInvitationsTable'.
migrator-1  |       Applying migration '20260529173745_MakeInvitationsPendingIndexUnique'.
migrator-1  |       Applying migration '20260531200125_AddUsersTenantEmailUnique'.
migrator-1  |       Applying migration '20260601142121_AddInvitationTokenColumns'.
migrator-1  |       Dev seed: demo team inserted.
migrator-1  |       Dev seed: demo team Admin membership for team-admin@orga inserted.
```

Both seed log lines confirm: the Demo Team row exists and `team-admin@orga` holds the
`Admin` membership on it. Migrator exit code: **0**.

## Pre-scenario: session bootstrap

Both users required a `POST /api/v1/auth/session` call to upsert their `users` rows
before tenant-scoped endpoints could resolve their identity. This is by design
(the post-auth hook was replaced by the `SessionStartHandler` in a prior slice).

### Token A session bootstrap — team-admin@orga.kartova.local

```
POST /api/v1/auth/session
Authorization: Bearer <TOKEN_A>

HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8

{
  "me": {
    "id": "aaaabbbb-0001-0001-0001-000000000001",
    "displayName": "Tanya TeamAdmin",
    "email": "team-admin@orga.kartova.local"
  },
  "role": "Member",
  "permissions": [
    "catalog.read","catalog.applications.register","catalog.applications.edit-metadata",
    "catalog.applications.lifecycle.forward","team.read","org.profile.read",
    "org.users.read","org.users.search"
  ],
  "teams": [
    {"teamId": "dddddddd-0001-0001-0001-000000000001", "role": "Admin"}
  ],
  "organization": {
    "id": "11111111-1111-1111-1111-111111111111",
    "displayName": "Org A",
    ...
  },
  "acceptedInvitation": null
}
```

Key observations:
- `role: "Member"` — the `TeamAdmin` realm role is confirmed absent.
- `teams: [{"teamId": "dddddddd-...", "role": "Admin"}]` — Admin membership on the Demo Team is wired.
- `id: "aaaabbbb-0001-0001-0001-000000000001"` — pinned KC user ID matches the seeded `team_members.user_id`.
- No `team.*` mutation claims in the permissions list — purged by ADR-0101.

### Token B session bootstrap — admin@orga.kartova.local

```
POST /api/v1/auth/session
Authorization: Bearer <TOKEN_B>

HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8

{
  "me": {
    "id": "e031a14a-4b6b-4b36-8cf1-ee2be2940852",
    "displayName": "Alice Admin",
    "email": "admin@orga.kartova.local"
  },
  "role": "OrgAdmin",
  "permissions": [
    "catalog.read","catalog.applications.register","catalog.applications.edit-metadata",
    "catalog.applications.lifecycle.forward","catalog.applications.lifecycle.reverse",
    "team.read","team.create","org.profile.read","org.profile.edit",
    "org.invitations.read","org.invitations.create","org.invitations.revoke",
    "org.users.read","org.users.search"
  ],
  "teams": [],
  "organization": { "id": "11111111-1111-1111-1111-111111111111", "displayName": "Org A", ... },
  "acceptedInvitation": null
}
```

## Token fetch

Tokens retrieved via password grant:

```bash
# Token A
curl -s -X POST http://localhost:8180/realms/kartova/protocol/openid-connect/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=password&client_id=kartova-api&username=team-admin@orga.kartova.local&password=dev_password_12&scope=openid"
# → HTTP 200, access_token length: 1326 chars, JWT sub: aaaabbbb-0001-0001-0001-000000000001

# Token B
curl -s -X POST http://localhost:8180/realms/kartova/protocol/openid-connect/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=password&client_id=kartova-api&username=admin@orga.kartova.local&password=dev_password_12&scope=openid"
# → HTTP 200, access_token length: 1305 chars
```

Note: the slice-9 verification doc used `dev_pass`; the actual password in the realm JSON
is `dev_password_12`. Updated here to match the realm.

---

## Scenarios

### Scenario 1 — HAPPY: realm-Member who is team Admin manages their team → 200

This is the exact case that returned `403` before ADR-0101 — a realm-`Member`
user attempting to update a team for which they hold the `Admin` membership.

```bash
curl -i -X PUT http://localhost:8080/api/v1/organizations/teams/dddddddd-0001-0001-0001-000000000001 \
  -H "Authorization: Bearer <TOKEN_A>" \
  -H "Content-Type: application/json" \
  --data '{"displayName":"Demo Team","description":"verified via ADR-0101 evidence"}'
```

```
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
Date: Tue, 09 Jun 2026 17:38:47 GMT
Server: Kestrel
Transfer-Encoding: chunked

{
  "id": "dddddddd-0001-0001-0001-000000000001",
  "displayName": "Demo Team",
  "description": "verified via ADR-0101 evidence",
  "createdAt": "2026-06-09T17:35:21.329802+00:00"
}
```

**Expected:** 200  
**Actual:** 200  
**Verdict: PASS** — A realm-`Member` holding the per-team `Admin` role can update
their team. The `TeamAdminOfThis` resource gate fires correctly based on membership,
not on the removed `TeamAdmin` realm role.

---

### Scenario 2 — NEGATIVE: same user, a team they do NOT admin → 403

#### Step 2a: OrgAdmin creates "Other Team"

```bash
curl -i -X POST http://localhost:8080/api/v1/organizations/teams \
  -H "Authorization: Bearer <TOKEN_B>" \
  -H "Content-Type: application/json" \
  --data '{"displayName":"Other Team","description":"not team-admin'\''s"}'
```

```
HTTP/1.1 201 Created
Content-Type: application/json; charset=utf-8
Location: /api/v1/organizations/teams/dad1dcf7-b3e6-485e-a13d-c6af9af3038f

{
  "id": "dad1dcf7-b3e6-485e-a13d-c6af9af3038f",
  "displayName": "Other Team",
  "description": "not team-admin's",
  "createdAt": "2026-06-09T17:39:17.7470142+00:00"
}
```

Other Team ID: `dad1dcf7-b3e6-485e-a13d-c6af9af3038f`. `team-admin@orga` has no membership
on this team.

#### Step 2b: team-admin tries to update Other Team

```bash
curl -i -X PUT http://localhost:8080/api/v1/organizations/teams/dad1dcf7-b3e6-485e-a13d-c6af9af3038f \
  -H "Authorization: Bearer <TOKEN_A>" \
  -H "Content-Type: application/json" \
  --data '{"displayName":"Hijack","description":"should fail"}'
```

```
HTTP/1.1 403 Forbidden
Content-Length: 0
Date: Tue, 09 Jun 2026 17:39:26 GMT
Server: Kestrel
```

**Expected:** 403  
**Actual:** 403  
**Verdict: PASS** — The `TeamAdminOfThis` gate correctly denies the update because
`team-admin@orga` is not an Admin member of `Other Team`. The authority is properly
scoped per-team, not globally.

---

### Scenario 3 — INVITATION: inviting with the removed role TeamAdmin → 422

#### Step 3a: OrgAdmin invites with role "TeamAdmin" (now invalid)

```bash
curl -i -X POST http://localhost:8080/api/v1/organizations/invitations \
  -H "Authorization: Bearer <TOKEN_B>" \
  -H "Content-Type: application/json" \
  --data '{"email":"x-adr0101@example.com","role":"TeamAdmin"}'
```

```
HTTP/1.1 422 Unprocessable Entity
Content-Type: application/problem+json
Date: Tue, 09 Jun 2026 17:39:36 GMT
Server: Kestrel
Transfer-Encoding: chunked

{
  "type": "https://kartova.io/problems/validation-failed",
  "title": "Invalid invitation request",
  "status": 422,
  "detail": "Email must be a non-empty, <=320-character string containing '@', and role must be one of: Viewer, Member, OrgAdmin.",
  "traceId": "00-d67d178f8dfadc366c770d13ed30744d-3232d695b599fb8d-00"
}
```

**Expected:** 422 with `validation-failed` problem-type  
**Actual:** 422 with `validation-failed` — detail explicitly lists the valid roles
(`Viewer, Member, OrgAdmin`), confirming `TeamAdmin` is absent from the allowed set.  
**Verdict: PASS**

#### Step 3b: Contrast — same endpoint with valid role "Member" → 201

```bash
curl -i -X POST http://localhost:8080/api/v1/organizations/invitations \
  -H "Authorization: Bearer <TOKEN_B>" \
  -H "Content-Type: application/json" \
  --data '{"email":"x-adr0101-valid@example.com","role":"Member"}'
```

```
HTTP/1.1 201 Created
Content-Type: application/json; charset=utf-8
Location: /api/v1/organizations/invitations/58fb7f51-211a-4a5d-8e1f-f40733f274bb

{
  "invitation": {
    "id": "58fb7f51-211a-4a5d-8e1f-f40733f274bb",
    "email": "x-adr0101-valid@example.com",
    "role": "Member",
    "invitedAt": "2026-06-09T17:39:47.2449036+00:00",
    "expiresAt": "2026-06-16T17:39:47.2449036+00:00",
    "status": "Pending",
    "invitedByUserId": "e031a14a-4b6b-4b36-8cf1-ee2be2940852",
    "acceptedAt": null,
    "revokedAt": null
  },
  "inviteUrl": "http://localhost:5173/accept-invitation?token=18gmUrq9AVVIfMz21_RPZJhB6iPv5lWBHwqqRjF-Nk8"
}
```

**Expected:** 201  
**Actual:** 201 — valid invitation created, proper token-based `inviteUrl` (the H4 API-1
placeholder fix carried through).  
**Verdict: PASS** (contrast case)

---

## Summary table

| # | Scenario | Expected | Actual | Verdict |
|---|----------|----------|--------|---------|
| Bootstrap A | Session start for `team-admin@orga` | 200 + `role=Member` + `teams=[{Admin}]` | 200 + `role=Member` + `teams=[{dddddddd-..., Admin}]` | PASS |
| Bootstrap B | Session start for `admin@orga` | 200 + `role=OrgAdmin` | 200 + `role=OrgAdmin` | PASS |
| 1 | Team Admin (realm-Member) updates their team | 200 | 200 + updated body | **PASS** |
| 2a | OrgAdmin creates Other Team | 201 | 201 + `id=dad1dcf7-...` | PASS (setup) |
| 2b | realm-Member non-admin tries to update Other Team | 403 | 403 | **PASS** |
| 3a | Invite with role `TeamAdmin` (removed) | 422 + `validation-failed` | 422 + `validation-failed` + valid-roles list | **PASS** |
| 3b | Invite with role `Member` (valid, contrast) | 201 | 201 + invitation + token URL | PASS |

**All 3 ADR-0101 proof scenarios matched expected status codes. No deviations.**

## Deviations and notes

| Topic | Note |
|-------|-------|
| Password in slice-9 doc | The slice-9 verification doc recorded `dev_pass`; the actual realm JSON credential is `dev_password_12`. Updated here. |
| Session bootstrap required | Both users needed `POST /api/v1/auth/session` before tenant-scoped endpoints could resolve their identity. This is by design — the `SessionStartHandler` upserts the `users` row on first login. |
| `docker compose ps` shows api as "Up" but not "(healthy)" | The API container does not expose a healthcheck probe in the compose file; "Up" without "(healthy)" is normal for this service. All endpoints responded correctly. |
| Volumes after teardown | `docker compose down` (no `-v`) — volumes preserved for next session reuse. |

## Teardown

After capture:

```
docker compose down
```

Volumes preserved (keycloak-db + postgres-data). The next session can reuse them
unless the realm JSON changes again (in that case, `down -v` + `up --build` is
required as documented above).
