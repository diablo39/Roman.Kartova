# Catalog Relationships UI Surface — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Dependencies/Dependents relationship section to the Application and Service detail pages (add via server-search typeahead from either side, list, delete), and widen relationship edge create/delete authority from source-side to either-endpoint team membership.

**Architecture:** Backend = a small authorization widening in `CatalogEndpointDelegates` (a new `AuthorizeEitherTeamAsync` gate; `Create`/`Delete` resolve both endpoints' teams). Frontend = one shared `<RelationshipsSection>` (two `useCursorList` groups), a generalized fixed-endpoint `<AddRelationshipDialog>`, a kind-parameterized `<EntitySearchCombobox>` typeahead (mirroring `UserSearchCombobox`), and a client-side mirror of the directionality matrix. No new routes, no new permissions.

**Tech Stack:** .NET 10 / ASP.NET Core minimal APIs + EF Core + Wolverine; MSTest v4 real-seam integration tests (`KartovaApiFixtureBase`, Testcontainers Postgres, real JWT). React 19 + TypeScript + TanStack Query + react-aria-components + Tailwind v4; Vitest + Testing Library.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-06-25-catalog-relationships-ui-surface-design.md`. ADR: `docs/architecture/decisions/ADR-0108-relationship-edge-authority-either-endpoint.md`.
- Authority (ADR-0108): create AND delete require `OrgAdmin` OR membership of the owning team of **at least one** connected entity. Member of neither → 403. Symmetric.
- Creatable relationship types: `DependsOn`, `PartOf` only. Matrix: `DependsOn` any `{Application,Service} → {Application,Service}`; `PartOf` only `Service → Application`.
- Entity kinds today: `Application`, `Service`. `direction` query values are lowercase: `outgoing` | `incoming` | `all`.
- `RelationshipResponse = { id, source:{kind,id,displayName}, target:{kind,id,displayName}, type, origin, createdByUserId, createdAt }` — the far endpoint's `teamId` is **not** in the response.
- Permission constant `KartovaPermissions.CatalogRelationshipsWrite` (`"catalog.relationships.write"`) already exists in C# and `web/src/shared/auth/permissions.ts` (+ snapshot). **Do not add or modify it.**
- C# code edits go through Serena tools (project policy); `dotnet` runs via `cmd //c` / PowerShell on Windows. Build with `TreatWarningsAsErrors=true` (0 warnings).
- Frontend: every new list surface follows ADR-0095/0107 — but this is an **embedded** section, so `useListUrlState`/`<FilterBar>` are intentionally NOT wired (no URL state, no facets).
- DoD: all eight blocking gates + **mutation gate (6) is blocking** here (authorization logic changes). Run `scripts/ci-local.sh` green before push.

---

## Task 1: Backend — either-endpoint authority on CREATE (ADR-0108)

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs` (add `AuthorizeEitherTeamAsync`; rewire `CreateRelationshipAsync`)
- Test: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CreateRelationshipTests.cs`

**Interfaces:**
- Consumes (existing): `ICatalogEntityLookup.Find(EntityKind, Guid, CancellationToken) → EntityLookupResult? { Guid TeamId, string DisplayName }`; existing private `AuthorizeTargetTeamAsync(IAuthorizationService, ClaimsPrincipal, Guid) → Task<IResult?>` (returns `null` when authorized, a forbidden `IResult` otherwise); `Fx.SeedTeamInOrganizationAsync(TenantId, string) → Guid`; `Fx.SeedTeamMembershipAsync(Guid teamId, Guid userId, byte roleByte)`; `Fx.GetSubClaimAsync(string email) → Guid`; `Fx.CreateAuthenticatedClientAsync(string email, string[] roles)`.
- Produces: `AuthorizeEitherTeamAsync(IAuthorizationService, ClaimsPrincipal, Guid teamA, Guid teamB) → Task<IResult?>` (reused by Task 2).

- [ ] **Step 1: Write the failing test — target-team member can create**

Add to `CreateRelationshipTests.cs` (uses the file's existing `SeedServiceAsync`, `PostRelAsync`, `OrgAUser`):

```csharp
[TestMethod]
public async Task POST_by_target_team_member_returns_201()
{
    // ADR-0108: a member of the TARGET entity's team (but NOT the source team)
    // may declare the edge. Was 403 under source-only authority.
    var admin = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
    var tenant = Fx.TenantIdForEmail(OrgAUser);
    var sourceTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "Rel Either Src 201");
    var targetTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "Rel Either Tgt 201");
    var src = await SeedServiceAsync(admin, sourceTeam, "svc-either-src-201");
    var tgt = await SeedServiceAsync(admin, targetTeam, "svc-either-tgt-201");

    var member = await Fx.CreateAuthenticatedClientAsync("member@orga.kartova.local", new[] { KartovaRoles.Member });
    var memberId = await Fx.GetSubClaimAsync("member@orga.kartova.local");
    await Fx.SeedTeamMembershipAsync(targetTeam, memberId, roleByte: 1 /* Member */);

    var resp = await PostRelAsync(member, EntityKind.Service, src, RelationshipType.DependsOn, EntityKind.Service, tgt);

    Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter FullyQualifiedName~POST_by_target_team_member_returns_201"`
Expected: FAIL — actual `403 Forbidden` (source-only gate rejects the non-source member).

- [ ] **Step 3: Add the `AuthorizeEitherTeamAsync` helper**

In `CatalogEndpointDelegates.cs`, beside the existing `AuthorizeTargetTeamAsync` private helper, add:

```csharp
private static async Task<IResult?> AuthorizeEitherTeamAsync(
    IAuthorizationService auth,
    ClaimsPrincipal caller,
    Guid teamA,
    Guid teamB)
{
    // ADR-0108: allowed if the caller passes the per-team gate for either
    // endpoint's owning team. OrgAdmin passes any team check (short-circuits on
    // teamA). A deleted endpoint contributes Guid.Empty, which only OrgAdmin passes.
    if (await AuthorizeTargetTeamAsync(auth, caller, teamA) is null)
        return null;
    return await AuthorizeTargetTeamAsync(auth, caller, teamB);
}
```

- [ ] **Step 4: Rewire `CreateRelationshipAsync` to gate on either endpoint**

Resolve the target **before** the gate, then gate on both teams. Replace this block:

```csharp
        if (sourceInfo is null)
            return Results.Problem(
                type: ProblemTypes.InvalidSourceEntity,
                title: "Invalid source entity",
                detail: "The source entity does not exist in this tenant.",
                statusCode: StatusCodes.Status422UnprocessableEntity);

        // Source-team membership gate: OrgAdmin may declare edges for any team;
        // a Member must belong to the source entity's owning team.
        if (await AuthorizeTargetTeamAsync(auth, caller, sourceInfo.TeamId) is { } forbidden)
            return forbidden;

        var targetInfo = await lookup.Find(target.Kind, target.Id, ct);
        if (targetInfo is null)
            return Results.Problem(
                type: ProblemTypes.InvalidTargetEntity,
                title: "Invalid target entity",
                detail: "The target entity does not exist in this tenant.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
```

with:

```csharp
        if (sourceInfo is null)
            return Results.Problem(
                type: ProblemTypes.InvalidSourceEntity,
                title: "Invalid source entity",
                detail: "The source entity does not exist in this tenant.",
                statusCode: StatusCodes.Status422UnprocessableEntity);

        var targetInfo = await lookup.Find(target.Kind, target.Id, ct);
        if (targetInfo is null)
            return Results.Problem(
                type: ProblemTypes.InvalidTargetEntity,
                title: "Invalid target entity",
                detail: "The target entity does not exist in this tenant.",
                statusCode: StatusCodes.Status422UnprocessableEntity);

        // Either-endpoint authority (ADR-0108): OrgAdmin, or member of the source
        // OR target entity's owning team. Member of neither -> 403.
        if (await AuthorizeEitherTeamAsync(auth, caller, sourceInfo.TeamId, targetInfo.TeamId) is { } forbidden)
            return forbidden;
```

(Use Serena `replace_content` on the file; the rest of `CreateRelationshipAsync` — duplicate pre-check, handler call, audit — is unchanged.)

- [ ] **Step 5: Run the new test to verify it passes**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter FullyQualifiedName~POST_by_target_team_member_returns_201"`
Expected: PASS (201).

- [ ] **Step 6: Update the "neither team" regression test**

The existing `POST_by_member_not_in_source_team_returns_403` now means "member of neither team". Rename it and make the two endpoints live in **different** teams so the case is unambiguous. Replace that method with:

```csharp
[TestMethod]
public async Task POST_by_member_in_neither_team_returns_403()
{
    // ADR-0108: a Member who belongs to NEITHER endpoint's team is still rejected.
    var admin = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
    var tenant = Fx.TenantIdForEmail(OrgAUser);
    var sourceTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "Rel Neither Src 403");
    var targetTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "Rel Neither Tgt 403");
    var a = await SeedServiceAsync(admin, sourceTeam, "svc-neither-1-403");
    var b = await SeedServiceAsync(admin, targetTeam, "svc-neither-2-403");

    var member = await Fx.CreateAuthenticatedClientAsync("member@orga.kartova.local", new[] { KartovaRoles.Member });
    var resp = await PostRelAsync(member, EntityKind.Service, a, RelationshipType.DependsOn, EntityKind.Service, b);

    Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
}
```

- [ ] **Step 7: Run the full create-relationship class**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter FullyQualifiedName~CreateRelationshipTests"`
Expected: PASS — all cases green (existing 201/400/409/422/401 cases + the new 201 + the renamed neither-team 403).

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs \
        src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CreateRelationshipTests.cs
git commit -m "feat(catalog): either-endpoint authority for relationship create (ADR-0108)"
```

---

## Task 2: Backend — either-endpoint authority on DELETE (ADR-0108)

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs` (`DeleteRelationshipAsync`)
- Test: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/DeleteRelationshipTests.cs`

**Interfaces:**
- Consumes: `AuthorizeEitherTeamAsync` (Task 1); existing `Rel(...)`, `SeedServiceAsync`, `OrgAUser` in the test file.
- Produces: none downstream.

- [ ] **Step 1: Write the failing test — target-team member can delete**

Add to `DeleteRelationshipTests.cs`:

```csharp
[TestMethod]
public async Task DELETE_by_target_team_member_returns_204()
{
    var admin = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
    var tenant = Fx.TenantIdForEmail(OrgAUser);
    var sourceTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "Del Either Src 204");
    var targetTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "Del Either Tgt 204");
    var src = await SeedServiceAsync(admin, sourceTeam, "svc-del-either-src");
    var tgt = await SeedServiceAsync(admin, targetTeam, "svc-del-either-tgt");
    var created = await (await admin.PostAsJsonAsync("/api/v1/catalog/relationships",
        Rel(EntityKind.Service, src, RelationshipType.DependsOn, EntityKind.Service, tgt)))
        .Content.ReadFromJsonAsync<RelationshipResponse>(KartovaApiFixtureBase.WireJson);

    var member = await Fx.CreateAuthenticatedClientAsync("member@orga.kartova.local", new[] { KartovaRoles.Member });
    var memberId = await Fx.GetSubClaimAsync("member@orga.kartova.local");
    await Fx.SeedTeamMembershipAsync(targetTeam, memberId, roleByte: 1 /* Member */);

    var del = await member.DeleteAsync($"/api/v1/catalog/relationships/{created!.Id}");
    Assert.AreEqual(HttpStatusCode.NoContent, del.StatusCode);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter FullyQualifiedName~DELETE_by_target_team_member_returns_204"`
Expected: FAIL — actual `403 Forbidden` (source-only delete gate).

- [ ] **Step 3: Rewire `DeleteRelationshipAsync` to gate on either endpoint**

Replace this block:

```csharp
        var sourceInfo = await lookup.Find(rel.Source.Kind, rel.Source.Id, ct);

        // source entity still exists: OrgAdmin OR member of source team.
        // source entity deleted: fall back to OrgAdmin-only (null TeamId -> gate never passes for Member).
        var teamId = sourceInfo?.TeamId ?? Guid.Empty;
        if (await AuthorizeTargetTeamAsync(auth, caller, teamId) is { } forbidden)
            return forbidden;
```

with:

```csharp
        var sourceInfo = await lookup.Find(rel.Source.Kind, rel.Source.Id, ct);
        var targetInfo = await lookup.Find(rel.Target.Kind, rel.Target.Id, ct);

        // Either-endpoint authority (ADR-0108). A hard-deleted endpoint resolves to
        // Guid.Empty (only OrgAdmin passes); both deleted -> OrgAdmin-only.
        if (await AuthorizeEitherTeamAsync(
                auth, caller,
                sourceInfo?.TeamId ?? Guid.Empty,
                targetInfo?.TeamId ?? Guid.Empty) is { } forbidden)
            return forbidden;
```

- [ ] **Step 4: Run the new test to verify it passes**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter FullyQualifiedName~DELETE_by_target_team_member_returns_204"`
Expected: PASS (204).

- [ ] **Step 5: Update the "neither team" delete regression test**

Replace the existing `DELETE_by_member_not_in_source_team_returns_403` with a two-team "neither" case:

```csharp
[TestMethod]
public async Task DELETE_by_member_in_neither_team_returns_403()
{
    var admin = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
    var tenant = Fx.TenantIdForEmail(OrgAUser);
    var sourceTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "Del Neither Src 403");
    var targetTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "Del Neither Tgt 403");
    var a = await SeedServiceAsync(admin, sourceTeam, "svc-dn-1-403");
    var b = await SeedServiceAsync(admin, targetTeam, "svc-dn-2-403");
    var created = await (await admin.PostAsJsonAsync("/api/v1/catalog/relationships",
        Rel(EntityKind.Service, a, RelationshipType.DependsOn, EntityKind.Service, b)))
        .Content.ReadFromJsonAsync<RelationshipResponse>(KartovaApiFixtureBase.WireJson);

    var member = await Fx.CreateAuthenticatedClientAsync("member@orga.kartova.local", new[] { KartovaRoles.Member });
    var resp = await member.DeleteAsync($"/api/v1/catalog/relationships/{created!.Id}");
    Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
}
```

- [ ] **Step 6: Run the full delete-relationship class + the whole Catalog integration suite**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter FullyQualifiedName~DeleteRelationshipTests"`
Then the whole assembly (catches `CatalogPermissionMatrixTests` regressions): `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests"`
Expected: PASS.

- [ ] **Step 7: Mutation gate on the changed delegate (DoD gate 6 — blocking)**

Run `/misc:mutation-sentinel` over `CatalogEndpointDelegates.cs` (the `AuthorizeEitherTeamAsync` / create / delete gate logic), then `/misc:test-generator` for survivors. Document any survivors. Target ≥80%.

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs \
        src/Modules/Catalog/Kartova.Catalog.IntegrationTests/DeleteRelationshipTests.cs
git commit -m "feat(catalog): either-endpoint authority for relationship delete (ADR-0108)"
```

---

## Task 3: Codegen — regenerate the typed client

**Files:**
- Modify: `web/src/generated/openapi.ts`, `web/openapi-snapshot.json` (both generated)

**Interfaces:**
- Produces: `components["schemas"]["RelationshipResponse"]`, `components["schemas"]["CreateRelationshipRequest"]`, `operations["ListRelationships"]`, and the three `/api/v1/catalog/relationships` paths in the typed client — consumed by Task 5.

- [ ] **Step 1: Start the API (dev stack) so codegen can read the live OpenAPI document**

Run the project's API per `web/package.json` `predev`/`codegen` scripts (e.g. `cmd //c "npm --prefix web run codegen"` with the API running). If Docker/dev-stack is unavailable in-session, STOP and flag this task **pending user verification** — the remaining frontend tasks compile against these types.

- [ ] **Step 2: Verify the relationship types/paths are present**

Run: `grep -c "RelationshipResponse" web/src/generated/openapi.ts && grep -o '"/api/v1/catalog/relationships[^"]*"' web/openapi-snapshot.json | sort -u`
Expected: `RelationshipResponse` count ≥ 1; the three relationship paths listed.

- [ ] **Step 3: Commit the regenerated client**

```bash
git add web/src/generated/openapi.ts web/openapi-snapshot.json
git commit -m "chore(web): regenerate client for catalog relationship endpoints"
```

---

## Task 4: Frontend — directionality matrix mirror

**Files:**
- Create: `web/src/features/catalog/relationships/relationshipTypeRules.ts`
- Test: `web/src/features/catalog/relationships/__tests__/relationshipTypeRules.test.ts`

**Interfaces:**
- Produces: `RelationshipKind = "Application" | "Service"`; `CreatableRelationshipType = "DependsOn" | "PartOf"`; `FixedRole = "source" | "target"`; `relationshipTypeLabel`; `isAllowedPair(type, source, target)`; `offerableTypes(fixedRole, fixedKind)`; `allowedOtherKinds(type, fixedRole, fixedKind)`.

- [ ] **Step 1: Write the failing tests**

```ts
// relationshipTypeRules.test.ts
import { describe, it, expect } from "vitest";
import {
  isAllowedPair, offerableTypes, allowedOtherKinds, relationshipTypeLabel,
} from "@/features/catalog/relationships/relationshipTypeRules";

describe("relationshipTypeRules", () => {
  it("DependsOn allows every kind pair", () => {
    for (const s of ["Application", "Service"] as const)
      for (const t of ["Application", "Service"] as const)
        expect(isAllowedPair("DependsOn", s, t)).toBe(true);
  });

  it("PartOf allows only Service -> Application", () => {
    expect(isAllowedPair("PartOf", "Service", "Application")).toBe(true);
    expect(isAllowedPair("PartOf", "Service", "Service")).toBe(false);
    expect(isAllowedPair("PartOf", "Application", "Application")).toBe(false);
    expect(isAllowedPair("PartOf", "Application", "Service")).toBe(false);
  });

  it("offerableTypes depends on the fixed role and kind", () => {
    expect(offerableTypes("source", "Application")).toEqual(["DependsOn"]);
    expect(offerableTypes("source", "Service")).toEqual(["DependsOn", "PartOf"]);
    expect(offerableTypes("target", "Application")).toEqual(["DependsOn", "PartOf"]);
    expect(offerableTypes("target", "Service")).toEqual(["DependsOn"]);
  });

  it("allowedOtherKinds constrains the other endpoint", () => {
    expect(allowedOtherKinds("DependsOn", "source", "Application")).toEqual(["Application", "Service"]);
    expect(allowedOtherKinds("PartOf", "source", "Service")).toEqual(["Application"]);
    expect(allowedOtherKinds("PartOf", "target", "Application")).toEqual(["Service"]);
  });

  it("labels both creatable types", () => {
    expect(relationshipTypeLabel.DependsOn).toBe("Depends on");
    expect(relationshipTypeLabel.PartOf).toBe("Part of");
  });
});
```

- [ ] **Step 2: Run to verify failure**

Run: `cmd //c "npm --prefix web run test -- relationshipTypeRules"`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement the module**

```ts
// relationshipTypeRules.ts
export type RelationshipKind = "Application" | "Service";
export type CreatableRelationshipType = "DependsOn" | "PartOf";
export type FixedRole = "source" | "target";

export const relationshipTypeLabel: Record<CreatableRelationshipType, string> = {
  DependsOn: "Depends on",
  PartOf: "Part of",
};

const CREATABLE_TYPES: CreatableRelationshipType[] = ["DependsOn", "PartOf"];
const KINDS: RelationshipKind[] = ["Application", "Service"];

// Mirror of backend RelationshipTypeRules.IsAllowedPair (ADR-0068, creatable subset).
export function isAllowedPair(
  type: CreatableRelationshipType,
  source: RelationshipKind,
  target: RelationshipKind,
): boolean {
  switch (type) {
    case "DependsOn":
      return true;
    case "PartOf":
      return source === "Service" && target === "Application";
  }
}

// Valid kinds for the OTHER endpoint given the chosen type and which side is fixed.
export function allowedOtherKinds(
  type: CreatableRelationshipType,
  fixedRole: FixedRole,
  fixedKind: RelationshipKind,
): RelationshipKind[] {
  return KINDS.filter((other) =>
    fixedRole === "source"
      ? isAllowedPair(type, fixedKind, other)
      : isAllowedPair(type, other, fixedKind),
  );
}

// Types creatable with `fixedKind` in the `fixedRole` slot (i.e. some other-kind is valid).
export function offerableTypes(
  fixedRole: FixedRole,
  fixedKind: RelationshipKind,
): CreatableRelationshipType[] {
  return CREATABLE_TYPES.filter((t) => allowedOtherKinds(t, fixedRole, fixedKind).length > 0);
}
```

- [ ] **Step 4: Run to verify pass**

Run: `cmd //c "npm --prefix web run test -- relationshipTypeRules"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/relationships/
git commit -m "feat(web): relationship directionality matrix mirror"
```

---

## Task 5: Frontend — relationships API layer

**Files:**
- Create: `web/src/features/catalog/api/relationships.ts`
- Test: `web/src/features/catalog/api/__tests__/relationships.test.tsx`

**Interfaces:**
- Consumes: `apiClient` (`@/features/catalog/api/client`), `unwrapData` (`@/shared/api/openapi-fetch-helpers`), `useCursorList` (`@/lib/list/useCursorList`), generated `components`/`operations`, `RelationshipKind` (Task 4).
- Produces: `RelationshipResponse`; `RelationshipDirection`; `RelationshipsListParams`; `relationshipKeys`; `useRelationshipsList(params)`; `CreateRelationshipPayload`; `useCreateRelationship()`; `useDeleteRelationship()`; `EntityOption = {kind,id,displayName}`; `useEntitySearch(kind, query, {enabled})`.

- [ ] **Step 1: Write the failing tests**

```tsx
// relationships.test.tsx
import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import * as clientModule from "@/features/catalog/api/client";
import {
  useRelationshipsList, useCreateRelationship, useDeleteRelationship, useEntitySearch,
} from "@/features/catalog/api/relationships";

function wrapper(qc: QueryClient) {
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={qc}>{children}</QueryClientProvider>
  );
}
const newQc = () => new QueryClient({ defaultOptions: { queries: { retry: false } } });

describe("relationships api", () => {
  beforeEach(() => vi.restoreAllMocks());

  it("useRelationshipsList fetches a directional page", async () => {
    const page = { items: [{ id: "r1", type: "DependsOn" }], nextCursor: null, prevCursor: null };
    const GET = vi.fn().mockResolvedValue({ data: page, error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET } as never);

    const qc = newQc();
    const { result } = renderHook(
      () => useRelationshipsList({ entityKind: "Service", entityId: "s1", direction: "outgoing" }),
      { wrapper: wrapper(qc) },
    );
    await waitFor(() => expect(result.current.items).toHaveLength(1));
    expect(GET).toHaveBeenCalledWith("/api/v1/catalog/relationships", expect.objectContaining({
      params: { query: expect.objectContaining({ entityKind: "Service", entityId: "s1", direction: "outgoing" }) },
    }));
  });

  it("useCreateRelationship POSTs and invalidates", async () => {
    const POST = vi.fn().mockResolvedValue({ data: { id: "r1" }, error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ POST } as never);
    const qc = newQc();
    const spy = vi.spyOn(qc, "invalidateQueries");
    const { result } = renderHook(() => useCreateRelationship(), { wrapper: wrapper(qc) });
    await result.current.mutateAsync({ sourceKind: "Service", sourceId: "s1", type: "DependsOn", targetKind: "Service", targetId: "s2" });
    expect(POST).toHaveBeenCalledWith("/api/v1/catalog/relationships", { body: expect.objectContaining({ type: "DependsOn" }) });
    expect(spy).toHaveBeenCalledWith({ queryKey: ["relationships"] });
  });

  it("useDeleteRelationship DELETEs by id and invalidates", async () => {
    const DELETE = vi.fn().mockResolvedValue({ data: undefined, error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ DELETE } as never);
    const qc = newQc();
    const spy = vi.spyOn(qc, "invalidateQueries");
    const { result } = renderHook(() => useDeleteRelationship(), { wrapper: wrapper(qc) });
    await result.current.mutateAsync("r1");
    expect(DELETE).toHaveBeenCalledWith("/api/v1/catalog/relationships/{id}", { params: { path: { id: "r1" } } });
    expect(spy).toHaveBeenCalledWith({ queryKey: ["relationships"] });
  });

  it("useEntitySearch hits the services endpoint for Service kind", async () => {
    const page = { items: [{ id: "s9", displayName: "AuthService" }], nextCursor: null, prevCursor: null };
    const GET = vi.fn().mockResolvedValue({ data: page, error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET } as never);
    const qc = newQc();
    const { result } = renderHook(() => useEntitySearch("Service", "au", { enabled: true }), { wrapper: wrapper(qc) });
    await waitFor(() => expect(result.current.data).toEqual([{ kind: "Service", id: "s9", displayName: "AuthService" }]));
    expect(GET).toHaveBeenCalledWith("/api/v1/catalog/services", expect.objectContaining({
      params: { query: expect.objectContaining({ displayNameContains: "au", limit: 10 }) },
    }));
  });
});
```

- [ ] **Step 2: Run to verify failure**

Run: `cmd //c "npm --prefix web run test -- api/__tests__/relationships"`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement `api/relationships.ts`**

```ts
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "./client";
import { useCursorList } from "@/lib/list/useCursorList";
import { unwrapData } from "@/shared/api/openapi-fetch-helpers";
import type { components, operations } from "@/generated/openapi";
import type { RelationshipKind } from "@/features/catalog/relationships/relationshipTypeRules";

export type RelationshipResponse = components["schemas"]["RelationshipResponse"];
export type CreateRelationshipPayload = components["schemas"]["CreateRelationshipRequest"];
type ListQuery = NonNullable<operations["ListRelationships"]["parameters"]["query"]>;
export type RelationshipDirection = NonNullable<ListQuery["direction"]>;

export type RelationshipsListParams = {
  entityKind: NonNullable<ListQuery["entityKind"]>;
  entityId: string;
  direction: RelationshipDirection;
  limit?: number;
};

export const relationshipKeys = {
  all: ["relationships"] as const,
  list: (p?: RelationshipsListParams) =>
    p
      ? ([...relationshipKeys.all, "list", p] as const)
      : ([...relationshipKeys.all, "list"] as const),
};

export function useRelationshipsList(params: RelationshipsListParams) {
  return useCursorList<RelationshipResponse>({
    queryKey: relationshipKeys.list(params),
    fetchPage: async (cursor) => {
      const { data, error } = await apiClient.GET("/api/v1/catalog/relationships", {
        params: {
          query: {
            entityKind: params.entityKind,
            entityId: params.entityId,
            direction: params.direction,
            limit: params.limit ?? 20,
            cursor,
          },
        },
      });
      if (error) throw error;
      return unwrapData(data);
    },
  });
}

export function useCreateRelationship() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (payload: CreateRelationshipPayload) => {
      const { data, error } = await apiClient.POST("/api/v1/catalog/relationships", { body: payload });
      if (error) throw error;
      return unwrapData(data);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: relationshipKeys.all }),
  });
}

export function useDeleteRelationship() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => {
      const { error } = await apiClient.DELETE("/api/v1/catalog/relationships/{id}", {
        params: { path: { id } },
      });
      if (error) throw error;
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: relationshipKeys.all }),
  });
}

export type EntityOption = { kind: RelationshipKind; id: string; displayName: string };

export function useEntitySearch(
  kind: RelationshipKind,
  query: string,
  opts: { enabled: boolean },
) {
  return useQuery({
    queryKey: ["catalog", "entity-search", kind, query],
    enabled: opts.enabled,
    queryFn: async (): Promise<EntityOption[]> => {
      const q = { displayNameContains: query, sortBy: "displayName", sortOrder: "asc", limit: 10 } as const;
      if (kind === "Application") {
        const { data, error } = await apiClient.GET("/api/v1/catalog/applications", { params: { query: q } });
        if (error) throw error;
        return unwrapData(data).items.map((e) => ({ kind, id: e.id, displayName: e.displayName }));
      }
      const { data, error } = await apiClient.GET("/api/v1/catalog/services", { params: { query: q } });
      if (error) throw error;
      return unwrapData(data).items.map((e) => ({ kind, id: e.id, displayName: e.displayName }));
    },
  });
}
```

- [ ] **Step 4: Run to verify pass**

Run: `cmd //c "npm --prefix web run test -- api/__tests__/relationships"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/api/relationships.ts web/src/features/catalog/api/__tests__/relationships.test.tsx
git commit -m "feat(web): relationships api hooks + entity search"
```

---

## Task 6: Frontend — EntitySearchCombobox (typeahead)

**Files:**
- Create: `web/src/features/catalog/components/EntitySearchCombobox.tsx`
- Test: `web/src/features/catalog/components/__tests__/EntitySearchCombobox.test.tsx`
- Reference (mirror): `web/src/features/users/components/UserSearchCombobox.tsx`

**Interfaces:**
- Consumes: `useEntitySearch`, `EntityOption` (Task 5); `RelationshipKind` (Task 4).
- Produces: `EntitySearchCombobox({ kind, excludeId?, onSelect, placeholder? })` where `onSelect: (e: EntityOption) => void`.

- [ ] **Step 1: Write the failing tests**

```tsx
// EntitySearchCombobox.test.tsx
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, fireEvent, waitFor, act } from "@testing-library/react";
import { EntitySearchCombobox } from "@/features/catalog/components/EntitySearchCombobox";
import * as api from "@/features/catalog/api/relationships";

beforeEach(() => vi.useFakeTimers());
afterEach(() => { vi.runOnlyPendingTimers(); vi.useRealTimers(); vi.restoreAllMocks(); });

function mockSearch(items: api.EntityOption[]) {
  vi.spyOn(api, "useEntitySearch").mockReturnValue({
    data: items, isLoading: false, isError: false,
  } as never);
}

it("does not query until 2 characters are typed", () => {
  const spy = vi.spyOn(api, "useEntitySearch").mockReturnValue({ data: undefined, isLoading: false, isError: false } as never);
  render(<EntitySearchCombobox kind="Service" onSelect={vi.fn()} />);
  fireEvent.change(screen.getByRole("combobox"), { target: { value: "a" } });
  act(() => vi.advanceTimersByTime(300));
  // last call's enabled flag is false for a single char
  const lastCall = spy.mock.calls.at(-1);
  expect(lastCall?.[2]).toEqual({ enabled: false });
});

it("selecting an option fires onSelect", async () => {
  mockSearch([{ kind: "Service", id: "s9", displayName: "AuthService" }]);
  const onSelect = vi.fn();
  render(<EntitySearchCombobox kind="Service" onSelect={onSelect} />);
  fireEvent.focus(screen.getByRole("combobox"));
  fireEvent.change(screen.getByRole("combobox"), { target: { value: "auth" } });
  act(() => vi.advanceTimersByTime(300));
  fireEvent.click(await screen.findByText("AuthService"));
  expect(onSelect).toHaveBeenCalledWith({ kind: "Service", id: "s9", displayName: "AuthService" });
});

it("excludes the excludeId from results", async () => {
  mockSearch([
    { kind: "Service", id: "self", displayName: "Me" },
    { kind: "Service", id: "s9", displayName: "AuthService" },
  ]);
  render(<EntitySearchCombobox kind="Service" excludeId="self" onSelect={vi.fn()} />);
  fireEvent.focus(screen.getByRole("combobox"));
  fireEvent.change(screen.getByRole("combobox"), { target: { value: "e" } });
  act(() => vi.advanceTimersByTime(300));
  expect(await screen.findByText("AuthService")).toBeInTheDocument();
  expect(screen.queryByText("Me")).not.toBeInTheDocument();
});
```

- [ ] **Step 2: Run to verify failure**

Run: `cmd //c "npm --prefix web run test -- EntitySearchCombobox"`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement the component (mirror `UserSearchCombobox`)**

```tsx
import { useEffect, useId, useRef, useState } from "react";
import { useEntitySearch, type EntityOption } from "@/features/catalog/api/relationships";
import type { RelationshipKind } from "@/features/catalog/relationships/relationshipTypeRules";

const MIN_QUERY_LENGTH = 2;
const DEBOUNCE_MS = 250;

interface Props {
  kind: RelationshipKind;
  excludeId?: string;
  onSelect: (entity: EntityOption) => void;
  placeholder?: string;
}

const optionId = (prefix: string, i: number) => `${prefix}-option-${i}`;

export function EntitySearchCombobox({ kind, excludeId, onSelect, placeholder }: Props) {
  const listboxId = useId();
  const containerRef = useRef<HTMLDivElement>(null);
  const [q, setQ] = useState("");
  const [debouncedQ, setDebouncedQ] = useState("");
  const [open, setOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState<number | null>(null);

  useEffect(() => {
    const id = window.setTimeout(() => setDebouncedQ(q), DEBOUNCE_MS);
    return () => window.clearTimeout(id);
  }, [q]);

  const enabled = debouncedQ.length >= MIN_QUERY_LENGTH;
  const search = useEntitySearch(kind, debouncedQ, { enabled });
  const results = (search.data ?? []).filter((e) => e.id !== excludeId);
  const showDropdown = open && enabled;

  useEffect(() => {
    if (!open) return;
    const onDocMouseDown = (e: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener("mousedown", onDocMouseDown);
    return () => document.removeEventListener("mousedown", onDocMouseDown);
  }, [open]);

  // Reset active highlight when the query or visibility changes (render-time guard).
  const [prevQ, setPrevQ] = useState(debouncedQ);
  const [prevShow, setPrevShow] = useState(showDropdown);
  if (debouncedQ !== prevQ || showDropdown !== prevShow) {
    setPrevQ(debouncedQ);
    setPrevShow(showDropdown);
    setActiveIndex(null);
  }

  const select = (e: EntityOption) => {
    onSelect(e);
    setQ("");
    setDebouncedQ("");
    setOpen(false);
    setActiveIndex(null);
  };

  const onKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "Escape") { e.preventDefault(); setQ(""); setDebouncedQ(""); setOpen(false); setActiveIndex(null); return; }
    if (!showDropdown || results.length === 0) return;
    if (e.key === "ArrowDown") { e.preventDefault(); setActiveIndex((p) => (p === null ? 0 : Math.min(p + 1, results.length - 1))); }
    else if (e.key === "ArrowUp") { e.preventDefault(); setActiveIndex((p) => (p === null ? 0 : Math.max(p - 1, 0))); }
    else if (e.key === "Enter" && activeIndex !== null && results[activeIndex]) { e.preventDefault(); select(results[activeIndex]); }
  };

  const activeDescendant = showDropdown && activeIndex !== null && results[activeIndex] ? optionId(listboxId, activeIndex) : undefined;

  return (
    <div ref={containerRef} className="relative w-full">
      <input
        type="text"
        role="combobox"
        aria-autocomplete="list"
        aria-controls={listboxId}
        aria-expanded={showDropdown}
        aria-activedescendant={activeDescendant}
        value={q}
        placeholder={placeholder ?? `Search ${kind.toLowerCase()}s…`}
        onChange={(e) => { setQ(e.target.value); setOpen(true); }}
        onFocus={() => setOpen(true)}
        onKeyDown={onKeyDown}
        className="w-full rounded-lg border border-secondary bg-primary px-3 py-2 text-sm text-primary shadow-xs outline-none placeholder:text-tertiary focus:border-brand-500 focus:ring-1 focus:ring-brand-500"
      />
      {showDropdown && (
        <div id={listboxId} role="listbox" className="absolute z-10 mt-1 w-full overflow-hidden rounded-lg border border-secondary bg-primary shadow-lg">
          {search.isLoading && <div className="px-3 py-2 text-sm text-tertiary">Searching…</div>}
          {!search.isLoading && search.isError && <div className="px-3 py-2 text-sm text-error-primary">Search failed. Try again.</div>}
          {!search.isLoading && !search.isError && results.length === 0 && <div className="px-3 py-2 text-sm text-tertiary">No matches.</div>}
          {!search.isLoading && !search.isError && results.map((e, i) => (
            <button
              key={e.id}
              id={optionId(listboxId, i)}
              type="button"
              role="option"
              aria-selected={i === activeIndex}
              onClick={() => select(e)}
              onMouseEnter={() => setActiveIndex(i)}
              className={`block w-full px-3 py-2 text-left text-sm ${i === activeIndex ? "bg-secondary" : ""}`}
            >
              {e.displayName}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
```

- [ ] **Step 4: Run to verify pass**

Run: `cmd //c "npm --prefix web run test -- EntitySearchCombobox"`
Expected: PASS. (If the active-descendant styling class differs from `UserSearchCombobox`, align it — cosmetic only.)

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/components/EntitySearchCombobox.tsx web/src/features/catalog/components/__tests__/EntitySearchCombobox.test.tsx
git commit -m "feat(web): entity search typeahead for relationships"
```

---

## Task 7: Frontend — AddRelationshipDialog (fixed-endpoint)

**Files:**
- Create: `web/src/features/catalog/components/AddRelationshipDialog.tsx`
- Test: `web/src/features/catalog/components/__tests__/AddRelationshipDialog.test.tsx`
- Reference (mirror modal + native-select pattern): `web/src/features/catalog/components/RegisterApplicationDialog.tsx`

**Interfaces:**
- Consumes: `useCreateRelationship`, `EntityOption` (Task 5); `offerableTypes`, `allowedOtherKinds`, `relationshipTypeLabel`, `RelationshipKind`, `CreatableRelationshipType`, `FixedRole` (Task 4); `EntitySearchCombobox` (Task 6); `ModalOverlay`/`Modal`/`Dialog` (`@/components/application/modals/modal`); `toast` (`sonner`); `ProblemDetails` (`@/shared/forms/problemDetails`).
- Produces: `AddRelationshipDialog({ open, onOpenChange, fixedRole, fixedEntity })` where `fixedEntity: { kind: RelationshipKind; id: string; displayName: string }`.

- [ ] **Step 1: Write the failing tests**

```tsx
// AddRelationshipDialog.test.tsx
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { AddRelationshipDialog } from "@/features/catalog/components/AddRelationshipDialog";
import * as api from "@/features/catalog/api/relationships";

vi.mock("@/features/catalog/components/EntitySearchCombobox", () => ({
  // Stub the typeahead: a button that selects a fixed option.
  EntitySearchCombobox: ({ onSelect }: { onSelect: (e: unknown) => void }) => (
    <button type="button" onClick={() => onSelect({ kind: "Application", id: "app9", displayName: "Checkout" })}>
      pick-entity
    </button>
  ),
}));

function harness(ui: React.ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}
const svc = { kind: "Service" as const, id: "s1", displayName: "AuthService" };

beforeEach(() => vi.restoreAllMocks());

it("on a Service Dependents add (fixedRole=target) offers DependsOn only", () => {
  vi.spyOn(api, "useCreateRelationship").mockReturnValue({ mutateAsync: vi.fn(), isPending: false } as never);
  harness(<AddRelationshipDialog open onOpenChange={vi.fn()} fixedRole="target" fixedEntity={svc} />);
  const typeSelect = screen.getByTestId("relationship-type-select") as HTMLSelectElement;
  const options = Array.from(typeSelect.options).map((o) => o.value);
  expect(options).toEqual(["DependsOn"]); // PartOf cannot target a Service
});

it("PartOf forces the other kind to Application (source side, Service fixed)", () => {
  vi.spyOn(api, "useCreateRelationship").mockReturnValue({ mutateAsync: vi.fn(), isPending: false } as never);
  harness(<AddRelationshipDialog open onOpenChange={vi.fn()} fixedRole="source" fixedEntity={svc} />);
  fireEvent.change(screen.getByTestId("relationship-type-select"), { target: { value: "PartOf" } });
  const kindSelect = screen.getByTestId("relationship-otherkind-select") as HTMLSelectElement;
  expect(Array.from(kindSelect.options).map((o) => o.value)).toEqual(["Application"]);
  expect(kindSelect.disabled).toBe(true);
});

it("submits a payload with the fixed entity as source", async () => {
  const mutateAsync = vi.fn().mockResolvedValue({ id: "r1" });
  vi.spyOn(api, "useCreateRelationship").mockReturnValue({ mutateAsync, isPending: false } as never);
  const onOpenChange = vi.fn();
  harness(<AddRelationshipDialog open onOpenChange={onOpenChange} fixedRole="source" fixedEntity={svc} />);
  // DependsOn is default; pick the (stubbed) Application target then submit.
  fireEvent.click(screen.getByText("pick-entity"));
  fireEvent.click(screen.getByRole("button", { name: /add relationship/i }));
  await waitFor(() => expect(mutateAsync).toHaveBeenCalledWith({
    sourceKind: "Service", sourceId: "s1", type: "DependsOn", targetKind: "Application", targetId: "app9",
  }));
  await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
});

it("toasts on a 409 duplicate", async () => {
  const { toast } = await import("sonner");
  const errSpy = vi.spyOn(toast, "error").mockImplementation(() => "" as never);
  const mutateAsync = vi.fn().mockRejectedValue({ status: 409, detail: "This relationship already exists." });
  vi.spyOn(api, "useCreateRelationship").mockReturnValue({ mutateAsync, isPending: false } as never);
  harness(<AddRelationshipDialog open onOpenChange={vi.fn()} fixedRole="source" fixedEntity={svc} />);
  fireEvent.click(screen.getByText("pick-entity"));
  fireEvent.click(screen.getByRole("button", { name: /add relationship/i }));
  await waitFor(() => expect(errSpy).toHaveBeenCalled());
});
```

- [ ] **Step 2: Run to verify failure**

Run: `cmd //c "npm --prefix web run test -- AddRelationshipDialog"`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement the dialog**

```tsx
import { useEffect, useMemo, useState } from "react";
import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { Button } from "@/components/base/buttons/button";
import { toast } from "sonner";
import type { ProblemDetails } from "@/shared/forms/problemDetails";
import { useCreateRelationship, type CreateRelationshipPayload, type EntityOption } from "@/features/catalog/api/relationships";
import { EntitySearchCombobox } from "@/features/catalog/components/EntitySearchCombobox";
import {
  offerableTypes, allowedOtherKinds, relationshipTypeLabel,
  type RelationshipKind, type CreatableRelationshipType, type FixedRole,
} from "@/features/catalog/relationships/relationshipTypeRules";

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  fixedRole: FixedRole;
  fixedEntity: { kind: RelationshipKind; id: string; displayName: string };
}

export function AddRelationshipDialog({ open, onOpenChange, fixedRole, fixedEntity }: Props) {
  const mutation = useCreateRelationship();
  const types = useMemo(() => offerableTypes(fixedRole, fixedEntity.kind), [fixedRole, fixedEntity.kind]);
  const [type, setType] = useState<CreatableRelationshipType>(types[0]);
  const otherKinds = useMemo(() => allowedOtherKinds(type, fixedRole, fixedEntity.kind), [type, fixedRole, fixedEntity.kind]);
  const [otherKind, setOtherKind] = useState<RelationshipKind>(otherKinds[0]);
  const [other, setOther] = useState<EntityOption | null>(null);
  const [otherError, setOtherError] = useState("");

  // Keep type/otherKind valid as the matrix narrows; reset selection on close.
  useEffect(() => { if (!types.includes(type)) setType(types[0]); }, [types, type]);
  useEffect(() => { if (!otherKinds.includes(otherKind)) setOtherKind(otherKinds[0]); setOther(null); }, [otherKinds]); // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => {
    if (!open) { setType(types[0]); setOther(null); setOtherError(""); }
  }, [open]); // eslint-disable-line react-hooks/exhaustive-deps

  const submit = async () => {
    if (!other) { setOtherError(`Select a ${otherKind.toLowerCase()}`); return; }
    const payload: CreateRelationshipPayload = fixedRole === "source"
      ? { sourceKind: fixedEntity.kind, sourceId: fixedEntity.id, type, targetKind: other.kind, targetId: other.id }
      : { sourceKind: other.kind, sourceId: other.id, type, targetKind: fixedEntity.kind, targetId: fixedEntity.id };
    try {
      await mutation.mutateAsync(payload);
      toast.success("Relationship added");
      onOpenChange(false);
    } catch (err) {
      const p = err as ProblemDetails & { status?: number };
      toast.error(p.detail ?? p.title ?? "Failed to add relationship");
    }
  };

  const otherLabel = fixedRole === "source" ? "Target" : "Source";

  return (
    <ModalOverlay isOpen={open} onOpenChange={onOpenChange} isDismissable={!mutation.isPending}>
      <Modal className="max-w-[480px]">
        <Dialog aria-label="Add relationship" className="bg-primary rounded-xl shadow-xl p-6 outline-none space-y-4">
          <h2 className="text-lg font-semibold text-primary">
            {fixedRole === "source" ? "Add dependency" : "Add dependent"}
          </h2>
          <p className="text-sm text-tertiary">
            {fixedRole === "source" ? `${fixedEntity.displayName} …` : `… ${fixedEntity.displayName}`}
          </p>

          <label className="block text-sm">
            <span className="text-secondary">Type</span>
            <select
              data-testid="relationship-type-select"
              className="mt-1 w-full rounded-md border border-secondary bg-primary px-3 py-2 text-sm text-primary"
              value={type}
              onChange={(e) => setType(e.target.value as CreatableRelationshipType)}
            >
              {types.map((t) => <option key={t} value={t}>{relationshipTypeLabel[t]}</option>)}
            </select>
          </label>

          <label className="block text-sm">
            <span className="text-secondary">{otherLabel} kind</span>
            <select
              data-testid="relationship-otherkind-select"
              className="mt-1 w-full rounded-md border border-secondary bg-primary px-3 py-2 text-sm text-primary disabled:opacity-60"
              value={otherKind}
              disabled={otherKinds.length <= 1}
              onChange={(e) => { setOtherKind(e.target.value as RelationshipKind); setOther(null); }}
            >
              {otherKinds.map((k) => <option key={k} value={k}>{k}</option>)}
            </select>
          </label>

          <div className="text-sm">
            <span className="text-secondary">{otherLabel}</span>
            <div className="mt-1">
              <EntitySearchCombobox
                kind={otherKind}
                excludeId={otherKind === fixedEntity.kind ? fixedEntity.id : undefined}
                onSelect={(e) => { setOther(e); setOtherError(""); }}
              />
            </div>
            {other && <p className="mt-1 text-xs text-tertiary">Selected: {other.displayName}</p>}
            {otherError && <p className="mt-1 text-xs text-error-primary">{otherError}</p>}
          </div>

          <div className="flex justify-end gap-2 pt-2">
            <Button color="secondary" size="sm" onClick={() => onOpenChange(false)} isDisabled={mutation.isPending}>Cancel</Button>
            <Button color="primary" size="sm" onClick={submit} isDisabled={mutation.isPending}>Add relationship</Button>
          </div>
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}
```

> Verify against the codebase during implementation (mechanical, not design): the `Button` import path/props (`@/components/base/buttons/button`, `color`/`size`/`isDisabled`) and the `ModalOverlay`/`Modal`/`Dialog` prop names — copy them verbatim from `RegisterApplicationDialog.tsx`.

- [ ] **Step 4: Run to verify pass**

Run: `cmd //c "npm --prefix web run test -- AddRelationshipDialog"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/components/AddRelationshipDialog.tsx web/src/features/catalog/components/__tests__/AddRelationshipDialog.test.tsx
git commit -m "feat(web): add-relationship dialog (fixed-endpoint, matrix-constrained)"
```

---

## Task 8: Frontend — RelationshipsSection (two groups + delete)

**Files:**
- Create: `web/src/features/catalog/components/RelationshipsSection.tsx`
- Test: `web/src/features/catalog/components/__tests__/RelationshipsSection.test.tsx`

**Interfaces:**
- Consumes: `useRelationshipsList`, `useDeleteRelationship`, `RelationshipResponse` (Task 5); `relationshipTypeLabel`, `RelationshipKind` (Task 4); `AddRelationshipDialog` (Task 7); `usePermissions` (`@/shared/auth/usePermissions`); `KartovaPermissions` (`@/shared/auth/permissions`); `Badge` (`@/components/base/badges/badges`); `CreatedByLink` (`@/features/users/components/CreatedByLink`); `Table` (`@/components/application/table/table`); `TableSkeleton`, `TablePager` (`@/components/application/data-table/data-table`); `toast` (`sonner`); `Link` (`react-router-dom`); `Button`.
- Produces: `RelationshipsSection({ entityKind, entityId, entityTeamId, entityDisplayName })`.

- [ ] **Step 1: Write the failing tests**

```tsx
// RelationshipsSection.test.tsx
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { RelationshipsSection } from "@/features/catalog/components/RelationshipsSection";
import * as api from "@/features/catalog/api/relationships";
import * as perms from "@/shared/auth/usePermissions";

function listResult(items: Partial<api.RelationshipResponse>[]) {
  return { items, isLoading: false, isError: false, hasNext: false, hasPrev: false, goNext: vi.fn(), goPrev: vi.fn() } as never;
}
const out = [{ id: "r1", type: "DependsOn", origin: "Manual", source: { kind: "Service", id: "s1", displayName: "Me" }, target: { kind: "Service", id: "s2", displayName: "AuthService" }, createdByUserId: "u1", createdAt: "2026-06-25T00:00:00Z" }];
const inc = [{ id: "r2", type: "DependsOn", origin: "Manual", source: { kind: "Application", id: "a1", displayName: "Checkout" }, target: { kind: "Service", id: "s1", displayName: "Me" }, createdByUserId: "u1", createdAt: "2026-06-25T00:00:00Z" }];

function mockLists() {
  vi.spyOn(api, "useRelationshipsList").mockImplementation((p: api.RelationshipsListParams) =>
    listResult(p.direction === "outgoing" ? out : inc));
}
function mockPerms(can: boolean) {
  vi.spyOn(perms, "usePermissions").mockReturnValue({
    hasPermission: () => can, role: can ? "OrgAdmin" : "Member", teamIds: [], teamAdminTeamIds: [], isLoading: false, isError: false,
  } as never);
}
function renderSection() {
  return render(
    <MemoryRouter>
      <RelationshipsSection entityKind="Service" entityId="s1" entityTeamId="t1" entityDisplayName="Me" />
    </MemoryRouter>,
  );
}
beforeEach(() => vi.restoreAllMocks());

it("renders the dependency target link and the dependent source link", () => {
  mockLists(); mockPerms(true);
  renderSection();
  expect(screen.getByText("AuthService").closest("a")).toHaveAttribute("href", "/catalog/services/s2"); // outgoing → target
  expect(screen.getByText("Checkout").closest("a")).toHaveAttribute("href", "/catalog/applications/a1"); // incoming → source
});

it("hides Add and Delete when the user cannot manage", () => {
  mockLists(); mockPerms(false);
  renderSection();
  expect(screen.queryByRole("button", { name: /add dependency/i })).not.toBeInTheDocument();
  expect(screen.queryByRole("button", { name: /delete/i })).not.toBeInTheDocument();
});

it("deletes a row after confirm", async () => {
  mockLists(); mockPerms(true);
  const mutateAsync = vi.fn().mockResolvedValue(undefined);
  vi.spyOn(api, "useDeleteRelationship").mockReturnValue({ mutateAsync, isPending: false } as never);
  vi.spyOn(window, "confirm").mockReturnValue(true);
  renderSection();
  fireEvent.click(screen.getAllByRole("button", { name: /delete/i })[0]);
  await waitFor(() => expect(mutateAsync).toHaveBeenCalledWith("r1"));
});
```

- [ ] **Step 2: Run to verify failure**

Run: `cmd //c "npm --prefix web run test -- RelationshipsSection"`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement the section**

```tsx
import { useState } from "react";
import { Link } from "react-router-dom";
import { toast } from "sonner";
import { Badge } from "@/components/base/badges/badges";
import { Button } from "@/components/base/buttons/button";
import { Table } from "@/components/application/table/table";
import { TableSkeleton, TablePager } from "@/components/application/data-table/data-table";
import { CreatedByLink } from "@/features/users/components/CreatedByLink";
import { usePermissions } from "@/shared/auth/usePermissions";
import { KartovaPermissions } from "@/shared/auth/permissions";
import {
  useRelationshipsList, useDeleteRelationship, type RelationshipResponse,
} from "@/features/catalog/api/relationships";
import { relationshipTypeLabel, type RelationshipKind } from "@/features/catalog/relationships/relationshipTypeRules";
import { AddRelationshipDialog } from "@/features/catalog/components/AddRelationshipDialog";

interface Props {
  entityKind: RelationshipKind;
  entityId: string;
  entityTeamId: string;
  entityDisplayName: string;
}

function entityLink(kind: string, id: string) {
  return `/catalog/${kind === "Application" ? "applications" : "services"}/${id}`;
}

export function RelationshipsSection({ entityKind, entityId, entityTeamId, entityDisplayName }: Props) {
  const { hasPermission, role, teamIds } = usePermissions();
  // Confirm the OrgAdmin role literal against the permissions source during implementation.
  const canManage = hasPermission(KartovaPermissions.CatalogRelationshipsWrite) && (role === "OrgAdmin" || teamIds.includes(entityTeamId));

  const outgoing = useRelationshipsList({ entityKind, entityId, direction: "outgoing" });
  const incoming = useRelationshipsList({ entityKind, entityId, direction: "incoming" });
  const del = useDeleteRelationship();
  const [dialog, setDialog] = useState<null | "source" | "target">(null);

  const onDelete = async (id: string) => {
    if (!window.confirm("Delete this relationship?")) return;
    try { await del.mutateAsync(id); toast.success("Relationship removed"); }
    catch { toast.error("Failed to remove relationship"); }
  };

  const fixedEntity = { kind: entityKind, id: entityId, displayName: entityDisplayName };

  const group = (
    title: string,
    emptyCopy: string,
    list: ReturnType<typeof useRelationshipsList>,
    related: (r: RelationshipResponse) => RelationshipResponse["source"],
    addRole: "source" | "target",
    addLabel: string,
  ) => (
    <div className="space-y-2">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold text-primary">{title}</h3>
        {canManage && (
          <Button color="secondary" size="sm" onClick={() => setDialog(addRole)}>{addLabel}</Button>
        )}
      </div>
      {list.isLoading ? (
        <TableSkeleton rows={2} cells={canManage ? 5 : 4} />
      ) : list.isError ? (
        <p className="text-sm text-error-primary">Couldn’t load relationships.</p>
      ) : list.items.length === 0 ? (
        <p className="text-sm italic text-tertiary">{emptyCopy}</p>
      ) : (
        <>
          <Table>
            <Table.Header>
              <Table.Head id="type">Type</Table.Head>
              <Table.Head id="entity">Entity</Table.Head>
              <Table.Head id="origin">Origin</Table.Head>
              <Table.Head id="createdBy">Added by</Table.Head>
              {canManage && <Table.Head id="actions"> </Table.Head>}
            </Table.Header>
            <Table.Body>
              {list.items.map((r) => {
                const e = related(r);
                return (
                  <Table.Row key={r.id} id={r.id}>
                    <Table.Cell><Badge type="badge-modern" size="sm" color="brand">{relationshipTypeLabel[r.type as "DependsOn" | "PartOf"] ?? r.type}</Badge></Table.Cell>
                    <Table.Cell><Link to={entityLink(e.kind, e.id)} className="text-primary hover:underline">{e.displayName}</Link></Table.Cell>
                    <Table.Cell><Badge type="badge-modern" size="sm" color="gray">{r.origin}</Badge></Table.Cell>
                    <Table.Cell><CreatedByLink user={null} /></Table.Cell>
                    {canManage && (
                      <Table.Cell>
                        <Button color="tertiary" size="sm" onClick={() => onDelete(r.id)} isDisabled={del.isPending}>Delete</Button>
                      </Table.Cell>
                    )}
                  </Table.Row>
                );
              })}
            </Table.Body>
          </Table>
          <TablePager hasPrev={list.hasPrev} hasNext={list.hasNext} onPrev={list.goPrev} onNext={list.goNext} pageSize={list.items.length} />
        </>
      )}
    </div>
  );

  return (
    <section className="space-y-6" aria-label="Relationships">
      {group("Dependencies", "No dependencies.", outgoing, (r) => r.target, "source", "Add dependency")}
      {group("Dependents", `Nothing depends on this ${entityKind.toLowerCase()}.`, incoming, (r) => r.source, "target", "Add dependent")}
      {dialog && (
        <AddRelationshipDialog
          open
          onOpenChange={(o) => { if (!o) setDialog(null); }}
          fixedRole={dialog}
          fixedEntity={fixedEntity}
        />
      )}
    </section>
  );
}
```

> Notes for implementation (mechanical): (1) `CreatedByLink` takes a `UserDisplayInfo | null`; `RelationshipResponse` carries only `createdByUserId`, so `user={null}` renders the "Unknown user" affordance — acceptable (the 1a contract has no embedded creator). If a creator-display enrichment is added later, thread it here. (2) Confirm `Table.Head`/`Cell`/`Row`/`Body`/`Header`, `TablePager` props, and `Badge` `type`/`color` tokens against `ServicesTable.tsx` / `badges.tsx`. (3) Confirm the `role === "OrgAdmin"` literal against the permissions endpoint / `usePermissions`.

- [ ] **Step 4: Run to verify pass**

Run: `cmd //c "npm --prefix web run test -- RelationshipsSection"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/components/RelationshipsSection.tsx web/src/features/catalog/components/__tests__/RelationshipsSection.test.tsx
git commit -m "feat(web): relationships section (dependencies/dependents + delete)"
```

---

## Task 9: Frontend — wire the section into both detail pages

**Files:**
- Modify: `web/src/features/catalog/pages/ApplicationDetailPage.tsx`
- Modify: `web/src/features/catalog/pages/ServiceDetailPage.tsx`
- Modify: `docs/product/CHECKLIST.md`

**Interfaces:**
- Consumes: `RelationshipsSection` (Task 8). Each page already has the loaded entity (`app`/`svc`) with `id`, `teamId`, `displayName`.

- [ ] **Step 1: Insert the section into `ApplicationDetailPage.tsx`**

In the loaded `<CardContent>`, after the metadata grid section (and its trailing `<hr/>`), add:

```tsx
<hr className="border-secondary" />
<RelationshipsSection
  entityKind="Application"
  entityId={app.id}
  entityTeamId={app.teamId}
  entityDisplayName={app.displayName}
/>
```

Add the import:

```tsx
import { RelationshipsSection } from "@/features/catalog/components/RelationshipsSection";
```

(Use Serena `insert_after_symbol` / `replace_content`; match the exact loaded-variable name — `app` vs `query.data` — used in the file.)

- [ ] **Step 2: Insert the section into `ServiceDetailPage.tsx`**

After the endpoints section, add the same block with `entityKind="Service"` and the service variable (`svc.id`, `svc.teamId`, `svc.displayName`), plus the import.

- [ ] **Step 3: Run the catalog frontend tests + typecheck/build**

Run: `cmd //c "npm --prefix web run test -- features/catalog"` then `cmd //c "npm --prefix web run build"`
Expected: tests PASS; `tsc` + `vite build` succeed (gate-4 relies on the committed `openapi.ts`).

- [ ] **Step 4: Update the progress checklist**

In `docs/product/CHECKLIST.md`, mark `E-04.F-01.S-01` and `E-04.F-01.S-02` complete with a note: `(Slice 1b catalog-relationships-ui-surface, 2026-06-25; Dependencies/Dependents section on Application+Service detail pages; either-endpoint authority — ADR-0108)`.

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/pages/ApplicationDetailPage.tsx \
        web/src/features/catalog/pages/ServiceDetailPage.tsx \
        docs/product/CHECKLIST.md
git commit -m "feat(web): wire relationships section into app + service detail pages"
```

- [ ] **Step 6: Full DoD pass**

Run `scripts/ci-local.sh` (Release mirror — backend build+test, web image, helm/stryker) green. Then the DoD gates per CLAUDE.md: full build (0 warnings), full test suite, container build, `/simplify`, mutation loop (gate 6 — already run in Task 2 step 7; re-run if frontend-adjacent C# changed), `/superpowers:requesting-code-review`, `/pr-review-toolkit:review-pr`, `/deep-review`, then terminal re-verify (build + suite). Manual Playwright MCP per ADR-0084 (cold-start dev server → add an outgoing dependency on an Application, add a dependent on a Service, delete one → console clean) — flag *pending user verification* if the dev stack is unavailable in-session. Open the PR.

---

## Self-Review

**1. Spec coverage.** Every spec §4.5 file maps to a task: backend delegate + tests → Tasks 1–2; codegen → Task 3; `relationshipTypeRules` → Task 4; `api/relationships.ts` → Task 5; `EntitySearchCombobox` → Task 6; `AddRelationshipDialog` → Task 7; `RelationshipsSection` → Task 8; page wiring + checklist → Task 9. Spec §8 gate-5 artifacts: backend real-seam new cases (Tasks 1–2), five frontend Vitest files (Tasks 4–8). ADR-0108 authority (spec §3 #1, §4.1) → Tasks 1–2. Mutation gate (spec §9) → Task 2 step 7 + Task 9 step 6.

**2. Placeholder scan.** No TBD/TODO. Three "verify against the codebase" notes (Button/modal props, Table/Badge/TablePager prop names, OrgAdmin role literal) are mechanical confirmations of existing primitive APIs, not undefined logic — same standard as the accepted Service-UI slice. No step defers showing code.

**3. Type consistency.** `useRelationshipsList(params)` / `RelationshipsListParams` identical across Tasks 5, 8. `useCreateRelationship().mutateAsync(CreateRelationshipPayload)` payload shape `{sourceKind, sourceId, type, targetKind, targetId}` identical across Tasks 5, 7. `useDeleteRelationship().mutateAsync(id: string)` consistent Tasks 5, 8. `EntityOption {kind,id,displayName}` consistent Tasks 5–8. `offerableTypes`/`allowedOtherKinds`/`relationshipTypeLabel` signatures identical Tasks 4, 7. `AuthorizeEitherTeamAsync(auth, caller, Guid, Guid)` defined Task 1, reused Task 2. Direction values lowercase (`outgoing`/`incoming`) consistent with the 1a endpoint.

No blocking issues found.
