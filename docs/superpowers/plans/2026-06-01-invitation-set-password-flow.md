# Invitation Set-Password Flow — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let an invited user set their own password on a Kartova-hosted page reached via an opaque, single-use token (email no longer in the URL), then log in through standard OIDC and land on `/welcome`.

**Architecture:** Keep the existing model (KeyCloak user created at invite time, acceptance flipped at first login in `SessionStartHandler`). Add a 256-bit random token (stored only as a SHA-256 hash on the `invitations` row, nulled on use), two anonymous BYPASSRLS endpoints (`GET`/`POST /api/v1/invitations/accept`), and a Kartova SPA page that collects password + display name and then triggers `signinRedirect({ login_hint })`. The accept endpoint sets the KC password via the admin API, clears `UPDATE_PASSWORD`, sets `emailVerified` + name, and burns the token — without touching invitation `Status`.

**Tech Stack:** .NET 10 / ASP.NET Core Minimal APIs · EF Core (Npgsql) · KeyCloak Admin REST · MSTest v4 + NSubstitute + Testcontainers · React 19 + TypeScript + react-oidc-context + openapi-fetch + zod + react-hook-form · Vitest + RTL.

**Spec:** [docs/superpowers/specs/2026-06-01-invitation-set-password-flow-design.md](../specs/2026-06-01-invitation-set-password-flow-design.md)

**Conventions:** Windows shell — prefix `dotnet`/`ef` with `cmd /c`. Solution: `Kartova.slnx`. Frontend in `web/`. Commit after every green step.

---

## File Structure

**Backend — new**
- `src/Modules/Organization/Kartova.Organization.Domain/InvitationToken.cs` — static issuance + SHA-256 hashing helper.
- `src/Modules/Organization/Kartova.Organization.Contracts/InvitationAcceptContext.cs` — GET-context DTO.
- `src/Modules/Organization/Kartova.Organization.Contracts/AcceptInvitationRequest.cs` — POST body DTO.
- `src/Modules/Organization/Kartova.Organization.Contracts/AcceptInvitationResponse.cs` — POST result DTO (`{ email }`).
- `src/Kartova.SharedKernel.Identity/UpdateKeycloakUserRequest.cs` — update DTO (in `KeycloakDtos.cs`, see Task 4).
- `src/Modules/Organization/Kartova.Organization.Infrastructure.Admin/AcceptInvitationHandler.cs` — GetContext + Accept logic on the bypass context.
- `src/Modules/Organization/Kartova.Organization.Infrastructure.Admin/InvitationAcceptRoutes.cs` — anonymous route mapping + delegates.
- `src/Modules/Organization/Kartova.Organization.Infrastructure/Migrations/<ts>_AddInvitationTokenColumns.cs` — generated.

