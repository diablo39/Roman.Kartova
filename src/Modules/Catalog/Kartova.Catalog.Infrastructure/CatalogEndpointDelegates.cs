using System.Security.Claims;
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Audit;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Pagination;
using Kartova.SharedKernel.Postgres.Pagination;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
// ApplicationId is aliased rather than imported via `using Kartova.Catalog.Domain`
// because that would clash with `System.ApplicationId` in the BCL — same trick
// ApplicationSortSpecs uses for `DomainApplication`.
using ApplicationId = Kartova.Catalog.Domain.ApplicationId;
using Lifecycle = Kartova.Catalog.Domain.Lifecycle;
using HealthStatus = Kartova.Catalog.Domain.HealthStatus;
using EntityRef = Kartova.Catalog.Domain.EntityRef;
using EntityKind = Kartova.Catalog.Domain.EntityKind;
using RelationshipType = Kartova.Catalog.Domain.RelationshipType;
using RelationshipDirection = Kartova.Catalog.Application.RelationshipDirection;
using SortOrder = Kartova.SharedKernel.Pagination.SortOrder;

namespace Kartova.Catalog.Infrastructure;

internal static class CatalogEndpointDelegates
{
    /// <summary>
    /// Synchronous in-process handler dispatch — invoked directly rather than
    /// via <c>IMessageBus.InvokeAsync</c>. Wolverine's bus opens its own DI
    /// scope which would not see the HTTP request's <c>ITenantScope</c> begun
    /// by <c>TenantScopeBeginMiddleware</c> (ADR-0090, formalized in ADR-0093).
    /// </summary>
    internal static async Task<IResult> RegisterApplicationAsync(
        [FromBody] RegisterApplicationRequest request,
        RegisterApplicationHandler handler,
        CatalogDbContext db,
        ITenantContext tenant,
        ClaimsPrincipal caller,
        ICurrentUser currentUser,
        IAuthorizationService auth,
        IOrganizationTeamExistenceChecker teamChecker,
        IAuditWriter audit,
        CancellationToken ct)
    {
        // ADR-0103: a new application requires an existing owning team in the
        // current tenant. Validate before dispatching — IOrganizationTeamExistenceChecker
        // is RLS-scoped, so a cross-tenant team id resolves as "not found" and hits the
        // same 422 branch as an unknown id. Mirrors the slice-8 invalid-team envelope
        // in AssignApplicationTeamAsync.
        var teamExists = await teamChecker.ExistsAsync(request.TeamId, ct);
        if (!teamExists)
        {
            return Results.Problem(
                type: ProblemTypes.InvalidTeam,
                title: "Invalid team",
                detail: "The supplied teamId does not resolve to a team in the current tenant.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        // Target-team membership gate (mirrors assign-team): a non-OrgAdmin caller cannot
        // register a new application into a team they do not belong to — that would
        // immediately leave them without access to the app they just created. The SPA picker
        // already hides such teams; this is the server-side enforcement. Reuses the shared
        // ApplicationTeamScoped policy via AuthorizeTargetTeamAsync so the OrgAdmin-OR-member
        // rule is defined exactly once (OrgAdmin is unaffected — global scope).
        if (await AuthorizeTargetTeamAsync(auth, caller, request.TeamId) is { } forbidden)
            return forbidden;

        var response = await handler.Handle(
            new RegisterApplicationCommand(request.DisplayName, request.Description, request.TeamId),
            db, tenant, currentUser, audit, ct);

        return Results.Created($"/api/v1/catalog/applications/{response.Id}", response);
    }

    internal static async Task<IResult> GetApplicationByIdAsync(
        Guid id,
        GetApplicationByIdHandler handler,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var resp = await handler.Handle(new GetApplicationByIdQuery(id), db, ct);
        if (resp is null) return EndpointResultExtensions.ApplicationNotFound();
        // Only the single-resource GET emits ETag; list rows carry `version` in
        // the body but no per-row ETag.
        return Results.Ok(resp).WithEtag(resp.Version);
    }

    /// <summary>
    /// <c>sortBy</c> and <c>sortOrder</c> are accepted as raw strings and parsed with
    /// <c>Enum.TryParse(ignoreCase: true)</c> so that the wire contract
    /// (<c>?sortBy=createdAt&amp;sortOrder=asc</c>, camelCase per ADR-0095) and the
    /// C# enum member names both bind. <c>limit</c> stays <c>string?</c> so non-integer
    /// inputs route through <c>InvalidLimitException</c> instead of the framework's
    /// generic parse-error 400.
    /// <para>
    /// <c>lifecycle</c> — ADR-0107 multi-select filter. Repeated <c>?lifecycle=</c>
    /// tokens are parsed as case-insensitive enum names; numeric tokens and unknown
    /// strings are rejected with 400 <c>invalid-lifecycle-filter</c>. Empty ⇒
    /// ADR-0073 default view (hide Decommissioned). Cursor encodes the filter
    /// (sorted comma-joined) so paging is stable; mismatch returns 400
    /// <c>cursor-filter-mismatch</c>.
    /// </para>
    /// <para>
    /// <c>teamId</c> — ADR-0107 multi-select team filter. Repeated <c>?teamId=</c>
    /// Guids narrow the result set to the selected teams. Encoded into the cursor
    /// <c>f</c>-map only when non-empty.
    /// </para>
    /// <para>
    /// <c>createdByUserId</c> — slice 9 / E2 (spec §6.5), reframed slice 10 /
    /// ADR-0103: optional filter that narrows the result set to applications whose
    /// <c>CreatedByUserId</c> matches the supplied guid. When non-null, the value is
    /// validated up-front against <see cref="IUserDirectory"/> (which is
    /// tenant-scoped, so an id from another tenant validates as "not found"). A miss
    /// surfaces as 422 <c>invalid-created-by</c>. The validation lives at the
    /// endpoint level rather than in the handler so the handler's
    /// <c>Task&lt;CursorPage&lt;T&gt;&gt;</c> return shape stays compliant with the
    /// pagination-convention architecture rule (no result-record wrapper). Mirrors
    /// the slice-8 <c>invalid-team</c> envelope pattern in
    /// <see cref="AssignApplicationTeamAsync"/>.
    /// </para>
    /// </summary>
    internal static async Task<IResult> ListApplicationsAsync(
        [FromQuery] string? sortBy,
        [FromQuery] string? sortOrder,
        [FromQuery] string? cursor,
        [FromQuery] string? limit,
        [FromQuery] string? displayNameContains,
        [FromQuery] string[]? lifecycle,
        [FromQuery] Guid[]? teamId,
        [FromQuery] Guid? createdByUserId,
        ListApplicationsHandler handler,
        CatalogDbContext db,
        IUserDirectory directory,
        CancellationToken ct)
    {
        // Enum.TryParse alone accepts numeric strings ("999", "-1") and binds them to
        // an undefined enum value. The shared CursorListBinding.Bind applies the
        // Enum.IsDefined guard plus the sort spec allow-list reject, then defers range
        // validation on limit to QueryablePagingExtensions.ToCursorPagedAsync.
        var (parsedSortBy, parsedSortOrder, effectiveLimit) = CursorListBinding.Bind<ApplicationSortField>(
            sortBy, sortOrder, limit, ApplicationSortSpecs.AllowedFieldNames);

        // Parse the repeated ?lifecycle= tokens (wire form: lowercase enum names). Reject
        // numeric tokens ("1") and unknown strings with a 400 invalid-lifecycle-filter so
        // the contract stays names-only (mirrors the sortBy IsDefined reject in CursorListBinding).
        // HashSet de-dups in place (repeated ?lifecycle=active&lifecycle=active is a no-op
        // insert) so the cursor f-map stays canonical without a second .Distinct() pass.
        var lifecycles = new HashSet<Lifecycle>();
        foreach (var raw in lifecycle ?? Array.Empty<string>())
        {
            if (int.TryParse(raw, out _)
                || !Enum.TryParse<Lifecycle>(raw, ignoreCase: true, out var parsed)
                || !Enum.IsDefined(parsed))
            {
                return Results.Problem(
                    type: ProblemTypes.InvalidLifecycleFilter,
                    title: "Invalid lifecycle filter",
                    detail: $"'{raw}' is not a valid lifecycle. Expected one of: active, deprecated, decommissioned.",
                    statusCode: StatusCodes.Status400BadRequest);
            }
            lifecycles.Add(parsed);
        }

        // Resource gate: when ?createdByUserId= is supplied, validate it resolves to a
        // user in the current tenant BEFORE invoking the handler. IUserDirectory is
        // already RLS-scoped so an id from another tenant returns null; we surface
        // both "unknown id" and "cross-tenant id" with the same 422 envelope. Mirror
        // of the slice-8 AssignApplicationTeam invalid-team pattern below.
        if (createdByUserId is { } createdByToValidate)
        {
            var user = await directory.GetAsync(createdByToValidate, ct);
            if (user is null)
            {
                return Results.Problem(
                    type: ProblemTypes.InvalidCreatedBy,
                    title: "Invalid created-by",
                    detail: "The supplied createdByUserId does not resolve to a user in the current tenant.",
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        }

        var name = string.IsNullOrWhiteSpace(displayNameContains) ? null : displayNameContains.Trim();

        var query = new ListApplicationsQuery(
            SortBy: parsedSortBy ?? ApplicationSortField.DisplayName,
            SortOrder: parsedSortOrder ?? SortOrder.Asc,
            Cursor: cursor,
            Limit: effectiveLimit,
            Lifecycle: lifecycles.ToArray(),
            // ToHashSet de-dups repeated ?teamId= values (mirrors the lifecycle HashSet) so the
            // cursor f-map stays canonical; ToArray() for the query record.
            TeamId: (teamId ?? Array.Empty<Guid>()).ToHashSet().ToArray(),
            DisplayNameContains: name,
            CreatedByUserId: createdByUserId);

        var page = await handler.Handle(query, db, ct);
        return Results.Ok(page);
    }

    internal static async Task<IResult> EditApplicationAsync(
        Guid id,
        [FromBody] EditApplicationRequest request,
        EditApplicationHandler handler,
        CatalogDbContext db,
        IAuthorizationService auth,
        ClaimsPrincipal user,
        HttpContext http,
        IAuditWriter audit,
        CancellationToken ct)
    {
        var gate = await LoadAndAuthorizeApplicationAsync(id, db, auth, user, ct);
        if (gate is not null) return gate;

        var expected = (uint)http.Items[IfMatchEndpointFilter.ExpectedVersionKey]!;

        var resp = await handler.Handle(
            new EditApplicationCommand(new ApplicationId(id), request.DisplayName, request.Description, expected),
            db, audit, ct);

        if (resp is null) return EndpointResultExtensions.ApplicationNotFound();
        return Results.Ok(resp).WithEtag(resp.Version);
    }

    internal static async Task<IResult> DeprecateApplicationAsync(
        Guid id,
        [FromBody] DeprecateApplicationRequest request,
        DeprecateApplicationHandler handler,
        CatalogDbContext db,
        IAuthorizationService auth,
        ClaimsPrincipal user,
        IAuditWriter audit,
        CancellationToken ct)
    {
        var gate = await LoadAndAuthorizeApplicationAsync(id, db, auth, user, ct);
        if (gate is not null) return gate;

        if (await RejectUnknownSuccessorAsync(request.SuccessorApplicationId, db, ct) is { } bad) return bad;

        var resp = await handler.Handle(
            new DeprecateApplicationCommand(new ApplicationId(id), request.SunsetDate, request.SuccessorApplicationId),
            db, audit, ct);

        if (resp is null) return EndpointResultExtensions.ApplicationNotFound();
        return Results.Ok(resp);
    }

    internal static async Task<IResult> DecommissionApplicationAsync(
        Guid id,
        [FromBody] DecommissionApplicationRequest? request,
        DecommissionApplicationHandler handler,
        CatalogDbContext db,
        IAuthorizationService auth,
        ClaimsPrincipal user,
        IAuditWriter audit,
        CancellationToken ct)
    {
        var gate = await LoadAndAuthorizeApplicationAsync(id, db, auth, user, ct);
        if (gate is not null) return gate;

        var overrideSunset = request?.OverrideSunset ?? false;
        if (overrideSunset)
        {
            var ovr = await auth.AuthorizeAsync(user, KartovaPermissions.CatalogApplicationsLifecycleOverride);
            if (!ovr.Succeeded) return Results.Forbid();
        }

        var resp = await handler.Handle(
            new DecommissionApplicationCommand(new ApplicationId(id), overrideSunset), db, audit, ct);

        if (resp is null) return EndpointResultExtensions.ApplicationNotFound();
        return Results.Ok(resp);
    }

    /// <summary>
    /// PUT /applications/{id}/successor — set/clear successor while Deprecated
    /// (ADR-0110, ADR-0096 PUT-idempotent-replacement; <see langword="null"/>
    /// clears). Successor existence pre-check mirrors the identical 422 envelope
    /// in <see cref="DeprecateApplicationAsync"/> — cross-tenant ids are invisible
    /// under RLS, so both an unknown id and a cross-tenant id surface 422 here.
    /// A self-successor id DOES exist (it's the app's own row), so it passes this
    /// check and is rejected 400 by the domain guard
    /// (<c>Application.SetSuccessor</c> → <c>RejectSelfSuccessor</c>). Not-Deprecated
    /// source surfaces 409 via the same domain method.
    /// </summary>
    internal static async Task<IResult> SetApplicationSuccessorAsync(
        Guid id,
        [FromBody] SetApplicationSuccessorRequest request,
        SetApplicationSuccessorHandler handler,
        CatalogDbContext db,
        IAuthorizationService auth,
        ClaimsPrincipal user,
        IAuditWriter audit,
        CancellationToken ct)
    {
        var gate = await LoadAndAuthorizeApplicationAsync(id, db, auth, user, ct);
        if (gate is not null) return gate;

        if (await RejectUnknownSuccessorAsync(request.SuccessorApplicationId, db, ct) is { } bad) return bad;

        var resp = await handler.Handle(
            new SetApplicationSuccessorCommand(new ApplicationId(id), request.SuccessorApplicationId), db, audit, ct);

        if (resp is null) return EndpointResultExtensions.ApplicationNotFound();
        return Results.Ok(resp);
    }

    internal static async Task<IResult> ReactivateApplicationAsync(
        Guid id,
        ReactivateApplicationHandler handler,
        CatalogDbContext db,
        IAuthorizationService auth,
        ClaimsPrincipal user,
        IAuditWriter audit,
        CancellationToken ct)
    {
        var gate = await LoadAndAuthorizeApplicationAsync(id, db, auth, user, ct);
        if (gate is not null) return gate;

        var resp = await handler.Handle(
            new ReactivateApplicationCommand(new ApplicationId(id)), db, audit, ct);

        if (resp is null) return EndpointResultExtensions.ApplicationNotFound();
        return Results.Ok(resp);
    }

    internal static async Task<IResult> UnDecommissionApplicationAsync(
        Guid id,
        [FromBody] UnDecommissionApplicationRequest request,
        UnDecommissionApplicationHandler handler,
        CatalogDbContext db,
        IAuthorizationService auth,
        ClaimsPrincipal user,
        IAuditWriter audit,
        CancellationToken ct)
    {
        var gate = await LoadAndAuthorizeApplicationAsync(id, db, auth, user, ct);
        if (gate is not null) return gate;

        var resp = await handler.Handle(
            new UnDecommissionApplicationCommand(new ApplicationId(id), request.SunsetDate),
            db, audit, ct);

        if (resp is null) return EndpointResultExtensions.ApplicationNotFound();
        return Results.Ok(resp);
    }

    /// <summary>
    /// PUT /applications/{id}/team — reassigns the team that owns the application
    /// (required, no unassign — ADR-0103). Slice 8 / ADR-0098 §6.4.
    /// <para>
    /// Two-gate authorization: the claim gate
    /// (<c>KartovaPermissions.CatalogApplicationsEditMetadata</c>) is applied
    /// at the route level via <c>.RequireAuthorization</c>; the resource gate
    /// (<see cref="KartovaTeamPolicies.ApplicationTeamScoped"/> — OrgAdmin OR
    /// member of the app's current team) runs here against the pre-loaded app
    /// so we can return 403 without leaking team-id existence.
    /// </para>
    /// <para>
    /// Pre-load + handler-reload mirrors <c>UpdateTeamAsync</c>: the pre-load
    /// is needed to evaluate the resource policy; the handler reload defends
    /// against a concurrent delete between the two reads (defensive 404).
    /// Invalid team (id that does not exist in the tenant) surfaces as 422
    /// <c>invalid-team</c> per spec §6.4. A Decommissioned target app throws
    /// <c>InvalidLifecycleTransitionException</c> inside the domain method; the
    /// shared lifecycle handler maps it to 409.
    /// </para>
    /// <para>
    /// Slice-8 boundary-review fix SF-2: a target-team membership check runs
    /// between the source-team gate (above) and the handler dispatch. A
    /// non-OrgAdmin caller cannot reassign the app to a team they do not
    /// belong to — that would orphan them from the app on the very next
    /// request. The SPA picker already hides such targets; this is the
    /// server-side enforcement so non-SPA clients cannot bypass it. OrgAdmin
    /// is unaffected.
    /// </para>
    /// </summary>
    internal static async Task<IResult> AssignApplicationTeamAsync(
        Guid id,
        [FromBody] AssignTeamRequest request,
        AssignApplicationTeamHandler handler,
        CatalogDbContext db,
        IAuthorizationService auth,
        ClaimsPrincipal user,
        IAuditWriter audit,
        CancellationToken ct)
    {
        var gate = await LoadAndAuthorizeApplicationAsync(id, db, auth, user, ct);
        if (gate is not null) return gate;

        // Target-team membership gate: block non-OrgAdmin callers from moving the app to a
        // team they aren't a member of. Reuses the same shared ApplicationTeamScoped policy
        // as the source-team gate above (see AuthorizeTargetTeamAsync). OrgAdmin always allowed.
        if (await AuthorizeTargetTeamAsync(auth, user, request.TeamId) is { } forbidden)
            return forbidden;

        var result = await handler.Handle(
            new AssignApplicationTeamCommand(id, request.TeamId), db, audit, ct);

        if (result.IsNotFound) return EndpointResultExtensions.ApplicationNotFound();
        if (result.IsInvalidTeam)
            return Results.Problem(
                type: ProblemTypes.InvalidTeam,
                title: "Invalid team",
                detail: "The target team does not exist in the current tenant.",
                statusCode: StatusCodes.Status422UnprocessableEntity);

        return Results.Ok(result.App!.ToResponse());
    }

    internal static async Task<IResult> RegisterServiceAsync(
        [FromBody] RegisterServiceRequest request,
        RegisterServiceHandler handler,
        CatalogDbContext db,
        ITenantContext tenant,
        ClaimsPrincipal caller,
        ICurrentUser currentUser,
        IAuthorizationService auth,
        IOrganizationTeamExistenceChecker teamChecker,
        IAuditWriter audit,
        CancellationToken ct)
    {
        // ADR-0103: a new service requires an existing owning team in the tenant.
        // RLS-scoped checker → a cross-tenant id resolves as "not found" (same 422 branch).
        if (!await teamChecker.ExistsAsync(request.TeamId, ct))
        {
            return Results.Problem(
                type: ProblemTypes.InvalidTeam,
                title: "Invalid team",
                detail: "The supplied teamId does not resolve to a team in the current tenant.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        // Target-team membership gate: a non-OrgAdmin caller cannot register into a team
        // they do not belong to (reuses the shared ApplicationTeamScoped policy).
        if (await AuthorizeTargetTeamAsync(auth, caller, request.TeamId) is { } forbidden)
            return forbidden;

        var endpoints = (request.Endpoints ?? Array.Empty<ServiceEndpointDto>())
            .Where(e => e is not null)
            .Select(e => new ServiceEndpointInput(e.Url, e.Protocol))
            .ToList();

        var response = await handler.Handle(
            new RegisterServiceCommand(request.DisplayName, request.Description, request.TeamId, endpoints),
            db, tenant, currentUser, audit, ct);

        return Results.Created($"/api/v1/catalog/services/{response.Id}", response);
    }

    internal static async Task<IResult> GetServiceByIdAsync(
        Guid id,
        GetServiceByIdHandler handler,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var resp = await handler.Handle(new GetServiceByIdQuery(id), db, ct);
        if (resp is null) return EndpointResultExtensions.ServiceNotFound();
        return Results.Ok(resp).WithEtag(resp.Version);
    }

    /// <summary>
    /// <c>sortBy</c> and <c>sortOrder</c> are accepted as raw strings and parsed with
    /// <c>Enum.TryParse(ignoreCase: true)</c> so that the wire contract
    /// (<c>?sortBy=createdAt&amp;sortOrder=asc</c>, camelCase per ADR-0095) and the
    /// C# enum member names both bind. <c>limit</c> stays <c>string?</c> so non-integer
    /// inputs route through <c>InvalidLimitException</c> instead of the framework's
    /// generic parse-error 400.
    /// <para>
    /// <c>teamId</c> — ADR-0107 multi-select team filter. Repeated <c>?teamId=</c>
    /// Guids narrow the result set to the selected teams. Encoded into the cursor
    /// <c>f</c>-map only when non-empty. Empty ⇒ no predicate (show all).
    /// </para>
    /// <para>
    /// <c>health</c> — ADR-0107 multi-select health filter. Repeated <c>?health=</c>
    /// tokens are parsed as case-insensitive enum names; numeric tokens and unknown
    /// strings are rejected with 400 <c>invalid-health-filter</c>. Empty ⇒ no predicate
    /// (show all health statuses — no ADR-0073 default-view rule applies to Services).
    /// </para>
    /// </summary>
    internal static async Task<IResult> ListServicesAsync(
        [FromQuery] string? sortBy,
        [FromQuery] string? sortOrder,
        [FromQuery] string? cursor,
        [FromQuery] string? limit,
        [FromQuery] string? displayNameContains,
        [FromQuery] Guid[]? teamId,
        [FromQuery] string[]? health,
        ListServicesHandler handler,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var (parsedSortBy, parsedSortOrder, effectiveLimit) =
            CursorListBinding.Bind<ServiceSortField>(sortBy, sortOrder, limit, ServiceSortSpecs.AllowedFieldNames);

        // Parse the repeated ?health= tokens (wire form: lowercase enum names). Reject
        // numeric tokens ("1") and unknown strings with a 400 invalid-health-filter so
        // the contract stays names-only (mirrors the lifecycle token parse in ListApplicationsAsync).
        // HashSet de-dups in place (repeated ?health=healthy&health=healthy is a no-op
        // insert) so the cursor f-map stays canonical without a second .Distinct() pass.
        var healthSet = new HashSet<HealthStatus>();
        foreach (var raw in health ?? Array.Empty<string>())
        {
            if (int.TryParse(raw, out _)
                || !Enum.TryParse<HealthStatus>(raw, ignoreCase: true, out var parsed)
                || !Enum.IsDefined(parsed))
            {
                return Results.Problem(
                    type: ProblemTypes.InvalidHealthFilter,
                    title: "Invalid health filter",
                    detail: $"'{raw}' is not a valid health status. Expected one of: unknown, healthy, degraded, unhealthy.",
                    statusCode: StatusCodes.Status400BadRequest);
            }
            healthSet.Add(parsed);
        }

        // Blank/whitespace ⇒ no filter (filter-absent must equal today's unfiltered cursor).
        var name = string.IsNullOrWhiteSpace(displayNameContains) ? null : displayNameContains.Trim();

        var query = new ListServicesQuery(
            SortBy: parsedSortBy ?? ServiceSortField.DisplayName,   // default flips: was CreatedAt
            SortOrder: parsedSortOrder ?? SortOrder.Asc,            // default flips: was Desc
            Cursor: cursor,
            Limit: effectiveLimit,
            // ToHashSet de-dups repeated ?teamId= values so the cursor f-map stays canonical; ToArray() for the query record.
            TeamId: (teamId ?? Array.Empty<Guid>()).ToHashSet().ToArray(),
            Health: healthSet.ToArray(),
            DisplayNameContains: name);

        var page = await handler.Handle(query, db, ct);
        return Results.Ok(page);
    }

    internal static async Task<IResult> RegisterApiAsync(
        [FromBody] RegisterApiRequest request,
        RegisterApiHandler handler,
        CatalogDbContext db,
        ITenantContext tenant,
        ClaimsPrincipal caller,
        ICurrentUser currentUser,
        IAuthorizationService auth,
        IOrganizationTeamExistenceChecker teamChecker,
        IAuditWriter audit,
        CancellationToken ct)
    {
        // ADR-0103: a new API requires an existing owning team in the tenant.
        // RLS-scoped checker → a cross-tenant id resolves as "not found" (same 422 branch).
        if (!await teamChecker.ExistsAsync(request.TeamId, ct))
        {
            return Results.Problem(
                type: ProblemTypes.InvalidTeam,
                title: "Invalid team",
                detail: "The supplied teamId does not resolve to a team in the current tenant.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        // Target-team membership gate (reuses the shared ApplicationTeamScoped policy).
        if (await AuthorizeTargetTeamAsync(auth, caller, request.TeamId) is { } forbidden)
            return forbidden;

        var response = await handler.Handle(
            new RegisterApiCommand(
                request.DisplayName, request.Description, request.Style, request.Version, request.SpecUrl, request.TeamId),
            db, tenant, currentUser, audit, ct);

        return Results.Created($"/api/v1/catalog/apis/{response.Id}", response);
    }

    internal static async Task<IResult> GetApiByIdAsync(
        Guid id,
        GetApiByIdHandler handler,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var resp = await handler.Handle(new GetApiByIdQuery(id), db, ct);
        if (resp is null) return EndpointResultExtensions.ApiNotFound();
        return Results.Ok(resp);
    }

    internal static async Task<IResult> ListApisAsync(
        [FromQuery] string? sortBy,
        [FromQuery] string? sortOrder,
        [FromQuery] string? cursor,
        [FromQuery] string? limit,
        ListApisHandler handler,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var (parsedSortBy, parsedSortOrder, effectiveLimit) =
            CursorListBinding.Bind<ApiSortField>(sortBy, sortOrder, limit, ApiSortSpecs.AllowedFieldNames);

        var query = new ListApisQuery(
            SortBy: parsedSortBy ?? ApiSortField.DisplayName,
            SortOrder: parsedSortOrder ?? SortOrder.Asc,
            Cursor: cursor,
            Limit: effectiveLimit,
            // Filter query-string binding lands in the API-UI slice (FU-9 Task 2); empty ⇒ no predicate.
            TeamId: Array.Empty<Guid>(),
            Style: Array.Empty<Kartova.Catalog.Domain.ApiStyle>());

        var page = await handler.Handle(query, db, ct);
        return Results.Ok(page);
    }

    internal static async Task<IResult> CreateRelationshipAsync(
        [FromBody] CreateRelationshipRequest req,
        ICatalogEntityLookup lookup,
        CreateRelationshipHandler handler,
        CatalogDbContext db,
        ITenantContext tenant,
        ICurrentUser currentUser,
        ClaimsPrincipal caller,
        IAuthorizationService auth,
        IAuditWriter audit,
        CancellationToken ct)
    {
        var source = new EntityRef(req.SourceKind, req.SourceId);
        var target = new EntityRef(req.TargetKind, req.TargetId);

        var sourceInfo = await lookup.Find(source.Kind, source.Id, ct);
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

        // ADR-0108: either-endpoint authority — OrgAdmin or member of source OR target team.
        if (await AuthorizeEitherTeamAsync(auth, caller, sourceInfo.TeamId, targetInfo.TeamId) is { } forbidden)
            return forbidden;

        // Duplicate pre-check via direct ComplexProperty navigation — EF 10 translates
        // r.Source.Kind / r.Target.Kind through the ComplexProperty mapping.
        // The unique index ux_relationships_edge backstops concurrent races.
        var exists = await db.Relationships.AnyAsync(r =>
            r.Source.Kind == source.Kind
            && r.Source.Id == source.Id
            && r.Type == req.Type
            && r.Target.Kind == target.Kind
            && r.Target.Id == target.Id, ct);
        if (exists)
            return Results.Problem(
                type: ProblemTypes.RelationshipAlreadyExists,
                title: "Relationship already exists",
                detail: "An identical relationship already exists.",
                statusCode: StatusCodes.Status409Conflict);

        var srcDto = new EntityRefDto(source.Kind, source.Id, sourceInfo.DisplayName);
        var tgtDto = new EntityRefDto(target.Kind, target.Id, targetInfo.DisplayName);
        var cmd = new CreateRelationshipCommand(source, target, req.Type);

        var response = await handler.Handle(cmd, srcDto, tgtDto, db, tenant, currentUser, audit, ct);
        return Results.Created($"/api/v1/catalog/relationships/{response.Id}", response);
    }

    internal static async Task<IResult> DeleteRelationshipAsync(
        Guid id,
        ICatalogEntityLookup lookup,
        DeleteRelationshipHandler handler,
        CatalogDbContext db,
        ClaimsPrincipal caller,
        IAuthorizationService auth,
        IAuditWriter audit,
        CancellationToken ct)
    {
        var rel = await db.Relationships.FirstOrDefaultAsync(r => EF.Property<Guid>(r, EfRelationshipConfiguration.IdFieldName) == id, ct);
        if (rel is null)
            return Results.Problem(
                type: ProblemTypes.ResourceNotFound,
                title: "Not found",
                statusCode: StatusCodes.Status404NotFound);

        var sourceInfo = await lookup.Find(rel.Source.Kind, rel.Source.Id, ct);
        var targetInfo = await lookup.Find(rel.Target.Kind, rel.Target.Id, ct);

        // Either-endpoint authority (ADR-0108). A hard-deleted endpoint resolves to
        // Guid.Empty (only OrgAdmin passes); both deleted -> OrgAdmin-only.
        if (await AuthorizeEitherTeamAsync(
                auth, caller,
                sourceInfo?.TeamId ?? Guid.Empty,
                targetInfo?.TeamId ?? Guid.Empty) is { } forbidden)
            return forbidden;

        await handler.Handle(rel, db, audit, ct);
        return Results.NoContent();
    }

    /// <summary>
    /// GET /relationships?entityKind=&amp;entityId=&amp;direction= — list relationships for an entity.
    /// Returns a cursor-paged list of relationships where the given entity is the source, target, or either.
    /// Default sort: createdAt desc (newest first) — relationships have no displayName of their own,
    /// so the project-wide displayName-asc list default deliberately does not apply here. Claim gate: catalog.read.
    /// </summary>
    internal static async Task<IResult> ListRelationshipsAsync(
        [FromQuery] string entityKind,
        [FromQuery] Guid entityId,
        [FromQuery] string? direction,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortOrder,
        [FromQuery] string? cursor,
        [FromQuery] string? limit,
        ListRelationshipsForEntityHandler handler,
        ICatalogEntityLookup lookup,
        CatalogDbContext db,
        CancellationToken ct)
    {
        if (!Enum.TryParse<EntityKind>(entityKind, ignoreCase: true, out var kind) || !Enum.IsDefined(kind) || entityId == Guid.Empty)
            return Results.Problem(type: ProblemTypes.ValidationFailed, title: "Invalid entity reference",
                detail: "entityKind and a non-empty entityId are required.", statusCode: StatusCodes.Status400BadRequest);

        var dir = RelationshipDirection.All;
        if (!string.IsNullOrWhiteSpace(direction)
            && (!Enum.TryParse(direction, ignoreCase: true, out dir) || !Enum.IsDefined(dir)))
            return Results.Problem(type: ProblemTypes.ValidationFailed, title: "Invalid direction",
                detail: "direction must be outgoing, incoming, or all.", statusCode: StatusCodes.Status400BadRequest);

        var (parsedSortBy, parsedSortOrder, effectiveLimit) =
            CursorListBinding.Bind<RelationshipSortField>(sortBy, sortOrder, limit, RelationshipSortSpecs.AllowedFieldNames);

        var query = new ListRelationshipsForEntityQuery(
            new EntityRef(kind, entityId), dir,
            SortBy: parsedSortBy ?? RelationshipSortField.CreatedAt,
            SortOrder: parsedSortOrder ?? SortOrder.Desc,
            Cursor: cursor, Limit: effectiveLimit);

        var page = await handler.Handle(query, db, lookup, ct);
        return Results.Ok(page);
    }

    /// <summary>
    /// GET /graph?entityKind=&amp;entityId=&amp;depth=&amp;direction= — BFS dependency neighbourhood
    /// around the focus entity. depth 1..4 (default 2); direction outgoing|incoming|all (default all).
    /// Bounded aggregate (node cap + truncated flag) — not a cursor list. Claim gate: catalog.read.
    /// </summary>
    internal static async Task<IResult> GetCatalogGraphAsync(
        [FromQuery] string entityKind,
        [FromQuery] Guid entityId,
        [FromQuery] int? depth,
        [FromQuery] string? direction,
        GraphTraversalHandler handler,
        ICatalogEntityLookup lookup,
        CatalogDbContext db,
        CancellationToken ct)
    {
        if (!Enum.TryParse<EntityKind>(entityKind, ignoreCase: true, out var kind) || !Enum.IsDefined(kind) || entityId == Guid.Empty)
            return Results.Problem(type: ProblemTypes.ValidationFailed, title: "Invalid entity reference",
                detail: "entityKind and a non-empty entityId are required.", statusCode: StatusCodes.Status400BadRequest);

        var dir = RelationshipDirection.All;
        if (!string.IsNullOrWhiteSpace(direction)
            && (!Enum.TryParse(direction, ignoreCase: true, out dir) || !Enum.IsDefined(dir)))
            return Results.Problem(type: ProblemTypes.ValidationFailed, title: "Invalid direction",
                detail: "direction must be outgoing, incoming, or all.", statusCode: StatusCodes.Status400BadRequest);

        var effectiveDepth = depth ?? 2;
        if (effectiveDepth < 1 || effectiveDepth > 4)
            return Results.Problem(type: ProblemTypes.ValidationFailed, title: "Invalid depth",
                detail: "depth must be between 1 and 4.", statusCode: StatusCodes.Status400BadRequest);

        var query = new GraphTraversalQuery(new EntityRef(kind, entityId), effectiveDepth, dir);
        var graph = await handler.Handle(query, db, lookup, ct);
        return Results.Ok(graph);
    }

    // ----- shared helpers -----------------------------------------------

    /// <summary>
    /// Loads the application by id (RLS-scoped to the current tenant) and runs
    /// the <see cref="KartovaTeamPolicies.ApplicationTeamScoped"/> resource gate
    /// against it. Returns <c>null</c> on success; otherwise returns the response
    /// to short-circuit with (404 if the app is not visible, 403 if the caller
    /// is neither OrgAdmin nor a member of the app's owning team). Used by every
    /// mutation endpoint on <c>/applications/{id}</c> — Edit, Deprecate,
    /// Decommission, Reactivate, UnDecommission, and AssignTeam (slice 8).
    /// </summary>
    private static async Task<IResult?> LoadAndAuthorizeApplicationAsync(
        Guid id,
        CatalogDbContext db,
        IAuthorizationService auth,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var app = await db.Applications.FirstOrDefaultAsync(ApplicationSortSpecs.IdEquals(id), ct);
        if (app is null) return EndpointResultExtensions.ApplicationNotFound();

        var authResult = await auth.AuthorizeAsync(user, app, KartovaTeamPolicies.ApplicationTeamScoped);
        if (!authResult.Succeeded) return Results.Forbid();

        return null;
    }

    /// <summary>
    /// Successor existence pre-check (RLS-scoped), shared by the Deprecate and
    /// Set-Successor endpoints. A cross-tenant id is invisible under RLS, so both
    /// an unknown id and a cross-tenant id surface the same 422 here. A self-successor
    /// id DOES exist (it's this row), so it passes this check and is rejected as 400
    /// by the domain guard (<c>Application</c> → <c>RejectSelfSuccessor</c>). Returns
    /// <c>null</c> when the successor is null or resolves; otherwise the 422 problem.
    /// </summary>
    private static async Task<IResult?> RejectUnknownSuccessorAsync(
        Guid? successorId, CatalogDbContext db, CancellationToken ct)
        => successorId is { } id
            && !await db.Applications.AnyAsync(ApplicationSortSpecs.IdEquals(id), ct)
            ? Results.Problem(
                type: ProblemTypes.InvalidSuccessor,
                title: "Invalid successor",
                detail: "The supplied successorApplicationId does not resolve to an application in the current tenant.",
                statusCode: StatusCodes.Status422UnprocessableEntity)
            : null;

    /// <summary>
    /// Runs the shared <see cref="KartovaTeamPolicies.ApplicationTeamScoped"/> resource gate
    /// against a <em>target</em> team id (a team the caller wants to register into or reassign
    /// to), by wrapping it in an <see cref="ITeamScopedResource"/>. Returns <c>null</c> when the
    /// caller is OrgAdmin or a member of that team; otherwise <c>Results.Forbid()</c> (403).
    /// Keeps the OrgAdmin-OR-member rule in one place — the same policy/handler that gates the
    /// source app in <see cref="LoadAndAuthorizeApplicationAsync"/> — so register and assign-team
    /// cannot drift from it.
    /// </summary>
    private static async Task<IResult?> AuthorizeTargetTeamAsync(
        IAuthorizationService auth, ClaimsPrincipal user, Guid teamId)
    {
        var gate = await auth.AuthorizeAsync(user, new TargetTeam(teamId), KartovaTeamPolicies.ApplicationTeamScoped);
        return gate.Succeeded ? null : Results.Forbid();
    }

    /// <summary>
    /// ADR-0108: authorized when caller is OrgAdmin OR is a member of AT LEAST ONE
    /// of the two connected-entity teams. Guid.Empty is treated as "no team" — only
    /// OrgAdmin passes that check (via <see cref="AuthorizeTargetTeamAsync"/>).
    /// Returns <c>null</c> when authorized; a forbidden <see cref="IResult"/> otherwise.
    /// </summary>
    internal static async Task<IResult?> AuthorizeEitherTeamAsync(
        IAuthorizationService auth, ClaimsPrincipal caller, Guid teamA, Guid teamB)
    {
        // OrgAdmin passes any team check (short-circuits on teamA).
        // A deleted endpoint contributes Guid.Empty, which only OrgAdmin passes.
        if (await AuthorizeTargetTeamAsync(auth, caller, teamA) is null)
            return null;
        return await AuthorizeTargetTeamAsync(auth, caller, teamB);
    }

    /// <summary>Lightweight <see cref="ITeamScopedResource"/> over a target team id.</summary>
    private sealed record TargetTeam(Guid? TeamId) : ITeamScopedResource;
}
