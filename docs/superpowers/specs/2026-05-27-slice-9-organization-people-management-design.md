# Slice 9 — Organization & people management (design)

**Date:** 2026-05-27
**Author:** Roman Głogowski (AI-assisted)
**Status:** Draft, pending user review
**Stories closed:** E-03.F-01.S-01 (org profile), E-03.F-01.S-02 (invite users with roles), E-03.F-01.S-03 (user display + `/users/{id}` page), E-03.F-01.S-04 (user search typeahead).
**ADRs created in this slice:** ADR-0099 (distributed locking + leader-elected periodic tasks), ADR-0100 (identity scope — strict one-email-per-tenant).
**ADRs referenced:** ADR-0004 (S3 blob storage — deferred for slice 9), ADR-0006 (KeyCloak as IdP), ADR-0011 (one Org = one tenant), ADR-0028/0080 (Wolverine — durability remains deferred), ADR-0082 (modular monolith), ADR-0090 (tenant scope), ADR-0091 (ProblemDetails), ADR-0092 (REST URL convention), ADR-0095 (cursor pagination), ADR-0096 (ETag/If-Match optimistic concurrency), ADR-0098 (UUID-only entity identifier).

---

## 1. Why this slice

Slice 8 (PR #26) shipped Team Management — admins can create teams, assign Applications to them, and add/remove members. But the `AddMemberDialog` takes a raw `Guid` as input: a dead-end UX. And the only way to "create" a new tenant member today is to seed them manually in `kartova-realm.json`. There is no production path from "OrgAdmin wants to onboard Alice" to "Alice is a member of the tenant" — slice 9 closes that gap.

This slice ships:

1. **KeyCloak Admin API integration** (`Kartova.SharedKernel.Identity`) — first piece of infrastructure for write operations against KeyCloak; reusable by every future module.
2. **`IUserDirectory` cross-module abstraction** — local `users` projection table fed from JWT claims on every authenticated request; consumed by Catalog (Application owner enrichment) + Team (member rendering) + Organization (user detail page).
3. **`Invitation` aggregate** — Organization module owns the invitation lifecycle (Pending → Accepted/Revoked/Expired) with 7-day expiry, three-way duplicate detection, and **no email infrastructure** (OrgAdmin gets a copy-link URL to share manually until E-06a lands).
4. **Distributed-locking shared infrastructure** (`IDistributedLock`, `PostgresAdvisoryLock`, `LeaderElectedPeriodicService`) — first usage is the invitation-expiry sweep; reusable by every future periodic task across the codebase.
5. **Org profile editing** (`Organization` aggregate extended with `Description`, `OrgLogo` value object, `DefaultTimeZone`) — logo stored as `bytea` directly on the row (256 KB cap, PNG/JPEG/SVG with sanitization); MinIO deferred to the GDPR-export slice.
6. **Session bootstrap endpoint** (`POST /api/v1/auth/session`) — explicit post-login moment; carries `AcceptedInvitation` payload for the new welcome screen.
7. **SPA surfaces** — `/settings/organization`, `/settings/invitations`, `/users/:id`, `/welcome`; `<UserSearchCombobox>` replaces slice 8's UUID input in `AddMemberDialog`; `<OwnerLink>` renders display names on catalog list/detail.
8. **Two new ADRs** — ADR-0099 (distributed locking strategy) and ADR-0100 (strict one-email-per-tenant scope).

Estimated size: ~10 working days. Larger than slice 7/8 (~5-6 each) because four stories close at once and three pieces of cross-cutting shared infrastructure land. The infrastructure investment compounds — E-06a, E-08, E-10, E-15 (and more) all consume what slice 9 ships.

## 2. Context

- Slices 0–8 merged. Slice 8 (Team Management, PR #26) merged 2026-05-22.
- `Kartova.Organization` module today carries `Organization` aggregate + `Team` aggregate + `TeamMembership`. E-03 is its bounded context; slice 9 adds two more aggregates: `Invitation` and `User` (projection only).
- `Kartova.SharedKernel.AspNetCore` hosts `TenantClaimsTransformation` (slice 7), `ITenantContext` (slice 8 extended), `KartovaPermissions` policy registration. Slice 9 extends `TenantClaimsTransformation` further to upsert the `users` projection.
- `Kartova.SharedKernel.Postgres` exists with connection-string helpers + the BYPASSRLS pool (slice 5 + slice 8 use it for admin-side reads/writes that need to bypass tenant RLS).
- Wolverine is configured as **in-process mediator only**; persistence is explicitly deferred (Program.cs:147-152 + ADR-0080). Slice 9 honors this deferral and uses Postgres advisory locks instead of Wolverine scheduled messages.
- ADR-0011 declares one Org = one tenant. ADR-0006 declares KeyCloak as the IdP. The realm `kartova` is shared across all tenants; per-user `tenantId` attribute scopes them.
- No SMTP/email infrastructure exists anywhere; KeyCloak's `executeActionsEmail` is *not* used. Invitations surface a copy-link URL.
- No KeyCloak Admin API client exists today — slice 9 builds it.
- KeyCloak's realm setting `duplicateEmailsAllowed: false` (default) is in force; slice 9 keeps it. See §11 / ADR-0100.

## 3. Decisions

| # | Decision | Rationale |
|---|---|---|
| 1 | **Slice 9 closes all four E-03.F-01 stories in one bundle** (S-01 + S-02 + S-03 + S-04). | They share the KeyCloak Admin client + the `IUserDirectory` infrastructure; splitting would mean re-introducing the same plumbing twice. User-confirmed full scope. |
| 2 | **KeyCloak Admin API client lives in a new project `Kartova.SharedKernel.Identity`** — not inside the Organization module. | Reusable by every future module that needs identity operations (E-06a notifications, E-14 billing, E-15 agent). Mirrors how `ITenantScope` lives in shared. |
| 3 | **`IUserDirectory` (cross-module read abstraction for user display info) lives in `Kartova.SharedKernel.Identity`** alongside `IKeycloakAdminClient`. Implementation `OrganizationUserDirectory` lives in `Kartova.Organization.Infrastructure` and reads from the Organization-owned `users` projection table. | Same pattern slice 8 set with `ITeamMembershipReader`. Catalog (and others) consume the interface only — no cross-module DB references; ADR-0082 honored. |
| 4 | **No email infrastructure built in slice 9.** Invitation flow returns a `copyInviteUrl` to the OrgAdmin. SPA renders a copy-link dialog with messaging *"Share this link with `<email>`. Expires in 7 days."* Real email lands when E-06a Notification infrastructure does. | Avoids duplicating notification plumbing ahead of E-06a. Aligns with the project's deferral pattern (MinIO, durable Wolverine). Strictly time-limited friction for OrgAdmins in MVP scope. |
| 5 | **Organization logo stored as `bytea` on the `organizations` row,** with 256 KB cap, allowed mime-types PNG/JPEG/SVG, SVG sanitized server-side via `Ganss.Xss`. | MinIO is the strategic backend (ADR-0004) but the next slice that actually needs it is the GDPR-export slice — logo is the *smallest* possible blob use case, the wrong place to introduce the abstraction. Deferred per user's explicit ask. |
| 6 | **User identity resolution uses a local `users` projection table** synced from JWT claims on every authenticated request. KeyCloak Admin API is touched only on write operations (invitation create/revoke + role assign) and on the invitation-expiry sweep. | KeyCloak Admin API has no "get users by id list" endpoint — N+1 to KeyCloak per page render is unacceptable. JWT claims (`sub`, `email`, `given_name`, `family_name`) carry everything we need; sync is idempotent + cheap. |
| 7 | **Invitations are owned by us, not KeyCloak.** Our `invitations` table records lifecycle (Pending/Accepted/Revoked/Expired), expires-at, invited-by; KeyCloak account is created at the moment of invitation but its `UPDATE_PASSWORD` required-action is what gates first-time login. We do NOT use KeyCloak action tokens with TTL. | KeyCloak's action-token TTL is realm-level; we want per-invitation expiry. Decoupling the lifecycle gives us audit + listing + revocation independent of KeyCloak's token model. |
| 8 | **Identity scope is strict: one email = one tenant in Kartova.** Realm setting `duplicateEmailsAllowed: false` (KeyCloak default) preserved. Cross-tenant duplicates surface as a third 409 error type (`email-already-on-platform`) — see §6.2. | Permissive mode would require an org-picker UX (Slack-style) that we have zero stories for. Multi-tenant-per-email, if ever required, is best served via realm-per-tenant — a much bigger decision; deferred to ADR-0100. |
| 9 | **Distributed-safe periodic work uses Postgres advisory locks**, not Wolverine durable scheduled messages. New shared infrastructure: `IDistributedLock` (in `Kartova.SharedKernel`), `PostgresAdvisoryLock` (in `Kartova.SharedKernel.Postgres`), `LeaderElectedPeriodicService` base class (in `Kartova.SharedKernel`). | Wolverine persistence is explicitly deferred per Program.cs:147-152 + ADR-0080. Advisory locks are session-scoped, auto-released on connection drop, multi-instance safe. Future periodic jobs (E-06a retry, E-08 re-scan, E-10 scorecard recompute, etc.) reuse the same primitives. Captured as ADR-0099. |
| 10 | **Session bootstrap endpoint `POST /api/v1/auth/session`** is the explicit post-login moment. Returns rich payload (user, permissions, teams, org profile, optional `AcceptedInvitation` for welcome UX). | Without it, invitation acceptance happens as a side effect on the first-fired API call (ambiguous), and there's no SPA-deterministic "I just logged in" moment for welcome UX. The endpoint also single-shots app hydration — no second fetches on cold start. |
| 11 | **OrgUsersRead permission is granted to every role (Viewer included).** | Owner display names appear on catalog list/detail; Viewers need to see them. Cross-tenant safety is via RLS, not permission gating. |
| 12 | **`<OwnerLink>` receives embedded `UserDisplayInfo` as a prop** — no per-row `useUser(id)` fetch. ApplicationResponse + TeamMemberResponse extended to include owner/member display info; backend handlers batch-fetch via `IUserDirectory.GetManyAsync(ids)`. | Eliminates N+1 client-side fetches; one DB query per page; ~1.6 KB payload growth for 20 rows is negligible. |
| 13 | **`Organization` profile fields landing in slice 9: DisplayName + Description + Logo + DefaultTimeZone.** | BrandColor (E-12), DefaultLocale (no i18n consumer), data residency (E-01.F-05.S-08), notification policy (E-06a.F-02) all deferred to their owning slices — no knobs ahead of consumers. |
| 14 | **`VERIFY_EMAIL` required-action is NOT set on invited KeyCloak users in slice 9.** | We have no SMTP; KeyCloak would prompt the user to verify an email it can't send. Re-enabled when E-06a notification infrastructure lands. |
| 15 | **Token cache for the KeyCloak Admin client** — via `Duende.IdentityModel.Client.TokenClient` with built-in refresh ~30s before expiry. | Avoids 2× round-trips per Admin API call; trivial implementation; matches standard OAuth client patterns. User-confirmed despite low slice-9 volume because we know Admin API surface area will grow. |
| 16 | **One PR, sequenced commits per task.** Closes E-03.F-01 in full. | Slice-8 precedent. |

## 4. Domain model

### 4.1 `Organization` aggregate — extended

Three new fields on the existing aggregate (slice 2):

```csharp
public sealed partial class Organization : ITenantOwned
{
    // existing: Id, TenantId, DisplayName, CreatedAt

    public string? Description { get; private set; }
    public OrgLogo? Logo { get; private set; }                  // owned VO, nullable
    public string DefaultTimeZone { get; private set; } = "UTC"; // IANA id

    public void UpdateProfile(string displayName, string? description, string defaultTimeZone)
    {
        ValidateDisplayName(displayName);
        ValidateDescription(description);
        ValidateTimeZone(defaultTimeZone);
        DisplayName = displayName;
        Description = description;
        DefaultTimeZone = defaultTimeZone;
    }

    public void SetLogo(OrgLogo logo) => Logo = logo;
    public void ClearLogo() => Logo = null;

    private static void ValidateDisplayName(string s) {
        if (string.IsNullOrWhiteSpace(s)) throw new ArgumentException("Display name required.", nameof(s));
        if (s.Length > 128) throw new ArgumentException("Display name must be <= 128 characters.", nameof(s));
    }
    private static void ValidateDescription(string? s) {
        if (s is { Length: > 1024 }) throw new ArgumentException("Description must be <= 1024 characters.", nameof(s));
    }
    private static void ValidateTimeZone(string tz) {
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(tz, out _))
            throw new ArgumentException("Unknown IANA time-zone id.", nameof(tz));
    }
}

public sealed class OrgLogo
{
    public byte[] Bytes { get; private set; } = [];
    public string MimeType { get; private set; } = "";
    public string ContentHash { get; private set; } = "";   // sha256 hex; doubles as ETag

    private OrgLogo() { }

    public static OrgLogo Create(byte[] bytes, string mimeType)
    {
        if (bytes.Length == 0 || bytes.Length > 256 * 1024)
            throw new ArgumentException("Logo bytes must be 1..262144.", nameof(bytes));
        if (!AcceptedMimeTypes.Contains(mimeType))
            throw new ArgumentException("Unsupported logo mime-type.", nameof(mimeType));
        return new OrgLogo {
            Bytes = bytes,
            MimeType = mimeType,
            ContentHash = Convert.ToHexString(SHA256.HashData(bytes)),
        };
    }

    private static readonly FrozenSet<string> AcceptedMimeTypes =
        new[] { "image/png", "image/jpeg", "image/svg+xml" }.ToFrozenSet();
}
```

### 4.2 `Invitation` aggregate

```csharp
public readonly record struct InvitationId(Guid Value) {
    public static InvitationId New() => new(Guid.NewGuid());
}

public enum InvitationStatus : byte {
    Pending = 1,
    Accepted = 2,
    Revoked = 3,
    Expired = 4,
}

public sealed class Invitation : ITenantOwned
{
    private Guid _id;
    public InvitationId Id => new(_id);
    public TenantId TenantId { get; private set; }
    public string Email { get; private set; } = "";       // normalized lowercase
    public string Role { get; private set; } = "";        // KartovaRoles constant
    public Guid InvitedByUserId { get; private set; }
    public DateTimeOffset InvitedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public InvitationStatus Status { get; private set; }
    public Guid? KeycloakUserId { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    private Invitation() { }

    public static Invitation Create(
        string email, string role, Guid invitedByUserId,
        Guid keycloakUserId, TenantId tenantId, TimeProvider clock)
    {
        ValidateEmail(email);
        if (!KartovaRoles.All.Contains(role))
            throw new ArgumentException("Unknown role.", nameof(role));
        var now = clock.GetUtcNow();
        return new Invitation {
            _id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = email.Trim().ToLowerInvariant(),
            Role = role,
            InvitedByUserId = invitedByUserId,
            InvitedAt = now,
            ExpiresAt = now.AddDays(7),
            Status = InvitationStatus.Pending,
            KeycloakUserId = keycloakUserId,
        };
    }

    public void MarkAccepted(TimeProvider clock) {
        if (Status != InvitationStatus.Pending)
            throw new InvalidOperationException($"Cannot accept invitation in {Status} state.");
        Status = InvitationStatus.Accepted;
        AcceptedAt = clock.GetUtcNow();
    }

    public void Revoke(TimeProvider clock) {
        if (Status != InvitationStatus.Pending)
            throw new InvalidOperationException($"Cannot revoke invitation in {Status} state.");
        Status = InvitationStatus.Revoked;
        RevokedAt = clock.GetUtcNow();
    }

    public void MarkExpired(TimeProvider clock) {
        if (Status != InvitationStatus.Pending)
            throw new InvalidOperationException($"Cannot expire invitation in {Status} state.");
        Status = InvitationStatus.Expired;
    }

    private static void ValidateEmail(string email) {
        if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Email required.", nameof(email));
        if (email.Length > 320) throw new ArgumentException("Email must be <= 320 characters.", nameof(email));
        if (!email.Contains('@')) throw new ArgumentException("Email must contain '@'.", nameof(email));
    }
}
```

### 4.3 `User` projection (not an aggregate)

Pure read model. Source of truth = KeyCloak. No write methods, no invariants.

```csharp
[ExcludeFromCodeCoverage]
public sealed class User : ITenantOwned
{
    public Guid Id { get; set; }                       // = KeyCloak `sub`
    public TenantId TenantId { get; set; }
    public string Email { get; set; } = "";
    public string? GivenName { get; set; }
    public string? FamilyName { get; set; }
    public string DisplayName { get; set; } = "";     // denormalized: "given_name family_name" || email
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

A `UserProjectionUpdater` helper (in `Kartova.Organization.Application`) handles upsert from JWT claims; called by `TenantClaimsTransformation`.

### 4.4 Tables

**`organizations`** — alter:
```sql
ALTER TABLE organizations
  ADD COLUMN description       varchar(1024) NULL,
  ADD COLUMN logo_bytes        bytea         NULL,
  ADD COLUMN logo_mime_type    varchar(32)   NULL,
  ADD COLUMN logo_content_hash varchar(64)   NULL,
  ADD COLUMN default_time_zone varchar(64)   NOT NULL DEFAULT 'UTC';
ALTER TABLE organizations
  ADD CONSTRAINT chk_logo_complete CHECK (
    (logo_bytes IS NULL AND logo_mime_type IS NULL AND logo_content_hash IS NULL)
    OR (logo_bytes IS NOT NULL AND logo_mime_type IS NOT NULL AND logo_content_hash IS NOT NULL)
  );
```

**`users`** — new:
```
id              uuid          PK            -- KeyCloak sub
tenant_id       uuid          not null
email           varchar(320)  not null
given_name      varchar(128)  null
family_name     varchar(128)  null
display_name    varchar(256)  not null
last_seen_at    timestamptz   null
created_at      timestamptz   not null

UNIQUE (tenant_id, email)
INDEX idx_users_tenant ON users(tenant_id)
INDEX idx_users_displayname_trgm ON users USING gin (display_name gin_trgm_ops)
INDEX idx_users_email_lower ON users(tenant_id, lower(email))
-- RLS: tenant_id = current_setting('app.current_tenant_id')::uuid
```

`CREATE EXTENSION IF NOT EXISTS pg_trgm;` in a prior migration.

**`invitations`** — new:
```
id                  uuid          PK
tenant_id           uuid          not null
email               varchar(320)  not null
role                varchar(32)   not null
invited_by_user_id  uuid          not null
invited_at          timestamptz   not null
expires_at          timestamptz   not null
status              smallint      not null
keycloak_user_id    uuid          null
accepted_at         timestamptz   null
revoked_at          timestamptz   null

INDEX idx_invitations_tenant_status ON invitations(tenant_id, status)
INDEX idx_invitations_email_pending ON invitations(tenant_id, lower(email)) WHERE status = 1
-- RLS: tenant_id = current_setting('app.current_tenant_id')::uuid
```

### 4.5 RLS policies

Both new tables follow slice-8's pattern (§4.4 there):

```sql
ALTER TABLE users ENABLE ROW LEVEL SECURITY;
ALTER TABLE users FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON users
  USING (tenant_id = current_setting('app.current_tenant_id')::uuid);

-- same pattern for invitations
```

`organizations` already has RLS enabled (slice 2); no change to RLS on the alter.

### 4.6 Migrations

Sequenced:

1. **`EnablePgTrgmExtension`** (Organization) — `CREATE EXTENSION IF NOT EXISTS pg_trgm;` (idempotent, no FORCE concerns).
2. **`AddOrganizationProfileColumns`** (Organization) — pure DDL on `organizations`; no FORCE toggle needed (no backfill).
3. **`AddUsersTable`** (Organization) — new table, single SQL block: `CREATE TABLE + INDEXES + ENABLE/FORCE RLS + CREATE POLICY`. Slice-8 pattern (`AddTeamsTable` precedent).
4. **`AddInvitationsTable`** (Organization) — same shape.

### 4.7 EF configurations

- `OrganizationEntityTypeConfiguration` extended: `Description`, `OrgLogo` mapped via `OwnsOne` with explicit column names (`logo_bytes`, `logo_mime_type`, `logo_content_hash`), `DefaultTimeZone`.
- New `InvitationEntityTypeConfiguration`: TenantId + InvitationId value converters, enum→smallint for Status.
- New `UserEntityTypeConfiguration`: pure POCO mapping; id is raw Guid (no value converter — comes from KeyCloak `sub`); TenantId converter; trigram index declared.

### 4.8 DbContexts

`OrganizationDbContext` gains `DbSet<User> Users` + `DbSet<Invitation> Invitations`. `AdminOrganizationDbContext` (BYPASSRLS) gains the same DbSets — needed by the expiry sweep (§9).

## 5. Authorization plumbing

### 5.1 Permission constants

Append to `KartovaPermissions`:

```csharp
public const string OrgProfileRead         = "org.profile.read";
public const string OrgProfileEdit         = "org.profile.edit";
public const string OrgInvitationsRead     = "org.invitations.read";
public const string OrgInvitationsCreate   = "org.invitations.create";
public const string OrgInvitationsRevoke   = "org.invitations.revoke";
public const string OrgUsersRead           = "org.users.read";
public const string OrgUsersSearch         = "org.users.search";
```

7 new permissions. Slice 7's data-driven `All` collection + drift-snapshot picks them up automatically via reflection.

### 5.2 Role → permission map

`KartovaRolePermissions.Map` gains:

| Role | OrgProfileRead | OrgProfileEdit | OrgInvitationsRead | OrgInvitationsCreate | OrgInvitationsRevoke | OrgUsersRead | OrgUsersSearch |
|---|---|---|---|---|---|---|---|
| Viewer    | ✓ | — | — | — | — | ✓ | — |
| Member    | ✓ | — | — | — | — | ✓ | ✓ |
| TeamAdmin | ✓ | — | — | — | — | ✓ | ✓ |
| OrgAdmin  | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |

No resource-based handlers added in slice 9. Every endpoint is OrgAdmin-only on writes; reads are tenant-scoped via RLS without per-team scoping.

### 5.3 Endpoint binding pattern

Standard slice-7 binding-level enforcement (`.RequireAuthorization(KartovaPermissions.X)` — Viewer/anon fail before any DB hit). Tenant scoping is automatic via the existing `ITenantScope` middleware chain.

### 5.4 SPA permission constants

`web/src/shared/auth/permissions.ts` + `permissions.snapshot.json` gain 7 entries. Drift sentinel arch test (slice 7's `Ts_snapshot_equals_csharp_KartovaPermissions_All`) auto-catches divergence.

## 6. Endpoints

### 6.1 Org profile endpoints — under `/api/v1/organizations`

| Method | Path | Claim policy | Notes |
|---|---|---|---|
| `GET`    | `/me` | `org.profile.read` | Returns `OrgProfileResponse` (no bytes). ETag header from logo content-hash + profile version. |
| `PUT`    | `/me` | `org.profile.edit` | Body: `UpdateOrgProfileRequest`. `If-Match` required (optimistic concurrency, ADR-0096). |
| `PUT`    | `/me/logo` | `org.profile.edit` | Content-Type ∈ {png, jpeg, svg+xml}. Body raw bytes ≤ 256 KB. 415 on bad mime; 413 over size; 422 on magic-byte/sanitization rejection. 200 returns `{ logoEtag, mimeType }`. |
| `DELETE` | `/me/logo` | `org.profile.edit` | 204. |
| `GET`    | `/me/logo` | `org.profile.read` | Streams bytes. `Content-Type: <stored mime>`. `ETag: "<hash>"`. `Cache-Control: private, max-age=300`. 304 on If-None-Match match. 404 if no logo. |

### 6.2 Invitation endpoints — under `/api/v1/organizations`

| Method | Path | Claim policy | Notes |
|---|---|---|---|
| `GET`  | `/invitations` | `org.invitations.read` | Cursor-paginated (ADR-0095). Filters: `status ∈ {pending, accepted, revoked, expired, all}` default `pending`. `sortBy ∈ {invitedAt, expiresAt, email}` default `invitedAt`. |
| `POST` | `/invitations` | `org.invitations.create` | Body: `CreateInvitationRequest`. Three-way 409 model (below). 201 returns `CreateInvitationResponse` (invitation + inviteUrl). |
| `POST` | `/invitations/{id:guid}/revoke` | `org.invitations.revoke` | 204. Marks Revoked + deletes the dormant KC user. 409 `invitation-not-pending` if not Pending. |

**Three-way 409 error model on `POST /invitations`:**

| Detected via | Problem type | Meaning |
|---|---|---|
| `users` row with matching email in current tenant | `email-already-in-tenant` | The email already belongs to this tenant. |
| Pending `invitations` row with matching email in current tenant | `email-already-invited` | Already invited (best-effort idempotency: response carries the existing invitation). |
| `KeycloakAdminError.EmailAlreadyExists` from create-user call | `email-already-on-platform` | The email exists in another tenant (KeyCloak realm uniqueness). Message: *"This email already has a Kartova account in another organization."* |

### 6.3 User endpoints — under `/api/v1/organizations`

| Method | Path | Claim policy | Notes |
|---|---|---|---|
| `GET` | `/users` | `org.users.search` | Typeahead. Query `q` (min 2 chars), `limit ≤ 20`. Matches `display_name` (trigram) + `email` (prefix). Returns `IReadOnlyList<UserSummaryResponse>`. **Marked `[BoundedListResult]`** with justification *"typeahead capped at 20 results — pagination not meaningful"*. |
| `GET` | `/users/{id:guid}` | `org.users.read` | Returns `UserDetailResponse` — user + teams. **Owned applications NOT included** — SPA composes via Catalog endpoint (see §6.5). |

### 6.4 Session bootstrap endpoint — under `/api/v1/auth`

| Method | Path | Authorization | Notes |
|---|---|---|---|
| `POST` | `/api/v1/auth/session` | `[Authorize]` (any valid JWT) | Returns `SessionStartResponse` (me + role + permissions + teams + organization profile + optional `AcceptedInvitation`). Called by SPA on OIDC callback completion. |

### 6.5 Catalog endpoint changes

- `GET /api/v1/catalog/applications` gains optional filter `?ownerUserId={guid}`. Validates against `users` projection (422 `invalid-owner` if not found in current tenant).
- `ApplicationResponse` gains nullable `Owner: UserDisplayInfo?` field. Catalog handlers batch-fetch via `IUserDirectory.GetManyAsync(ids)` when assembling list/detail responses.
- `GET /api/v1/catalog/applications/{id}` same `Owner` enrichment.

This is a **breaking change** to `ApplicationResponse`. SPA + integration tests updated in lockstep.

### 6.6 Team endpoint changes

- `TeamMemberResponse` gains `DisplayName` + `Email` fields (slice 8 returned only `UserId + Role`). `IUserDirectory.GetManyAsync` populates them. Breaking change to slice-8 contract; SPA updated in lockstep.

### 6.7 New DTOs in `Kartova.Organization.Contracts`

All carry `[ExcludeFromCodeCoverage]`:

```csharp
public sealed record OrgProfileResponse(Guid Id, string DisplayName, string? Description,
    string DefaultTimeZone, string? LogoEtag, string? LogoMimeType, DateTimeOffset CreatedAt);

public sealed record UpdateOrgProfileRequest(string DisplayName, string? Description, string DefaultTimeZone);

public sealed record InvitationResponse(Guid Id, string Email, string Role,
    DateTimeOffset InvitedAt, DateTimeOffset ExpiresAt, string Status,
    Guid InvitedByUserId, DateTimeOffset? AcceptedAt, DateTimeOffset? RevokedAt);

public sealed record CreateInvitationRequest(string Email, string Role);
public sealed record CreateInvitationResponse(InvitationResponse Invitation, string InviteUrl);

public sealed record UserSummaryResponse(Guid Id, string DisplayName, string Email);

public sealed record UserDetailResponse(Guid Id, string Email, string DisplayName,
    string? GivenName, string? FamilyName, IReadOnlyCollection<UserTeamMembership> Teams,
    DateTimeOffset CreatedAt, DateTimeOffset? LastSeenAt);

public sealed record UserTeamMembership(Guid TeamId, string TeamDisplayName, string Role);

public sealed record SessionStartResponse(
    UserDisplayInfo Me, string Role,
    IReadOnlyCollection<string> Permissions,
    IReadOnlyCollection<MeTeamMembership> Teams,
    OrgProfileResponse Organization,
    AcceptedInvitationInfo? AcceptedInvitation);

public sealed record AcceptedInvitationInfo(
    string OrgDisplayName, UserDisplayInfo InvitedBy,
    DateTimeOffset InvitedAt, DateTimeOffset AcceptedAt);
```

`UserDisplayInfo` lives in `Kartova.SharedKernel` (the base shared assembly) — see §7.4 layering note. Wire contracts reference it without dragging in `Kartova.SharedKernel.Identity`.

### 6.8 Problem types

Five new constants in `ProblemTypes`:

```csharp
public const string EmailAlreadyInvited     = "https://kartova.io/problems/email-already-invited";
public const string EmailAlreadyInTenant    = "https://kartova.io/problems/email-already-in-tenant";
public const string EmailAlreadyOnPlatform  = "https://kartova.io/problems/email-already-on-platform";
public const string InvitationNotPending    = "https://kartova.io/problems/invitation-not-pending";
public const string UnsupportedLogoMedia    = "https://kartova.io/problems/unsupported-logo-media";
```

Reused: `ResourceNotFound`, `ValidationFailed`, `ConcurrencyConflict`.

## 7. Shared infrastructure — `Kartova.SharedKernel.Identity` + locking

### 7.1 New project `Kartova.SharedKernel.Identity`

```
src/Kartova.SharedKernel.Identity/
  Kartova.SharedKernel.Identity.csproj
  IKeycloakAdminClient.cs
  KeycloakAdminClient.cs
  KeycloakAdminOptions.cs
  KeycloakUser.cs
  KeycloakAdminException.cs
  IUserDirectory.cs
  UserDisplayInfo.cs
  ServiceCollectionExtensions.cs
```

NuGet deps: `Duende.IdentityModel` (8.x — the legacy `IdentityModel` package was renamed to `Duende.IdentityModel` after v7; the `TokenClient` namespace was renamed to `Duende.IdentityModel.Client` in the rebrand — earlier reconciliation note that claimed the legacy `IdentityModel.Client` namespace was preserved was wrong and has been corrected). `System.Net.Http.Json` is NOT added explicitly — it ships with the `net10.0` shared framework, and adding it triggers NU1510 under `TreatWarningsAsErrors`.
Project references: `Kartova.SharedKernel` only (no ASP.NET coupling).

### 7.2 `IKeycloakAdminClient`

```csharp
public interface IKeycloakAdminClient
{
    Task<Guid> CreateUserAsync(CreateKeycloakUserRequest request, CancellationToken ct);
    Task<KeycloakUser?> GetUserAsync(Guid userId, CancellationToken ct);
    Task AssignRealmRoleAsync(Guid userId, string roleName, CancellationToken ct);
    Task<IReadOnlyList<KeycloakUser>> SearchUsersAsync(string query, int limit, CancellationToken ct);
    Task DeleteUserAsync(Guid userId, CancellationToken ct);
}

public sealed record CreateKeycloakUserRequest(
    string Email, string? FirstName, string? LastName,
    string TenantId, IReadOnlyList<string> RequiredActions);

public sealed record KeycloakUser(
    Guid Id, string Email, string? FirstName, string? LastName,
    bool Enabled, bool EmailVerified, string? TenantId);

public sealed class KeycloakAdminException : Exception
{
    public KeycloakAdminError Error { get; }
    public KeycloakAdminException(KeycloakAdminError error, string message) : base(message) => Error = error;
}

public enum KeycloakAdminError {
    EmailAlreadyExists,
    Unauthorized,
    NotFound,
    Unexpected,
}
```

Interface deliberately narrow — no generic admin-request escape hatch.

### 7.3 `KeycloakAdminClient` (implementation)

- Uses `Duende.IdentityModel.Client.TokenClient` for `client_credentials` grant with built-in cache + ~30s pre-expiry refresh.
- `HttpClient` injected via `IHttpClientFactory`; base address = realm root.
- Returns 409 from KC's create-user → throws `KeycloakAdminException(EmailAlreadyExists, ...)`.
- 401/403 → `Unauthorized` (backend config issue, surfaced as 502 from the calling endpoint).
- 404 → `NotFound` (delete on already-gone user — idempotent OK).
- Other non-2xx → `Unexpected`.
- No retry policy in slice 9 (fail-fast); Polly retry deferred.

### 7.4 `IUserDirectory`

```csharp
public interface IUserDirectory
{
    Task<UserDisplayInfo?> GetAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, UserDisplayInfo>> GetManyAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken ct);
}

public sealed record UserDisplayInfo(Guid Id, string DisplayName, string Email);
```

**Layering note:** `UserDisplayInfo` lives in `Kartova.SharedKernel` (the base shared assembly), *not* in `Kartova.SharedKernel.Identity` — even though `IUserDirectory` is in Identity. Reason: wire DTOs in `*.Contracts` assemblies use `UserDisplayInfo` (see §6.7); Contracts already reference `Kartova.SharedKernel` but should not reference `Kartova.SharedKernel.Identity` (which is infrastructure plumbing). The record carries `[ExcludeFromCodeCoverage]` since it's a pure data carrier.

Implementation `OrganizationUserDirectory` lives in `Kartova.Organization.Infrastructure`:

```csharp
internal sealed class OrganizationUserDirectory(OrganizationDbContext db) : IUserDirectory
{
    public async Task<UserDisplayInfo?> GetAsync(Guid userId, CancellationToken ct)
        => await db.Users
            .Where(u => u.Id == userId)
            .Select(u => new UserDisplayInfo(u.Id, u.DisplayName, u.Email))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, UserDisplayInfo>> GetManyAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0) return new Dictionary<Guid, UserDisplayInfo>();
        var rows = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new UserDisplayInfo(u.Id, u.DisplayName, u.Email))
            .ToListAsync(ct);
        return rows.ToDictionary(u => u.Id);
    }
}
```

Registered scoped via `OrganizationModule.RegisterServices`. RLS scopes naturally to the current tenant.

### 7.5 KeyCloak realm changes

`deploy/keycloak/kartova-realm.json` gains a confidential client `kartova-admin` with service-account roles: `manage-users`, `view-users`, `view-realm`. Single JSON edit, mirrored Helm template values.

DI registration:
```csharp
public static IServiceCollection AddKeycloakAdminClient(this IServiceCollection services,
    IConfiguration config, string sectionName = "KartovaIdentity:Keycloak")
{
    services.AddOptions<KeycloakAdminOptions>().Bind(config.GetSection(sectionName)).ValidateOnStart();
    services.AddHttpClient<IKeycloakAdminClient, KeycloakAdminClient>((sp, http) => {
        var opts = sp.GetRequiredService<IOptions<KeycloakAdminOptions>>().Value;
        http.BaseAddress = new Uri(opts.BaseUrl);
    });
    services.AddSingleton<TokenClient>(/* configured for client_credentials */);
    return services;
}
```

### 7.6 Distributed locking shared infrastructure

#### 7.6.1 `IDistributedLock` (in `Kartova.SharedKernel`)

```csharp
public interface IDistributedLock
{
    Task<IAsyncDisposable?> TryAcquireAsync(string lockName, CancellationToken ct);
}
```

Returns `null` when the lock is held elsewhere; the handle releases on dispose.

#### 7.6.2 `PostgresAdvisoryLock` (in `Kartova.SharedKernel.Postgres`)

Uses session-level `pg_try_advisory_lock` (bound to connection lifetime, auto-released on connection drop — no stale locks).

```csharp
internal sealed class PostgresAdvisoryLock(
    NpgsqlDataSource dataSource, ILogger<PostgresAdvisoryLock> logger) : IDistributedLock
{
    public async Task<IAsyncDisposable?> TryAcquireAsync(string lockName, CancellationToken ct)
    {
        var key = StableHash64(lockName);
        var conn = await dataSource.OpenConnectionAsync(ct);
        try {
            await using var cmd = new NpgsqlCommand("SELECT pg_try_advisory_lock(@k)", conn);
            cmd.Parameters.AddWithValue("k", key);
            var acquired = (bool)(await cmd.ExecuteScalarAsync(ct) ?? false);
            if (!acquired) { await conn.DisposeAsync(); return null; }
            return new Handle(conn, key, lockName, logger);
        }
        catch { await conn.DisposeAsync(); throw; }
    }

    private sealed class Handle(NpgsqlConnection conn, long key, string name, ILogger log) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() {
            try {
                await using var cmd = new NpgsqlCommand("SELECT pg_advisory_unlock(@k)", conn);
                cmd.Parameters.AddWithValue("k", key);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex) { log.LogWarning(ex, "Lock unlock failed for {LockName}", name); }
            finally { await conn.DisposeAsync(); }
        }
    }

    private static long StableHash64(string input)
    {
        // FNV-1a 64-bit
        unchecked {
            ulong h = 14695981039346656037UL;
            foreach (var b in Encoding.UTF8.GetBytes(input)) {
                h ^= b; h *= 1099511628211UL;
            }
            return (long)h;
        }
    }
}
```

DI:
```csharp
public static IServiceCollection AddPostgresDistributedLocks(this IServiceCollection services)
{
    services.AddSingleton<IDistributedLock, PostgresAdvisoryLock>();
    return services;
}
```

Uses the BYPASSRLS pool — leader work is system-level, not tenant-scoped.

#### 7.6.3 `LeaderElectedPeriodicService` (in `Kartova.SharedKernel`)

```csharp
public abstract class LeaderElectedPeriodicService(
    IServiceScopeFactory scopes, IDistributedLock locks, TimeProvider clock, ILogger logger)
    : BackgroundService
{
    protected abstract string LockName { get; }
    protected abstract TimeSpan Interval { get; }
    protected abstract Task ExecuteLeaderWorkAsync(IServiceProvider services, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        do {
            await using var scope = scopes.CreateAsyncScope();
            await using var lockHandle = await locks.TryAcquireAsync(LockName, ct);
            if (lockHandle is null) {
                logger.LogDebug("{Service}: lock held by another instance — skipping tick", GetType().Name);
                continue;
            }
            try { await ExecuteLeaderWorkAsync(scope.ServiceProvider, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { logger.LogError(ex, "{Service}: leader tick failed", GetType().Name); }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
```

Subclasses are concrete + unit-testable. First usage: `ExpireInvitationsHostedService` (§9).

## 8. SPA

### 8.1 New routes

```tsx
<Route element={<ProtectedShell />}>
  <Route path="/settings/organization" element={<OrganizationSettingsPage />} />
  <Route path="/settings/invitations" element={<InvitationsPage />} />
  <Route path="/users/:id" element={<UserDetailPage />} />
  <Route path="/welcome" element={<WelcomePage />} />
</Route>
```

Page-level gating per the permission matrix (§5.2). Sidebar adds a Settings group; `/welcome` is reached only via the session-bootstrap response, not navigation.

### 8.2 New files

| Path | Purpose |
|---|---|
| `web/src/features/organization/api/organization.ts` | `useOrgProfile`, `useUpdateOrgProfile`, `useUploadOrgLogo`, `useDeleteOrgLogo`, `useLogoUrl` helper. |
| `web/src/features/organization/api/invitations.ts` | `useInvitationsList`, `useCreateInvitation`, `useRevokeInvitation`. |
| `web/src/features/organization/api/__tests__/*.test.tsx` | Hook tests. |
| `web/src/features/organization/pages/OrganizationSettingsPage.tsx` | Profile form + logo uploader + time-zone picker. |
| `web/src/features/organization/pages/InvitationsPage.tsx` | Pending/All tabs, cursor list, invite button. |
| `web/src/features/organization/pages/__tests__/*.test.tsx` | Component tests. |
| `web/src/features/organization/components/LogoUploader.tsx` | Drag-drop + client-side 256 KB guard + format check + preview. |
| `web/src/features/organization/components/InviteUserDialog.tsx` | Form (email + role) + post-create success state. |
| `web/src/features/organization/components/CopyInviteLinkBox.tsx` | Copy-button panel; "Share this link with <email>. Expires in 7 days." + "Email delivery is coming soon" explainer. |
| `web/src/features/organization/components/RevokeInvitationConfirm.tsx` | Plain confirm. |
| `web/src/features/organization/components/__tests__/*.test.tsx` | Component tests. |
| `web/src/features/organization/schemas/orgProfile.ts` | zod: displayName ≤128, description ≤1024, defaultTimeZone via `Intl.supportedValuesOf('timeZone')`. |
| `web/src/features/organization/schemas/inviteUser.ts` | zod: email format, role enum. |
| `web/src/features/users/api/users.ts` | `useUser(id)`, `useUserSearch(q, opts)`. |
| `web/src/features/users/api/__tests__/users.test.tsx` | Hook tests. |
| `web/src/features/users/pages/UserDetailPage.tsx` | User card + Teams card + Owned-applications card (parallel fetches). |
| `web/src/features/users/pages/__tests__/UserDetailPage.test.tsx` | Component test. |
| `web/src/features/users/components/UserSearchCombobox.tsx` | Typeahead; debounced 250ms; min 2 chars. |
| `web/src/features/users/components/OwnerLink.tsx` | `<OwnerLink user={UserDisplayInfo?} />` — renders display name as `<Link>`, falls back to "Unknown user" if null. |
| `web/src/features/users/components/__tests__/*.test.tsx` | Component tests. |
| `web/src/features/auth/api/session.ts` | `useStartSession` mutation. |
| `web/src/features/auth/pages/WelcomePage.tsx` | One-time welcome screen reading `AcceptedInvitation` from router state. |
| `web/src/features/auth/components/OidcCallbackHandler.tsx` | Invokes `useStartSession()` on OIDC callback; routes to `/welcome` if `AcceptedInvitation` present else to intended URL. |

### 8.3 Modified files

| Path | Change |
|---|---|
| `web/src/shared/auth/permissions.ts` + `permissions.snapshot.json` | +7 permission strings. |
| `web/src/app/router.tsx` | Add `/settings/organization`, `/settings/invitations`, `/users/:id`, `/welcome`. |
| `web/src/components/layout/Sidebar.tsx` | Add Settings group (Organization + Invitations entries) gated by permissions. |
| `web/src/components/layout/Header.tsx` | Consume `useOrgProfile()`; render logo via `useLogoUrl()` else org display-name text. |
| `web/src/features/catalog/pages/ApplicationDetailPage.tsx` | Owner renders via `<OwnerLink user={app.owner} />`. |
| `web/src/features/catalog/components/ApplicationsTable.tsx` | Owner column → `<OwnerLink>`. |
| `web/src/features/teams/components/AddMemberDialog.tsx` (slice 8) | Replace `Guid` input with `<UserSearchCombobox>`. |
| `web/src/features/teams/pages/TeamDetailPage.tsx` | Member rows render display name + email from embedded `TeamMemberResponse`. |

### 8.4 SPA cross-module composition for user-detail page

`UserDetailPage` fires two React Query hooks in parallel:
- `useUser(id)` → `GET /api/v1/organizations/users/{id}` → user + teams
- `useApplications({ ownerUserId: id })` → `GET /api/v1/catalog/applications?ownerUserId={id}` → owned apps

Independent loading skeletons; each card renders as its data arrives. Cleanly mirrors ADR-0082.

### 8.5 Codegen

Slice 7's chore (PR #25) — TS types regenerate after backend ships new endpoints. SPA hooks use the typed `apiClient` with compile-time URL + DTO safety.

## 9. Critical runtime flows

### 9.1 JWT-claim → `users` projection sync

**Trigger:** every authenticated request, in `TenantClaimsTransformation.TransformAsync`.

```
1. Extract from JWT: sub, email, given_name, family_name, tenantId.
2. Open OrganizationDbContext via existing ITenantScope (already begun upstream).
3. Upsert `users` row:
     ON CONFLICT (id) DO UPDATE SET
       email = EXCLUDED.email,
       given_name = EXCLUDED.given_name, family_name = EXCLUDED.family_name,
       display_name = COALESCE(NULLIF(TRIM(given_name || ' ' || family_name), ''), email),
       last_seen_at = NOW()
   Insert path sets created_at = NOW() as well.
4. Check matching Pending invitation by `keycloak_user_id = userId`:
     IF found AND ExpiresAt > NOW():
       invitation.MarkAccepted(clock); SaveChanges
       ICurrentUser.JustAcceptedInvitation = invitation  (used by §9.4)
5. Slice-8 logic continues: populate ICurrentUser.TeamMemberships via ITeamMembershipReader.
```

Idempotent + cheap. Naive `last_seen_at` write per request is acceptable for MVP scale; debounce is a documented follow-up (§13).

### 9.2 Invitation create

```
POST /api/v1/organizations/invitations  body { email, role }

1. Validate request shape; 422 on bad email/role.
2. Check `users` row in current tenant matching email → 409 email-already-in-tenant.
3. Check Pending `invitations` row in current tenant matching email →
   409 email-already-invited (returns existing invitation for idempotency).
4. IKeycloakAdminClient.CreateUserAsync({
       email, firstName=null, lastName=null,
       enabled=true, emailVerified=false,
       requiredActions=["UPDATE_PASSWORD"],          // VERIFY_EMAIL omitted — no SMTP
       attributes.tenantId=[<current tenant id>]
   })
   Catches KeycloakAdminError.EmailAlreadyExists → 409 email-already-on-platform.
5. IKeycloakAdminClient.AssignRealmRoleAsync(kcId, role).
   Try-catch: on failure, IKeycloakAdminClient.DeleteUserAsync(kcId); rethrow as 502.
6. Insert Invitation aggregate (Pending, expires +7d, keycloakUserId = kcId).
7. Insert `users` projection stub (id = kcId, email, displayName = email fallback).
8. Return 201:
     { invitation: <InvitationResponse>,
       inviteUrl: $"{frontendBaseUrl}/?invitation=1" }
```

### 9.3 Invitation acceptance

```
1. OrgAdmin shares inviteUrl with new user.
2. User opens URL → SPA <ProtectedShell> sees no session → redirects to KeyCloak OIDC.
3. KeyCloak login page; user enters email; KC sees requiredActions=[UPDATE_PASSWORD] →
   prompts for new password.
4. User sets password → KC clears the required action → issues OIDC tokens.
5. SPA OIDC callback completes → fires useStartSession() mutation.
6. Backend TenantClaimsTransformation runs (§9.1) — upserts users row + accepts
   matching Pending invitation (sets ICurrentUser.JustAcceptedInvitation).
7. POST /api/v1/auth/session handler returns SessionStartResponse with
   AcceptedInvitation populated.
8. SPA routes to /welcome with the info as router state.
9. WelcomePage renders celebration screen; user clicks Continue → /catalog.
```

**Late-acceptance edge case:** if invitation has `ExpiresAt < NOW()` when the user clicks, step 6's acceptance is skipped. The user authenticates (their KeyCloak account is still valid until the expiry sweep deletes it) but no invitation flips. They appear in `users` table with `last_seen_at` but no team memberships — a "ghost" tenant member for up to 1 hour worst case (the expiry-sweep interval). Acceptable per §13.

### 9.4 Detection of "just accepted in this request"

Per-request flag on `ICurrentUser`:

```csharp
public interface ICurrentUser {
    // existing ...
    Guid? JustAcceptedInvitationId { get; }
}
```

Raw `Guid?` — *not* `InvitationId?` — to avoid leaking `Kartova.Organization.Domain` types into `Kartova.SharedKernel.AspNetCore`. `TenantClaimsTransformation` sets this flag with the invitation's id *only* when it actually flipped Pending → Accepted in the current request. The session-bootstrap endpoint resolves the full aggregate via `OrganizationDbContext` to build `AcceptedInvitationInfo`. Deterministic — no clock-skew dependency.

### 9.5 Logo upload

```
PUT /api/v1/organizations/me/logo
1. Content-Type ∈ {png, jpeg, svg+xml}? Bad → 415 unsupported-logo-media.
2. Stream-read body with 256 KB limit (Microsoft.AspNetCore.WebUtilities). Over → 413.
3. Magic-byte check:
     PNG: starts 89 50 4E 47 0D 0A 1A 0A
     JPEG: starts FF D8 FF
     SVG: text starts with "<?xml" or "<svg" (after whitespace)
   Mismatch → 422 unsupported-logo-media.
4. IF SVG: Ganss.Xss sanitize in allow-list mode:
     - Whitelist tags: svg, g, path, rect, circle, ellipse, polygon, polyline,
                       line, text, defs, use, linearGradient, radialGradient,
                       stop, clipPath, mask, pattern
     - Whitelist attrs: id, class, style (no expression()), viewBox, d, fill,
                        stroke, stroke-width, x, y, cx, cy, r, rx, ry, points,
                        transform, opacity
     - Strip: script, foreignObject, iframe, on-* handlers, xlink:href external,
              javascript:, data: (except data:image/* in <use>)
   If stripped-size > 20% of original → 422 unsupported-logo-media
   (extension: "SVG contained disallowed content").
5. Compute SHA-256(bytes) → contentHash.
6. org.SetLogo(OrgLogo.Create(bytes, mimeType)); SaveChanges.
7. 200 { logoEtag: contentHash, mimeType }.
```

### 9.6 Logo serve

```
GET /api/v1/organizations/me/logo
1. Load org; if no logo → 404.
2. If If-None-Match matches logo_content_hash → 304 (no body).
3. Headers: Content-Type, Content-Length, ETag, Cache-Control: private, max-age=300.
4. Stream bytes.
```

### 9.7 Invitation-expiry sweep

`ExpireInvitationsHostedService : LeaderElectedPeriodicService` — `LockName = "expire-invitations"`, `Interval = TimeSpan.FromHours(1)`.

```csharp
internal sealed class ExpireInvitationsHostedService(
    IServiceScopeFactory scopes, IDistributedLock locks, TimeProvider clock,
    ILogger<ExpireInvitationsHostedService> logger)
    : LeaderElectedPeriodicService(scopes, locks, clock, logger)
{
    protected override string LockName => "expire-invitations";
    protected override TimeSpan Interval => TimeSpan.FromHours(1);

    protected override async Task ExecuteLeaderWorkAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AdminOrganizationDbContext>();   // BYPASSRLS pool
        var kc = services.GetRequiredService<IKeycloakAdminClient>();
        var now = clock.GetUtcNow();

        var due = await db.Invitations
            .Where(i => i.Status == InvitationStatus.Pending && i.ExpiresAt < now)
            .ToListAsync(ct);

        foreach (var inv in due) {
            try { if (inv.KeycloakUserId is { } kid) await kc.DeleteUserAsync(kid, ct); }
            catch (KeycloakAdminException ex) when (ex.Error == KeycloakAdminError.NotFound) { }
            inv.MarkExpired(clock);
        }
        await db.SaveChangesAsync(ct);

        if (due.Count > 0) logger.LogInformation("Expired {Count} invitations.", due.Count);
    }
}
```

Idempotent — safe to re-run if a node dies mid-work. BYPASSRLS connection because expiry is system-level, not tenant-scoped.

### 9.8 Session bootstrap

```
POST /api/v1/auth/session  (no body)

1. (Pipeline) TenantScopeBeginMiddleware opens scope from JWT tenant claim.
2. (Pipeline) TenantClaimsTransformation runs §9.1 — upsert + invitation
   acceptance side-effects + ICurrentUser.JustAcceptedInvitation flag.
3. Handler:
     - Load my User from local projection (now fresh).
     - Load my permissions (KartovaRolePermissions.Map[role]).
     - Load my Teams (already in ICurrentUser).
     - Load OrgProfile (Organization aggregate).
     - If ICurrentUser.JustAcceptedInvitationId is { } id:
         Load Invitation by id from OrganizationDbContext (RLS-scoped).
         Look up invitedBy via IUserDirectory.GetAsync.
         Build AcceptedInvitationInfo.
4. Publish in-process Wolverine event UserSessionStarted (no subscribers in slice 9).
5. Return SessionStartResponse.
```

## 10. ADRs landed in slice 9

### 10.1 ADR-0099 — Distributed locking + leader-elected periodic tasks via Postgres advisory locks

**Status:** Accepted
**Category:** Platform Infrastructure
**Related:** ADR-0001 (PostgreSQL), ADR-0080 (Wolverine — durability deferred), ADR-0082 (modular monolith)

**Context.** Several upcoming work streams need periodic background work running safely across multiple application instances: invitation expiry (slice 9), notification dispatch retry (E-06a), scheduled re-scans (E-08), scorecard recompute (E-10), data retention purge (E-01.F-05), agent aggregation (E-15). Wolverine has durable scheduled messages but its persistence is explicitly deferred per ADR-0080.

**Decision.** Adopt Postgres session-level advisory locks (`pg_try_advisory_lock`) as the distributed-locking primitive. Provide three reusable building blocks: `IDistributedLock` abstraction in `Kartova.SharedKernel`, `PostgresAdvisoryLock` implementation in `Kartova.SharedKernel.Postgres`, and `LeaderElectedPeriodicService` base class in `Kartova.SharedKernel`. Periodic services declare a `LockName + Interval` and an `ExecuteLeaderWorkAsync` implementation; the base class handles timer + lock acquisition + scope creation + exception isolation.

**Consequences.**
- **Positive:** Locks auto-release on connection drop (no stale-lock recovery). Multi-instance safe. No new infrastructure (Postgres is already required). Reusable across every future periodic task. Doesn't force the Wolverine-persistence decision.
- **Negative:** Each tick opens a new connection per acquisition attempt; modest overhead at small scale, but at very high tick rates this could pressure the BYPASSRLS pool. Acceptable for hourly/daily ticks; alternative needed only if we end up with second-by-second leader work.
- **Upgrade path:** when Wolverine durability is enabled in a future slice, periodic work *can* migrate to Wolverine scheduled messages, but the existing primitives remain valid and can stay where they are — no forced migration.

### 10.2 ADR-0100 — Identity scope: strict one-email-per-tenant in a single KeyCloak realm

**Status:** Accepted
**Category:** Identity & Authorization
**Related:** ADR-0006 (KeyCloak as IdP), ADR-0011 (one Org = one tenant)

**Context.** Kartova runs a single KeyCloak realm (`kartova`) for all tenants; per-user `tenantId` attribute scopes membership. The realm setting `duplicateEmailsAllowed` defaults to `false`, which means a given email exists at most once in the realm — across all tenants. Slice 9's invitation flow needs a clear product decision on this.

**Decision.** Keep the strict model: **one email = one tenant**. The realm setting `duplicateEmailsAllowed: false` is preserved. Cross-tenant duplicate invitations surface as a 409 `email-already-on-platform` with a soft message ("This email already has a Kartova account in another organization") — accepting that the existence of the user across tenants is leaked, consistent with Atlassian/GitHub behavior.

**Consequences.**
- **Positive:** OIDC login is unambiguous (no org-picker). Invitation UX is simple. KeyCloak default preserved — no realm-config drift.
- **Negative:** Users who genuinely need access to multiple Kartova organizations must use separate email addresses (e.g. `alice@company.com` + `alice+orgb@company.com`). Industry pattern; acceptable.
- **Upgrade path:** if multi-tenant-per-user ever becomes a real product requirement, the correct response is **realm-per-tenant** (full isolation), not `duplicateEmailsAllowed: true` (which breaks OIDC login). That would be a new ADR superseding this one.

## 11. Testing

### 11.1 Architecture tests (CI gate)

`Kartova.ArchitectureTests` extends:

- `Kartova_SharedKernel_Identity_does_not_reference_AspNetCore`
- `Organization_owns_users_and_invitations_tables`
- `Catalog_does_not_reference_Organization_Domain` (verifies owner enrichment is via `IUserDirectory` only)
- `IDistributedLock_implementations_use_session_advisory_locks` (reflection check for `pg_try_advisory_lock` not `_xact_lock`)
- `OrganizationPermissionMatrixTests` (7×4 grid matching §5.2)
- Existing `Ts_snapshot_equals_csharp_KartovaPermissions_All` auto-catches the 7 new strings
- Existing `ContractsCoverageRules` enforces `[ExcludeFromCodeCoverage]` on all new DTOs

### 11.2 Unit tests

- **`Kartova.SharedKernel.Identity.Tests`** (new) — `KeycloakAdminClient` against `HttpMessageHandler` test double; each `KeycloakAdminError` branch + happy path.
- **`Kartova.SharedKernel.Tests`** — `LeaderElectedPeriodicService` against mock `IDistributedLock`: single-tick, skip-when-locked, exception isolation, cancellation, dispose-releases.
- **`Kartova.Organization.Domain.Tests`** — `Organization.UpdateProfile` validation, `OrgLogo.Create` size+mime, `Invitation` state transitions.
- **`Kartova.Organization.Application.Tests`** — `CreateInvitationHandler` with mocked `IKeycloakAdminClient`, partial-failure compensation (§9.2 step 5), three-way 409 error model.
- **`Kartova.Organization.Infrastructure.Tests`** — `OrganizationUserDirectory`, `ExpireInvitationsHostedService` against mocks.

### 11.3 Integration tests (Testcontainers)

`Kartova.Organization.IntegrationTests` — real KeyCloak + PostgreSQL containers.

- `PostgresAdvisoryLock_two_acquisitions_only_one_wins` (concurrent acquire from two contexts)
- `Invitation_create_persists_keycloak_user_and_db_row`
- `Invitation_create_returns_409_when_email_already_in_tenant`
- `Invitation_create_returns_409_when_email_already_pending_in_tenant`
- `Invitation_create_returns_409_when_email_exists_in_other_tenant`
- `Invitation_revoke_deletes_keycloak_user_and_flips_status`
- `Session_start_after_invitation_login_marks_accepted`
- `Session_start_subsequent_call_returns_no_accepted_invitation`
- `Expire_invitations_sweep_disables_keycloak_user_and_flips_status`
- `Logo_upload_with_svg_containing_script_returns_422`
- `Logo_upload_with_jpeg_returns_200_and_serve_returns_correct_bytes_and_etag`
- `Logo_upload_above_256_kb_returns_413`
- `User_search_typeahead_matches_displayname_and_email`
- `User_search_is_tenant_scoped_by_rls`
- `Cross_module_owner_enrichment_via_IUserDirectory`
- `Org_profile_update_with_invalid_timezone_returns_422`

### 11.4 Contract tests (Pact)

None in slice 9 (no new external consumers).

### 11.5 E2E (Playwright)

- `Invitation_happy_path` — OrgA admin invites user → copy link → log out → open link → set password → land on `/welcome` → continue to `/catalog`
- `Org_profile_logo_upload_visible_in_header` — upload PNG → header reflects new logo

Per ADR-0084, cold-start dev server before Playwright runs.

### 11.6 Mutation testing target

Per repo target ≥80% (`stryker-config.json`). Focus surfaces: `Invitation` state machine, `OrgLogo.Create` validation, `ExpireInvitationsHostedService`, `PostgresAdvisoryLock.Handle.DisposeAsync` catch-clauses.

## 12. Out of scope (deferred explicitly)

| Item | Owner / trigger |
|---|---|
| Real email delivery for invitations | E-06a Notification infrastructure |
| MinIO / object storage for logos + general assets | GDPR-export slice (E-01.F-05.S-03) |
| BrandColor on Organization | E-12 Status Page (Phase 4) |
| DefaultLocale + i18n | Future — no i18n consumer yet |
| Data residency tracking on Organization | E-01.F-05.S-08 |
| Notification policy defaults on Organization | E-06a.F-02 |
| Org-picker UX for multi-tenant-per-email | Realm-per-tenant ADR (likely never) |
| Full `Idempotency-Key` infrastructure | Future API hardening |
| TeamAdmin-scoped invitations (slice 9 = OrgAdmin only) | Follow-up if a story demands it |
| Wolverine durable persistence + scheduled messages | Future slice (ADR-0080 deferral preserved) |
| Email `VERIFY_EMAIL` required-action enforcement | E-06a Notification infrastructure |
| Avatar / user-profile image | Future user-profile slice; not in E-03 |
| `last_seen_at` write debouncing | Performance follow-up |
| Audit log entries for invitation create/revoke | E-01.F-03.S-03 Append-only audit log |
| Background sweep for orphaned KC users (step-6 role-assign failure) | Future hardening slice |
| Self-service org creation | E-09 Onboarding wizard |
| GDPR cascade on user deletion | E-01.F-05.S-04 |

## 13. Risks + open questions

| # | Risk | Mitigation in slice 9 |
|---|---|---|
| 1 | KeyCloak Admin client config (admin client + secret) drifts between dev `realm.json` and prod Helm values | Single `realm.json` edit, mirrored Helm template values; documented in `deploy/keycloak/README.md`. |
| 2 | Invitation-expiry sweep runs hourly → up to 1h ghost window where a "Pending but expired" invitation could be accepted | Acceptable for slice 9; documented. Future hardening: JWT-claim sync can enforce stricter rule. |
| 3 | `last_seen_at` write per request causes hot-row contention for busy users | Acceptable at MVP scale; debounce-to-5min follow-up tracked in §12. |
| 4 | SVG sanitization false-positives on legitimate designer-exported SVGs | 20% threshold is conservative; can drop if false-positives accumulate. |
| 5 | `Kartova.SharedKernel.Identity` becomes a junk drawer for cross-cutting people stuff | Today: 2 narrow interfaces. Split if it grows past ~4 interfaces. |
| 6 | Step-6 role-assignment failure leaves orphaned KC users | Try-catch + best-effort delete + 502. Persistent orphans are documented out-of-scope. |
| 7 | Logo `bytea` on the `organizations` row inflates list-shaped queries | Mitigation: dedicated `GET /me/logo` endpoint; the profile GET doesn't pull bytes. ADR-0011 caps Organization rows at one per tenant, so total table size is bounded. |

## 14. Definition of Done

Per CLAUDE.md, slice 9 is complete only when ALL of the following are green:

1. Full solution build with `TreatWarningsAsErrors=true` (0 warnings, 0 errors).
2. Per-task subagent reviews (spec-compliance + code-quality) executed.
3. `/superpowers:requesting-code-review` invoked at slice boundary against the full branch diff.
4. Full test suite green: architecture + unit + integration (Testcontainers).
5. `docker compose up` + real HTTP happy-path + one negative-path captured for invitation create, login, session bootstrap, logo upload, and user search (this is an HTTP/auth/DB slice — explicit Docker verification required).
6. `/simplify` skill run against the branch diff; findings addressed or explicitly skipped.
7. Mutation feedback loop on changed files; mutation score meets ≥80% target.
8. `/pr-review-toolkit:review-pr` skill.
9. `/deep-review` skill against branch diff with spec / plan / ADRs / tests; Blocking + Should-fix addressed.

Until all nine are green: **implementation staged, verification pending** — never "slice 9 complete".

---

*End of design — pending user review before transitioning to writing-plans.*