**Backend — modified**
- `…/Organization.Domain/Invitation.cs` — `TokenHash`, `CredentialSetAt`; `Create(...)` takes `tokenHash`; `MarkCredentialSet(clock)`.
- `…/Organization.Infrastructure/InvitationEntityTypeConfiguration.cs` — map the two columns + partial-unique index.
- `…/Organization.Infrastructure/CreateInvitationHandler.cs` — issue token, persist hash, change URL.
- `src/Kartova.SharedKernel.Identity/IKeycloakAdminClient.cs` + `KeycloakAdminClient.cs` — `SetPasswordAsync`, `UpdateUserAsync`.
- `src/Modules/Organization/Kartova.Organization.Infrastructure.Admin/OrganizationAdminModule.cs` (or the module's endpoint wiring) — map `InvitationAcceptRoutes`; DI for `AcceptInvitationHandler`.
- `src/Kartova.Api/Program.cs` — rate limiter registration + `UseRateLimiter()`.
- `deploy/keycloak/kartova-realm.json` — `passwordPolicy` + `bruteForceProtected`.

**Frontend — new**
- `web/src/features/invitations/api/acceptInvitation.ts`
- `web/src/features/invitations/schemas/acceptInvitation.ts`
- `web/src/features/invitations/pages/AcceptInvitationPage.tsx`
- `web/src/features/invitations/api/anonymousClient.ts` — openapi-fetch client without auth middleware.
- test siblings under `__tests__/`.

**Frontend — modified**
- `web/src/app/router.tsx` — anonymous `/accept-invitation` route.
- `web/src/features/catalog/api/client.ts` — export `createAnonymousApiClient` (reuse the same `deferredFetch`).

---

## Task 1: `InvitationToken` issuance + hashing helper

**Files:**
- Create: `src/Modules/Organization/Kartova.Organization.Domain/InvitationToken.cs`
- Test: `src/Modules/Organization/Kartova.Organization.Tests/InvitationTokenTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using Kartova.Organization.Domain;

namespace Kartova.Organization.Tests;

[TestClass]
public sealed class InvitationTokenTests
{
    [TestMethod]
    public void Hash_is_deterministic_for_same_input()
    {
        Assert.AreEqual(InvitationToken.Hash("abc"), InvitationToken.Hash("abc"));
    }

    [TestMethod]
    public void Hash_differs_for_different_input()
    {
        Assert.AreNotEqual(InvitationToken.Hash("abc"), InvitationToken.Hash("abd"));
    }

    [TestMethod]
    public void Hash_is_base64url_of_sha256_44_chars()
    {
        // SHA-256 = 32 bytes → base64url without padding = 43 chars.
        Assert.AreEqual(43, InvitationToken.Hash("anything").Length);
        Assert.IsFalse(InvitationToken.Hash("anything").Contains('+'));
        Assert.IsFalse(InvitationToken.Hash("anything").Contains('/'));
        Assert.IsFalse(InvitationToken.Hash("anything").Contains('='));
    }

    [TestMethod]
    public void Issue_returns_distinct_high_entropy_plaintext_and_matching_hash()
    {
        var (p1, h1) = InvitationToken.Issue();
        var (p2, _) = InvitationToken.Issue();
        Assert.AreNotEqual(p1, p2);              // 256-bit randomness → never collides
        Assert.AreEqual(43, p1.Length);          // 32 bytes base64url
        Assert.AreEqual(InvitationToken.Hash(p1), h1);
    }
}
```

- [ ] **Step 2: Run the tests — verify they fail**

Run: `cmd /c dotnet test src/Modules/Organization/Kartova.Organization.Tests/Kartova.Organization.Tests.csproj --filter "FullyQualifiedName~InvitationTokenTests"`
Expected: FAIL — `InvitationToken` does not exist.

- [ ] **Step 3: Implement the helper**

```csharp
using System.Security.Cryptography;

namespace Kartova.Organization.Domain;

/// <summary>
/// Opaque single-use invitation token. The plaintext is delivered to the
/// invitee (copy-link URL); only <see cref="Hash"/> is ever persisted. 256-bit
/// CSPRNG entropy via <see cref="RandomNumberGenerator"/> — NOT a GUID, which
/// is neither contractually cryptographic nor safe to expose as a credential.
/// </summary>
public static class InvitationToken
{
    /// <summary>Generates a fresh (plaintext, hash) pair. Plaintext goes in the URL; hash is stored.</summary>
    public static (string Plaintext, string Hash) Issue()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var plaintext = Base64UrlNoPad(bytes);
        return (plaintext, Hash(plaintext));
    }

    /// <summary>Deterministic base64url(SHA-256(plaintext)) — used at issuance and validation.</summary>
    public static string Hash(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var digest = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(plaintext));
        return Base64UrlNoPad(digest);
    }

    private static string Base64UrlNoPad(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
```

- [ ] **Step 4: Run the tests — verify they pass**

Run: `cmd /c dotnet test src/Modules/Organization/Kartova.Organization.Tests/Kartova.Organization.Tests.csproj --filter "FullyQualifiedName~InvitationTokenTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.Domain/InvitationToken.cs src/Modules/Organization/Kartova.Organization.Tests/InvitationTokenTests.cs
git commit -m "feat(invitation): add opaque single-use InvitationToken helper"
```

---

## Task 2: `Invitation` entity — token fields + credential-set transition

**Files:**
- Modify: `src/Modules/Organization/Kartova.Organization.Domain/Invitation.cs`
- Test: `src/Modules/Organization/Kartova.Organization.Tests/InvitationTests.cs` (append)

- [ ] **Step 1: Write the failing tests** (append to the existing class; if the file uses a fixed clock helper, reuse it — otherwise `TimeProvider.System`)

```csharp
[TestMethod]
public void Create_stores_token_hash_and_leaves_credential_unset()
{
    var inv = Invitation.Create("a@b.com", KartovaRoles.Member, Guid.NewGuid(),
        Guid.NewGuid(), new TenantId(Guid.NewGuid()), TimeProvider.System, tokenHash: "HASH");
    Assert.AreEqual("HASH", inv.TokenHash);
    Assert.IsNull(inv.CredentialSetAt);
}

[TestMethod]
public void MarkCredentialSet_burns_token_and_stamps_time()
{
    var inv = Invitation.Create("a@b.com", KartovaRoles.Member, Guid.NewGuid(),
        Guid.NewGuid(), new TenantId(Guid.NewGuid()), TimeProvider.System, tokenHash: "HASH");
    inv.MarkCredentialSet(TimeProvider.System);
    Assert.IsNull(inv.TokenHash);            // single-use: link dies
    Assert.IsNotNull(inv.CredentialSetAt);
    Assert.AreEqual(InvitationStatus.Pending, inv.Status); // flip stays at login
}
```

- [ ] **Step 2: Run — verify fail**

Run: `cmd /c dotnet test src/Modules/Organization/Kartova.Organization.Tests/Kartova.Organization.Tests.csproj --filter "FullyQualifiedName~InvitationTests"`
Expected: FAIL — `Create` has no `tokenHash` parameter; `TokenHash`/`CredentialSetAt`/`MarkCredentialSet` missing.

- [ ] **Step 3: Modify the entity**

Add the two properties (after `RevokedAt`):
```csharp
    public string? TokenHash { get; private set; }
    public DateTimeOffset? CredentialSetAt { get; private set; }
```
Change the `Create` signature + body to accept and store `tokenHash`:
```csharp
    public static Invitation Create(
        string email, string role, Guid invitedByUserId,
        Guid keycloakUserId, TenantId tenantId, TimeProvider clock, string tokenHash)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (string.IsNullOrEmpty(tokenHash)) throw new ArgumentException("Token hash required.", nameof(tokenHash));
        ValidateEmail(email);
        if (!KartovaRoles.All.Contains(role))
            throw new ArgumentException("Unknown role.", nameof(role));
        var now = clock.GetUtcNow();
        return new Invitation
        {
            _id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = email.Trim().ToLowerInvariant(),
            Role = role,
            InvitedByUserId = invitedByUserId,
            InvitedAt = now,
            ExpiresAt = now.AddDays(7),
            Status = InvitationStatus.Pending,
            KeycloakUserId = keycloakUserId,
            TokenHash = tokenHash,
        };
    }
```
Add the transition (next to `MarkAccepted`):
```csharp
    /// <summary>Invitee set their credential via the accept token. Burns the token
    /// (single-use) but does NOT accept — Status flips at first login (spec §6/D6).</summary>
    public void MarkCredentialSet(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        CredentialSetAt = clock.GetUtcNow();
        TokenHash = null;
    }
```

- [ ] **Step 4: Run — verify pass**

Run: `cmd /c dotnet test src/Modules/Organization/Kartova.Organization.Tests/Kartova.Organization.Tests.csproj --filter "FullyQualifiedName~InvitationTests"`
Expected: PASS. (The existing `CreateInvitationHandler` call site now won't compile — fixed in Task 6; do not build the whole solution yet. Keep this task's verification scoped to the Domain test project, which compiles independently.)

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.Domain/Invitation.cs src/Modules/Organization/Kartova.Organization.Tests/InvitationTests.cs
git commit -m "feat(invitation): add token hash + credential-set transition to aggregate"
```

---

## Task 3: EF mapping + migration (columns + partial unique index)

**Files:**
- Modify: `src/Modules/Organization/Kartova.Organization.Infrastructure/InvitationEntityTypeConfiguration.cs`
- Create: `…/Kartova.Organization.Infrastructure/Migrations/<ts>_AddInvitationTokenColumns.cs` (generated)

- [ ] **Step 1: Map the new columns + index**

Append inside `Configure`, after the `RevokedAt` property:
```csharp
        b.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(64);
        b.Property(x => x.CredentialSetAt).HasColumnName("credential_set_at");

        // Global lookup key for the anonymous accept path; UNIQUE so a token maps
        // to at most one invitation. Partial (token_hash IS NOT NULL) so burned/
        // legacy NULL rows don't collide.
        b.HasIndex(x => x.TokenHash)
            .HasDatabaseName("idx_invitations_token_hash")
            .IsUnique()
            .HasFilter("token_hash IS NOT NULL");
```

- [ ] **Step 2: Generate the migration**

Run (mirror how prior Organization migrations were created — `Kartova.Organization.Infrastructure` owns the migrations folder and a design-time factory):
```
cmd /c dotnet ef migrations add AddInvitationTokenColumns ^
  --project src/Modules/Organization/Kartova.Organization.Infrastructure/Kartova.Organization.Infrastructure.csproj ^
  --startup-project src/Kartova.Migrator/Kartova.Migrator.csproj
```
Expected: a new `<ts>_AddInvitationTokenColumns.cs` with `AddColumn<string>("token_hash"…)`, `AddColumn<DateTimeOffset>("credential_set_at"…)`, and `CreateIndex(… "idx_invitations_token_hash" … unique … filter "token_hash IS NOT NULL")`. Open it and confirm those three operations are present and `Down()` reverses them.

- [ ] **Step 3: Verify the migration applies (Testcontainers migration test)**

If a migrations integration test exists for Organization (mirror `Catalog…Migrations/MigrationIntegrationTests.cs`), run it; otherwise verify against the running dev DB:
```
cmd /c dotnet build Kartova.slnx
```
Then bring the stack up (`docker compose up migrator`) or run the Organization migration integration test project. Expected: migrator applies cleanly, `\d invitations` shows `token_hash`, `credential_set_at`, and `idx_invitations_token_hash`.

- [ ] **Step 4: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.Infrastructure/InvitationEntityTypeConfiguration.cs src/Modules/Organization/Kartova.Organization.Infrastructure/Migrations/
git commit -m "feat(invitation): map token columns + partial unique token-hash index"
```

---

## Task 4: `IKeycloakAdminClient` — `SetPasswordAsync` + `UpdateUserAsync`

**Files:**
- Modify: `src/Kartova.SharedKernel.Identity/KeycloakDtos.cs` (add `UpdateKeycloakUserRequest`)
- Modify: `src/Kartova.SharedKernel.Identity/IKeycloakAdminClient.cs`
- Modify: `src/Kartova.SharedKernel.Identity/KeycloakAdminClient.cs`
- Test: `tests/Kartova.SharedKernel.Identity.IntegrationTests/KeycloakAdminClientIntegrationTests.cs` (append — real KC Testcontainer)

- [ ] **Step 1: Add the DTO**

In `KeycloakDtos.cs`:
```csharp
[ExcludeFromCodeCoverage]
public sealed record UpdateKeycloakUserRequest(
    string? FirstName,
    string? LastName,
    bool EmailVerified,
    IReadOnlyList<string> RequiredActions);
```

- [ ] **Step 2: Declare the interface members** (in `IKeycloakAdminClient.cs`, with XML docs mirroring the existing style)

```csharp
    /// <summary>Sets (resets) a realm user's password. Admin override — bypasses the
    /// realm password policy, so callers MUST validate strength themselves.</summary>
    /// <exception cref="KeycloakAdminException">NotFound (user gone) / Unauthorized / Unexpected.</exception>
    Task SetPasswordAsync(Guid userId, string password, bool temporary, CancellationToken ct);

    /// <summary>Partial-updates a realm user (emailVerified, requiredActions, name).
    /// Used to finalize an invited user after they set their password.</summary>
    /// <exception cref="KeycloakAdminException">NotFound (user gone) / Unauthorized / Unexpected.</exception>
    Task UpdateUserAsync(Guid userId, UpdateKeycloakUserRequest request, CancellationToken ct);
```

- [ ] **Step 3: Write the failing integration tests** (append; reuse the fixture's `IKeycloakAdminClient` + a freshly-created KC user id)

```csharp
[TestMethod]
public async Task SetPasswordAsync_then_UpdateUserAsync_finalizes_invited_user()
{
    var id = await _client.CreateUserAsync(new CreateKeycloakUserRequest(
        $"setpw-{Guid.NewGuid():N}@example.com", null, null, Guid.NewGuid().ToString(),
        new[] { KeycloakAdminRequiredActions.UpdatePassword }), CancellationToken.None);

    await _client.SetPasswordAsync(id, "Sup3rSecretPassw0rd!", temporary: false, CancellationToken.None);
    await _client.UpdateUserAsync(id, new UpdateKeycloakUserRequest("Jane Doe", null, EmailVerified: true,
        RequiredActions: Array.Empty<string>()), CancellationToken.None);

    var user = await _client.GetUserAsync(id, CancellationToken.None);
    Assert.IsNotNull(user);
    Assert.IsTrue(user!.EmailVerified);
    Assert.AreEqual("Jane Doe", user.FirstName);
}

[TestMethod]
public async Task SetPasswordAsync_throws_NotFound_for_unknown_user()
{
    var ex = await Assert.ThrowsExceptionAsync<KeycloakAdminException>(() =>
        _client.SetPasswordAsync(Guid.NewGuid(), "whatever12345", false, CancellationToken.None));
    Assert.AreEqual(KeycloakAdminError.NotFound, ex.Error);
}
```

- [ ] **Step 4: Run — verify fail**

Run: `cmd /c dotnet test tests/Kartova.SharedKernel.Identity.IntegrationTests/Kartova.SharedKernel.Identity.IntegrationTests.csproj --filter "FullyQualifiedName~SetPasswordAsync"`
Expected: FAIL — methods not implemented (won't compile until Step 5).

- [ ] **Step 5: Implement both methods** (in `KeycloakAdminClient.cs`, mirroring the existing token+error pattern)

```csharp
    public async Task SetPasswordAsync(Guid userId, string password, bool temporary, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);
        using var req = new HttpRequestMessage(HttpMethod.Put, $"/admin/realms/{_realm}/users/{userId}/reset-password")
        {
            Content = JsonContent.Create(new { type = "password", value = password, temporary }, options: JsonOpts),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await http.SendAsync(req, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            throw new KeycloakAdminException(KeycloakAdminError.NotFound, $"User {userId} not found.");
        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new KeycloakAdminException(KeycloakAdminError.Unauthorized, "Admin client unauthorized.");
        if (!resp.IsSuccessStatusCode)
            throw new KeycloakAdminException(KeycloakAdminError.Unexpected, $"KeyCloak reset-password returned {(int)resp.StatusCode}.");
    }

    public async Task UpdateUserAsync(Guid userId, UpdateKeycloakUserRequest request, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);
        using var req = new HttpRequestMessage(HttpMethod.Put, $"/admin/realms/{_realm}/users/{userId}")
        {
            Content = JsonContent.Create(new
            {
                firstName = request.FirstName,
                lastName = request.LastName,
                emailVerified = request.EmailVerified,
                requiredActions = request.RequiredActions,
            }, options: JsonOpts),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await http.SendAsync(req, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            throw new KeycloakAdminException(KeycloakAdminError.NotFound, $"User {userId} not found.");
        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new KeycloakAdminException(KeycloakAdminError.Unauthorized, "Admin client unauthorized.");
        if (!resp.IsSuccessStatusCode)
            throw new KeycloakAdminException(KeycloakAdminError.Unexpected, $"KeyCloak update-user returned {(int)resp.StatusCode}.");
    }
```

- [ ] **Step 6: Run — verify pass**

Run: `cmd /c dotnet test tests/Kartova.SharedKernel.Identity.IntegrationTests/Kartova.SharedKernel.Identity.IntegrationTests.csproj --filter "FullyQualifiedName~SetPasswordAsync|FullyQualifiedName~UpdateUser"`
Expected: PASS (requires Docker for the KC Testcontainer).

- [ ] **Step 7: Commit**

```bash
git add src/Kartova.SharedKernel.Identity/ tests/Kartova.SharedKernel.Identity.IntegrationTests/KeycloakAdminClientIntegrationTests.cs
git commit -m "feat(identity): add SetPasswordAsync + UpdateUserAsync to KeyCloak admin client"
```

---

## Task 5: Contracts DTOs

**Files:**
- Create: `…/Organization.Contracts/InvitationAcceptContext.cs`
- Create: `…/Organization.Contracts/AcceptInvitationRequest.cs`
- Create: `…/Organization.Contracts/AcceptInvitationResponse.cs`

- [ ] **Step 1: Write the DTOs** (every Contracts type carries `[ExcludeFromCodeCoverage]` — enforced by `ContractsCoverageRules`)

`InvitationAcceptContext.cs`:
```csharp
using System.Diagnostics.CodeAnalysis;

namespace Kartova.Organization.Contracts;

[ExcludeFromCodeCoverage]
public sealed record InvitationAcceptContext(
    string OrgDisplayName,
    string InvitedByDisplayName,
    string Email,
    string DefaultDisplayName,
    string Role,
    DateTimeOffset ExpiresAt);
```
`AcceptInvitationRequest.cs`:
```csharp
using System.Diagnostics.CodeAnalysis;

namespace Kartova.Organization.Contracts;

[ExcludeFromCodeCoverage]
public sealed record AcceptInvitationRequest(string Token, string Password, string DisplayName);
```
`AcceptInvitationResponse.cs`:
```csharp
using System.Diagnostics.CodeAnalysis;

namespace Kartova.Organization.Contracts;

[ExcludeFromCodeCoverage]
public sealed record AcceptInvitationResponse(string Email);
```

- [ ] **Step 2: Build the Contracts + Architecture test projects**

Run: `cmd /c dotnet build src/Modules/Organization/Kartova.Organization.Contracts/Kartova.Organization.Contracts.csproj`
Then: `cmd /c dotnet test tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj --filter "FullyQualifiedName~ContractsCoverage"`
Expected: PASS (the `[ExcludeFromCodeCoverage]` attributes satisfy the rule).

- [ ] **Step 3: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.Contracts/
git commit -m "feat(invitation): add accept-context + accept request/response contracts"
```

---

## Task 6: `CreateInvitationHandler` — issue token, store hash, change URL

**Files:**
- Modify: `src/Modules/Organization/Kartova.Organization.Infrastructure/CreateInvitationHandler.cs`
- Test: `src/Modules/Organization/Kartova.Organization.Infrastructure.Tests/CreateInvitationHandlerTests.cs`

- [ ] **Step 1: Update/Write the failing test** — assert the URL shape and that a hash is persisted

```csharp
[TestMethod]
public async Task Create_returns_tokenized_url_and_persists_hash()
{
    // ...arrange handler with the existing test harness (NSubstitute KC client returns a kc id,
    // in-memory/Testcontainer DbContext, FrontendBaseUrl = "http://localhost:5173")...
    var result = await handler.HandleAsync(new CreateInvitationRequest("alice@example.com", KartovaRoles.Member), CancellationToken.None);

    var created = (CreateInvitationResult.Created)result;
    StringAssert.StartsWith(created.Response.InviteUrl, "http://localhost:5173/accept-invitation?token=");
    StringAssert.DoesNotMatch(created.Response.InviteUrl, new System.Text.RegularExpressions.Regex("email=|invitation=1"));

    var token = created.Response.InviteUrl.Split("token=")[1];
    var saved = await db.Invitations.SingleAsync();
    Assert.AreEqual(InvitationToken.Hash(token), saved.TokenHash);   // stored hash matches the URL token
    Assert.IsNull(saved.CredentialSetAt);
}
```
(Update any existing assertion in this file that referenced the old `?invitation=1&email=` URL or the 6-arg `Invitation.Create`.)

- [ ] **Step 2: Run — verify fail**

Run: `cmd /c dotnet test src/Modules/Organization/Kartova.Organization.Infrastructure.Tests/Kartova.Organization.Infrastructure.Tests.csproj --filter "FullyQualifiedName~CreateInvitationHandler"`
Expected: FAIL (old URL / `Create` arity).

- [ ] **Step 3: Modify the handler** — issue the token before `Invitation.Create`, pass the hash, build the new URL

Replace the `Invitation.Create(...)` call and the `inviteUrl` construction:
```csharp
        var (tokenPlaintext, tokenHash) = InvitationToken.Issue();
        var invitation = Invitation.Create(
            email, request.Role, currentUser.UserId, kcId, tenant.Id, clock, tokenHash);
        db.Invitations.Add(invitation);
        db.Users.Add(new User { Id = kcId, TenantId = tenant.Id, Email = email, DisplayName = email, CreatedAt = clock.GetUtcNow() });
        // ...existing SaveChangesAsync + 23505 compensation block unchanged...
```
And the URL (replace lines ~186-187):
```csharp
        var inviteUrl =
            $"{options.Value.FrontendBaseUrl}/accept-invitation?token={Uri.EscapeDataString(tokenPlaintext)}";
```
Add `using Kartova.Organization.Domain;` if not already present (it is). Keep `tokenPlaintext` in a local only — never log it.

- [ ] **Step 4: Run — verify pass**

Run: `cmd /c dotnet test src/Modules/Organization/Kartova.Organization.Infrastructure.Tests/Kartova.Organization.Infrastructure.Tests.csproj --filter "FullyQualifiedName~CreateInvitationHandler"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.Infrastructure/CreateInvitationHandler.cs src/Modules/Organization/Kartova.Organization.Infrastructure.Tests/CreateInvitationHandlerTests.cs
git commit -m "feat(invitation): issue tokenized accept URL, persist token hash"
```

---

## Task 7: `AcceptInvitationHandler` (BYPASSRLS) — context + accept

**Files:**
- Create: `…/Organization.Infrastructure.Admin/AcceptInvitationHandler.cs`
- Test: `…/Organization.Infrastructure.Admin.Tests/AcceptInvitationHandlerTests.cs` (create test project file if the Admin test project exists; otherwise add to `…Infrastructure.Tests` using an `AdminOrganizationDbContext` built on the same provider as `CreateInvitationHandlerTests`)

**Password policy:** 12–128 chars (NIST 800-63B; no composition). Define a private const in the handler and mirror it in zod (Task 12).

- [ ] **Step 1: Write the failing tests** (NSubstitute `IKeycloakAdminClient`; seed an `Invitation` via the bypass context using `Invitation.Create(..., tokenHash: InvitationToken.Hash("TOK"))`)

```csharp
// happy path
[TestMethod]
public async Task AcceptAsync_sets_password_clears_action_burns_token_keeps_pending()
{
    var kc = Substitute.For<IKeycloakAdminClient>();
    var inv = Seed("TOK", expiresInDays: 7);                 // helper inserts a Pending invitation
    var handler = NewHandler(kc);

    var email = await handler.AcceptAsync("TOK", "Sup3rSecretPassw0rd!", "Jane Doe", CancellationToken.None);

    Assert.AreEqual(inv.Email, email);
    await kc.Received(1).SetPasswordAsync(inv.KeycloakUserId!.Value, "Sup3rSecretPassw0rd!", false, Arg.Any<CancellationToken>());
    await kc.Received(1).UpdateUserAsync(inv.KeycloakUserId!.Value,
        Arg.Is<UpdateKeycloakUserRequest>(r => r.EmailVerified && r.FirstName == "Jane Doe" && r.RequiredActions.Count == 0),
        Arg.Any<CancellationToken>());
    var saved = await Db.Invitations.SingleAsync();
    Assert.IsNull(saved.TokenHash);
    Assert.IsNotNull(saved.CredentialSetAt);
    Assert.AreEqual(InvitationStatus.Pending, saved.Status);
}

[TestMethod]
public async Task GetContextAsync_returns_context_for_valid_token() { /* asserts OrgDisplayName/email/role */ }

[DataTestMethod]
[DataRow("")]                 // unknown token  → NotFound
public async Task GetContextAsync_unknown_token_throws_NotFound(string _) { /* Assert AcceptInvitationError.NotFound */ }

[TestMethod]
public async Task AcceptAsync_expired_token_throws_Gone_expired() { /* seed ExpiresAt in past */ }

[TestMethod]
public async Task AcceptAsync_already_used_token_throws_Gone_alreadyUsed() { /* seed with TokenHash=null */ }

[TestMethod]
public async Task AcceptAsync_short_password_throws_Validation() { /* "short" → AcceptInvitationError.Validation */ }

[TestMethod]
public async Task AcceptAsync_kc_user_gone_throws_Gone() { kc.SetPasswordAsync(...).Throws(new KeycloakAdminException(KeycloakAdminError.NotFound,"")); /* → Gone */ }
```

- [ ] **Step 2: Run — verify fail**

Run: `cmd /c dotnet test <the chosen Admin/Infrastructure test project> --filter "FullyQualifiedName~AcceptInvitationHandler"`
Expected: FAIL — handler missing.

- [ ] **Step 3: Implement the handler**

```csharp
using Kartova.Organization.Contracts;
using Kartova.Organization.Domain;
using Kartova.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure.Admin;

public abstract record AcceptInvitationResult
{
    public sealed record Ok(string Email) : AcceptInvitationResult;
    public sealed record Failed(AcceptInvitationError Error) : AcceptInvitationResult;
}

public enum AcceptInvitationError { NotFound, GoneExpired, GoneRevoked, GoneAlreadyUsed, Validation, Upstream }

public sealed class AcceptInvitationHandler(
    AdminOrganizationDbContext db,
    IKeycloakAdminClient kc,
    TimeProvider clock)
{
    private const int MinPasswordLength = 12;
    private const int MaxPasswordLength = 128;

    public async Task<InvitationAcceptContext?> GetContextAsync(string token, CancellationToken ct)
    {
        var (inv, error) = await ResolveAsync(token, ct);
        if (inv is null) return null; // caller maps `error` to 404/410
        var org = await db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.TenantId == inv.TenantId, ct);
        var inviter = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == inv.InvitedByUserId, ct);
        var localPart = inv.Email.Split('@')[0];
        return new InvitationAcceptContext(
            OrgDisplayName: org?.DisplayName ?? "",
            InvitedByDisplayName: inviter?.DisplayName ?? inv.Email,
            Email: inv.Email,
            DefaultDisplayName: localPart,
            Role: inv.Role,
            ExpiresAt: inv.ExpiresAt);
    }

    public async Task<AcceptInvitationResult> AcceptAsync(string token, string password, string displayName, CancellationToken ct)
    {
        var (inv, error) = await ResolveAsync(token, ct);
        if (inv is null) return new AcceptInvitationResult.Failed(error!.Value);

        var trimmedName = (displayName ?? "").Trim();
        if (password is null || password.Length < MinPasswordLength || password.Length > MaxPasswordLength
            || trimmedName.Length is < 1 or > 128)
            return new AcceptInvitationResult.Failed(AcceptInvitationError.Validation);

        var kcId = inv.KeycloakUserId!.Value;
        try
        {
            await kc.SetPasswordAsync(kcId, password, temporary: false, ct);
            await kc.UpdateUserAsync(kcId, new UpdateKeycloakUserRequest(trimmedName, null, EmailVerified: true, RequiredActions: Array.Empty<string>()), ct);
        }
        catch (KeycloakAdminException ex) when (ex.Error == KeycloakAdminError.NotFound)
        {
            return new AcceptInvitationResult.Failed(AcceptInvitationError.GoneAlreadyUsed);
        }

        // Compare-and-swap: only the first concurrent caller burns the token.
        var rows = await db.Invitations
            .Where(i => i.TokenHash == inv.TokenHash)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.CredentialSetAt, clock.GetUtcNow())
                .SetProperty(i => i.TokenHash, (string?)null), ct);
        if (rows == 0) return new AcceptInvitationResult.Failed(AcceptInvitationError.GoneAlreadyUsed);

        return new AcceptInvitationResult.Ok(inv.Email);
    }

    /// <summary>Resolve + validate a token. Returns (invitation, null) when valid,
    /// or (null, error) for the not-found / gone reasons.</summary>
    private async Task<(Invitation?, AcceptInvitationError?)> ResolveAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token)) return (null, AcceptInvitationError.NotFound);
        var hash = InvitationToken.Hash(token);
        var inv = await db.Invitations.FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
        if (inv is null) return (null, AcceptInvitationError.NotFound);
        if (inv.Status == InvitationStatus.Revoked) return (null, AcceptInvitationError.GoneRevoked);
        if (inv.Status != InvitationStatus.Pending) return (null, AcceptInvitationError.GoneAlreadyUsed);
        if (inv.ExpiresAt <= clock.GetUtcNow()) return (null, AcceptInvitationError.GoneExpired);
        return (inv, null);
    }
}
```
> Note on the CAS: `ExecuteUpdateAsync` with `WHERE token_hash = @hash` won't match once burned (hash nulled), so a duplicate POST returns `GoneAlreadyUsed`. Because `ResolveAsync` already loaded a tracked entity, run the `ExecuteUpdate` on the same `DbContext` (it issues raw SQL, no tracking conflict).

- [ ] **Step 4: Run — verify pass**

Run: `cmd /c dotnet test <test project> --filter "FullyQualifiedName~AcceptInvitationHandler"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.Infrastructure.Admin/AcceptInvitationHandler.cs <test file>
git commit -m "feat(invitation): add AcceptInvitationHandler (set password, burn token, keep pending)"
```

---

## Task 8: Anonymous routes + DI + rate limiter + Referrer-Policy

**Files:**
- Create: `…/Organization.Infrastructure.Admin/InvitationAcceptRoutes.cs`
- Modify: `…/Organization.Infrastructure.Admin/OrganizationAdminModule.cs` (map the routes) + the module's DI registration (register `AcceptInvitationHandler`)
- Modify: `src/Kartova.Api/Program.cs` (rate limiter + `UseRateLimiter()`)

- [ ] **Step 1: Write the routes + delegates**

```csharp
using Kartova.Organization.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kartova.Organization.Infrastructure.Admin;

internal static class InvitationAcceptRoutes
{
    public const string RateLimitPolicy = "invitation-accept";

    public static void MapTo(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/invitations")
            .RequireRateLimiting(RateLimitPolicy);   // anonymous: no RequireAuthorization, no RequireTenantScope

        group.MapGet("/accept", GetContextAsync)
            .WithName("GetInvitationAcceptContext")
            .Produces<InvitationAcceptContext>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status410Gone);

        group.MapPost("/accept", AcceptAsync)
            .WithName("AcceptInvitation")
            .Produces<AcceptInvitationResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status410Gone);
    }

    private static async Task<IResult> GetContextAsync(string token, AcceptInvitationHandler handler, HttpContext ctx, CancellationToken ct)
    {
        ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
        var context = await handler.GetContextAsync(token, ct);
        return context is null ? Results.NotFound() : Results.Ok(context);
    }

    private static async Task<IResult> AcceptAsync(AcceptInvitationRequest body, AcceptInvitationHandler handler, HttpContext ctx, CancellationToken ct)
    {
        ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
        var result = await handler.AcceptAsync(body.Token, body.Password, body.DisplayName, ct);
        return result switch
        {
            AcceptInvitationResult.Ok ok => Results.Ok(new AcceptInvitationResponse(ok.Email)),
            AcceptInvitationResult.Failed { Error: AcceptInvitationError.Validation } => Results.Problem(statusCode: 400, title: "Password or display name invalid."),
            AcceptInvitationResult.Failed { Error: AcceptInvitationError.NotFound } => Results.NotFound(),
            AcceptInvitationResult.Failed { Error: AcceptInvitationError.Upstream } => Results.Problem(statusCode: 502, title: "Identity provider error."),
            AcceptInvitationResult.Failed f => Results.Problem(statusCode: 410, title: f.Error.ToString()),
            _ => Results.Problem(statusCode: 500),
        };
    }
}
```
> The `GET` reason-detail (`expired`/`revoked`/`alreadyUsed`) is carried in the `410` problem title via the same `AcceptInvitationError` mapping — apply the identical `switch` arm to the GET path if you want reason-specific GET 410s (the spec shows `410 {reason}` for GET; reuse `handler.GetContextAsync` returning the error instead of null — optional refinement, keep null→404/410 split consistent with the POST switch).

- [ ] **Step 2: Map the routes + register the handler**

In `OrganizationAdminModule.MapEndpoints`, after the existing admin mapping, add:
```csharp
        InvitationAcceptRoutes.MapTo(app);
```
In the Organization module's service registration (wherever `AdminOrganizationDbContext` consumers are registered — mirror `AdminOrganizationCommands`), add:
```csharp
        services.AddScoped<AcceptInvitationHandler>();
```

- [ ] **Step 3: Add the rate limiter in `Program.cs`**

Where services are configured:
```csharp
builder.Services.AddRateLimiter(o =>
{
    o.AddFixedWindowLimiter(InvitationAcceptRoutes.RateLimitPolicy, opt =>
    {
        opt.PermitLimit = 10;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });
});
```
In the middleware pipeline (after routing/auth, before endpoints):
```csharp
app.UseRateLimiter();
```

- [ ] **Step 4: Build + run the full unit/arch suite**

Run: `cmd /c dotnet build Kartova.slnx` then `cmd /c dotnet test tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj`
Expected: 0 warnings/errors; arch tests green (no module-boundary violations; Contracts coverage rule satisfied).

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.Infrastructure.Admin/ src/Kartova.Api/Program.cs
git commit -m "feat(invitation): map anonymous accept endpoints + rate limit + no-referrer"
```

---

## Task 9: Integration tests for the accept endpoints

**Files:**
- Test: `src/Modules/Organization/Kartova.Organization.IntegrationTests/InvitationAcceptTests.cs`

- [ ] **Step 1: Write the integration tests** (real Postgres + real KC, mirroring `InvitationTests`/`SessionBootstrapTests`; use the bypass connection to seed a Pending invitation with a known token, and the existing KC fixture)

```csharp
// 1. GET /accept?token=<valid> → 200 with org/email/role
// 2. GET /accept?token=<unknown> → 404
// 3. GET /accept?token=<expired> → 410
// 4. POST /accept {valid token, strong pw, name} → 200 {email};
//    DB: token_hash NULL, credential_set_at set, status still Pending;
//    KC: GetUserAsync shows emailVerified true, firstName set, no UPDATE_PASSWORD.
// 5. POST /accept twice (same token) → first 200, second 410.
// 6. POST /accept {valid token, "short"} → 400.
// 7. Full chain: POST accept → obtain KC token via password grant for the email →
//    POST /api/v1/auth/session → SessionStartResponse.AcceptedInvitation != null AND invitation now Accepted.
```

- [ ] **Step 2: Run — verify they pass (build first)**

Run: `cmd /c dotnet test src/Modules/Organization/Kartova.Organization.IntegrationTests/Kartova.Organization.IntegrationTests.csproj --filter "FullyQualifiedName~InvitationAccept"`
Expected: PASS (requires Docker).

- [ ] **Step 3: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.IntegrationTests/InvitationAcceptTests.cs
git commit -m "test(invitation): integration coverage for accept endpoints + accept→login chain"
```

---

## Task 10: Realm hardening

**Files:**
- Modify: `deploy/keycloak/kartova-realm.json`

- [ ] **Step 1: Add the policy keys** at the realm root:
```json
"passwordPolicy": "length(12)",
"bruteForceProtected": true,
```

- [ ] **Step 2: Verify import** — `docker compose down -v && docker compose up -d keycloak` then confirm the realm imports without error (`docker compose logs keycloak | grep -i imported`).

- [ ] **Step 3: Commit**

```bash
git add deploy/keycloak/kartova-realm.json
git commit -m "chore(keycloak): enforce min-12 password policy + brute-force protection"
```

---

## Task 11: Frontend — regenerate types + anonymous API client + accept API module

**Files:**
- Modify: `web/src/features/catalog/api/client.ts` (export anonymous client factory)
- Create: `web/src/features/invitations/api/anonymousClient.ts`
- Create: `web/src/features/invitations/api/acceptInvitation.ts`
- Test: `web/src/features/invitations/api/__tests__/acceptInvitation.test.tsx`

- [ ] **Step 1: Regenerate OpenAPI types** (API must expose the new endpoints — Task 8 done; bring the API up, then codegen)

Run: `docker compose up -d api` then `cd web && npm run codegen`
Expected: `web/src/generated/openapi.ts` now contains `/api/v1/invitations/accept` paths + `InvitationAcceptContext` / `AcceptInvitationRequest` / `AcceptInvitationResponse` schemas. Commit the regenerated file with this task.

- [ ] **Step 2: Export an anonymous client factory** — in `client.ts` add (reusing `deferredFetch`, NO `authMiddleware`):
```typescript
export function createAnonymousApiClient(baseUrl: string) {
  return createClient<paths>({ baseUrl, fetch: deferredFetch });
}
```

- [ ] **Step 3: Anonymous client instance** — `anonymousClient.ts`:
```typescript
import { API_BASE_URL, createAnonymousApiClient } from "@/features/catalog/api/client";

/** No Authorization header — the invitee has no session; the token is the only credential. */
export const anonymousApiClient = createAnonymousApiClient(API_BASE_URL);
```

- [ ] **Step 4: Write the failing API-module test** (asserts no Authorization header + status-aware errors)
```typescript
// mocks globalThis.fetch; calls getInvitationAcceptContext("TOK");
// asserts the request had no "Authorization" header and the URL is …/api/v1/invitations/accept?token=TOK
// asserts acceptInvitation(...) returns { email } on 200 and throws with __status on 410/400.
```

- [ ] **Step 5: Run — verify fail** → `cd web && npx vitest run src/features/invitations/api`

- [ ] **Step 6: Implement `acceptInvitation.ts`**
```typescript
import { anonymousApiClient } from "./anonymousClient";
import { throwWithStatus, unwrapData } from "@/shared/api/openapi-fetch-helpers";
import type { components } from "@/generated/openapi";

export type InvitationAcceptContext = components["schemas"]["InvitationAcceptContext"];

export async function getInvitationAcceptContext(token: string): Promise<InvitationAcceptContext> {
  const { data, error, response } = await anonymousApiClient.GET("/api/v1/invitations/accept", {
    params: { query: { token } },
  });
  if (error) throwWithStatus(error, response);
  return unwrapData(data);
}

export async function acceptInvitation(input: { token: string; password: string; displayName: string }): Promise<{ email: string }> {
  const { data, error, response } = await anonymousApiClient.POST("/api/v1/invitations/accept", { body: input });
  if (error) throwWithStatus(error, response);
  return unwrapData(data);
}
```

- [ ] **Step 7: Run — verify pass** → `cd web && npx vitest run src/features/invitations/api`

- [ ] **Step 8: Commit**
```bash
git add web/src/generated/openapi.ts web/src/features/catalog/api/client.ts web/src/features/invitations/api/
git commit -m "feat(web): anonymous accept-invitation API client + regenerated types"
```

---

## Task 12: Frontend — zod schema

**Files:**
- Create: `web/src/features/invitations/schemas/acceptInvitation.ts`
- Test: `web/src/features/invitations/schemas/__tests__/acceptInvitation.test.ts`

- [ ] **Step 1: Write failing schema tests** (mirror `inviteUser.test.ts`): password 12–128 ok; <12 fails; confirm-mismatch fails; displayName empty fails; >128 fails.

- [ ] **Step 2: Run — verify fail** → `cd web && npx vitest run src/features/invitations/schemas`

- [ ] **Step 3: Implement the schema** (policy mirrors the backend handler's 12–128)
```typescript
import { z } from "zod";

export const acceptInvitationSchema = z
  .object({
    password: z.string().min(12, "Password must be at least 12 characters.").max(128, "Password too long."),
    confirmPassword: z.string(),
    displayName: z.string().trim().min(1, "Display name is required.").max(128, "Display name too long."),
  })
  .refine((v) => v.password === v.confirmPassword, {
    message: "Passwords do not match.",
    path: ["confirmPassword"],
  });

export type AcceptInvitationInput = z.infer<typeof acceptInvitationSchema>;
```

- [ ] **Step 4: Run — verify pass** → `cd web && npx vitest run src/features/invitations/schemas`

- [ ] **Step 5: Commit**
```bash
git add web/src/features/invitations/schemas/
git commit -m "feat(web): accept-invitation zod schema (12-128 policy + confirm match)"
```

---

## Task 13: Frontend — `AcceptInvitationPage` + route

**Files:**
- Create: `web/src/features/invitations/pages/AcceptInvitationPage.tsx`
- Modify: `web/src/app/router.tsx`
- Test: `web/src/features/invitations/pages/__tests__/AcceptInvitationPage.test.tsx`

- [ ] **Step 1: Write failing component tests** (RTL + mocked api module + mocked `react-oidc-context`):
  - missing token → "invalid link", no fetch.
  - GET 200 → renders org name, "Invited by …", read-only email, prefilled display name, role badge, two password inputs + Save.
  - GET 404 → invalid message; GET 410 expired/used → reason message.
  - submit valid → calls `acceptInvitation` then `auth.signinRedirect({ login_hint: <email> })`.
  - submit when POST throws `__status===410` → gone state; `__status===400` → field error.

- [ ] **Step 2: Run — verify fail** → `cd web && npx vitest run src/features/invitations/pages`

- [ ] **Step 3: Implement the page** (centered card like `WelcomePage`; react-hook-form + zodResolver; Untitled UI inputs/Button; password show/hide input)
```tsx
import { useEffect, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { useAuth } from "react-oidc-context";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";

import { acceptInvitationSchema, type AcceptInvitationInput } from "@/features/invitations/schemas/acceptInvitation";
import { getInvitationAcceptContext, acceptInvitation, type InvitationAcceptContext } from "@/features/invitations/api/acceptInvitation";
// ...Untitled UI imports: Input, password Input, Button, Badge...

type LoadState =
  | { kind: "loading" }
  | { kind: "invalid" }
  | { kind: "gone"; reason: string }
  | { kind: "ready"; ctx: InvitationAcceptContext };

export function AcceptInvitationPage() {
  const [params] = useSearchParams();
  const token = params.get("token") ?? "";
  const auth = useAuth();
  const [state, setState] = useState<LoadState>({ kind: "loading" });

  const form = useForm<AcceptInvitationInput>({ resolver: zodResolver(acceptInvitationSchema) });

  useEffect(() => {
    if (!token) { setState({ kind: "invalid" }); return; }
    let active = true;
    void (async () => {
      try {
        const ctx = await getInvitationAcceptContext(token);
        if (!active) return;
        setState({ kind: "ready", ctx });
        form.reset({ password: "", confirmPassword: "", displayName: ctx.defaultDisplayName });
      } catch (e) {
        if (!active) return;
        const status = (e as { __status?: number }).__status;
        setState(status === 410 ? { kind: "gone", reason: "This invitation can no longer be used." } : { kind: "invalid" });
      }
    })();
    return () => { active = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token]);

  if (state.kind === "loading") return /* spinner */ null;
  if (state.kind === "invalid") return /* "This invitation link is invalid." card */ null;
  if (state.kind === "gone") return /* state.reason + "try signing in" card */ null;

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      const { email } = await acceptInvitation({ token, password: values.password, displayName: values.displayName });
      await auth.signinRedirect({ login_hint: email });
    } catch (e) {
      const status = (e as { __status?: number }).__status;
      if (status === 400) form.setError("password", { message: "Password does not meet requirements." });
      else if (status === 410) setState({ kind: "gone", reason: "This invitation can no longer be used." });
      else form.setError("root", { message: "Something went wrong. Please try again." });
    }
  });

  const { ctx } = state;
  return (
    /* centered card:
       h1 "Join {ctx.orgDisplayName}", subtitle "{ctx.invitedByDisplayName} invited you",
       read-only email field = ctx.email, role badge = ctx.role,
       displayName input, password input (show/hide), confirmPassword input,
       Button "Set password & continue" → onSubmit */
    null
  );
}
```
(Fill the JSX with the Untitled UI components per `WelcomePage`/`InviteUserDialog` patterns; the test assertions in Step 1 define the exact text/roles to render.)

- [ ] **Step 4: Add the route** — in `router.tsx`, import the page and add **outside** `<ProtectedShell>` (next to `/welcome`):
```tsx
<Route path="/accept-invitation" element={<AcceptInvitationPage />} />
```

- [ ] **Step 5: Run — verify pass** → `cd web && npx vitest run src/features/invitations`

- [ ] **Step 6: Commit**
```bash
git add web/src/features/invitations/pages/ web/src/app/router.tsx
git commit -m "feat(web): AcceptInvitationPage + anonymous /accept-invitation route"
```

---

## Task 14: Update the invite dialog copy (token link)

**Files:**
- Modify: `web/src/features/organization/components/CopyInviteLinkBox.tsx` and/or `InviteUserDialog.tsx` (only if they hard-code copy that references the old URL/email-sharing wording)
- Test: update the affected component tests.

- [ ] **Step 1:** Grep for any UI copy asserting `?invitation=1` / "email" in the share box; update wording to "Share this link. It lets them set a password and join. Expires in 7 days." The link value already comes from the backend `inviteUrl` (now tokenized), so no logic change — only copy + any snapshot/test expectations.
- [ ] **Step 2:** Run the affected tests → `cd web && npx vitest run src/features/organization/components`
- [ ] **Step 3:** Commit → `git commit -m "chore(web): invite dialog copy for tokenized link"`

---

## Task 15: Definition-of-Done verification (slice boundary)

Per `CLAUDE.md` DoD. Each sub-step must be citable by command + output.

- [ ] **Build:** `cmd /c dotnet build Kartova.slnx` — 0 warnings, 0 errors (`TreatWarningsAsErrors=true`).
- [ ] **Full backend suite:** `cmd /c dotnet test Kartova.slnx --configuration Release` — unit + architecture + integration (Docker up) green.
- [ ] **Frontend suite:** `cd web && npm run test` + `npm run typecheck` + `npm run lint` — green.
- [ ] **Real-HTTP E2E (DoD step 5):** `docker compose up -d` → in the app: invite a user (OrgAdmin) → copy link → open `/accept-invitation?token=…` → set password + name → redirected to KC login → log in → land on `/welcome`. Then negative path: reopen the same link → "can no longer be used"; open an expired/garbage token → invalid. Capture Playwright snapshots/console (0 errors).
- [ ] **/simplify** against the branch diff — address should-fix reuse/quality/efficiency items or note skips.
- [ ] **Mutation (DoD step 7):** `/misc:mutation-sentinel` on changed files (`InvitationToken`, `Invitation`, `AcceptInvitationHandler`, `CreateInvitationHandler`, KC client additions) → `/misc:test-generator` until ≥80%. Document score + accepted survivors.
- [ ] **Reviews:** per-task spec+quality reviews; `/superpowers:requesting-code-review` on the full branch diff; `/pr-review-toolkit:review-pr`; `/deep-review`. Address Blocking + Should-fix.
- [ ] **Checklist:** tick the completed accept-flow item(s) in `docs/product/CHECKLIST.md`.

---

## Self-Review (author checklist — completed)

**Spec coverage:** §4 token model → Tasks 1–3; §5 endpoints/handler/KC client → Tasks 4,5,7,8; §6 flip + display-name → Tasks 6,7 (+ unchanged `SessionStartHandler`, covered by Task 9 chain test); §7 security (policy/rate-limit/no-referrer/CAS/emailVerified) → Tasks 7,8,10; §8 frontend → Tasks 11–14; §9 testing → Tasks 1–9,11–13,15; §10 files → all; §11 out-of-scope → not built (correct). No spec requirement left without a task.

**Placeholder scan:** Task 13's JSX body is intentionally a skeleton because the Step-1 component tests pin the exact rendered text/roles (TDD drives the markup); all other code steps are complete. No "TBD/add validation/handle edge cases".

**Type consistency:** `Invitation.Create(..., string tokenHash)` (Task 2) matches its call in Task 6; `InvitationToken.Issue()/Hash()` (Task 1) used in Tasks 6,7; `UpdateKeycloakUserRequest(FirstName,LastName,EmailVerified,RequiredActions)` (Task 4) matches handler usage (Task 7) and test (Task 4); `AcceptInvitationResult`/`AcceptInvitationError` (Task 7) match the route switch (Task 8); contract names `InvitationAcceptContext`/`AcceptInvitationRequest`/`AcceptInvitationResponse` (Task 5) match backend routes (Task 8) and frontend types (Task 11).

**Open implementation choices (safe defaults chosen):** display name → KC `firstName` whole-string (split-on-space alternative noted in spec §6); GET 410-reason refinement noted inline in Task 8.
