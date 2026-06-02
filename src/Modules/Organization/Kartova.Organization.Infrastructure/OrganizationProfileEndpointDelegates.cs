using System.Security.Claims;
using Kartova.Organization.Application;
using Kartova.Organization.Contracts;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Endpoint delegates for the Organization profile surface: GET/PUT
/// <c>/me</c>, the logo upload/clear/serve trio under <c>/me/logo</c>, and
/// the permission / admin-only probes under <c>/me/permissions</c> and
/// <c>/me/admin-only</c>. Split out of the legacy
/// <c>OrganizationEndpointDelegates</c> aggregator (slice-9 carry-forward #16) —
/// behavior is identical, only the host type name changed.
/// </summary>
internal static class OrganizationProfileEndpointDelegates
{
    // Must match the OrgLogo.Create invariant
    // (src/Modules/Organization/Kartova.Organization.Domain/OrgLogo.cs:22).
    // The aggregate enforces <= 256 KiB; the endpoint enforces the same limit
    // early so a hostile uploader can't stream gigabytes before the domain
    // validation kicks in.
    private const int LogoMaxBytes = 256 * 1024;

    // Read buffer for the upload stream. 8 KiB matches Kestrel's default
    // request-body buffer and is well below the LogoMaxBytes ceiling.
    private const int LogoUploadReadBuffer = 8192;
    internal static async Task<IResult> GetMeAsync(OrgProfileQueries queries, CancellationToken ct)
    {
        var profile = await queries.GetMyOrgAsync(ct);
        if (profile is null) return OrgNotFound();
        return Results.Ok(profile);
    }

    /// <summary>
    /// Slice-9 spec §4: applies the supplied <see cref="UpdateOrgProfileRequest"/>
    /// to the current tenant's <c>Organization</c>. The If-Match header is
    /// reserved on the wire contract but not parsed here — see the inline note
    /// inside the body for the deferral rationale.
    /// </summary>
    internal static async Task<IResult> UpdateMeAsync(
        [FromBody] UpdateOrgProfileRequest body,
        UpdateOrgProfileHandler handler,
        CancellationToken ct)
    {
        // If-Match wire contract reserved per slice-9 spec §4 + ADR-0096.
        // The Organization aggregate does not carry an EF concurrency token yet,
        // so the header would be a no-op even if parsed. When xmin mapping lands,
        // wire IfMatchEndpointFilter (already used by CatalogModule) onto this
        // endpoint and pull the expected token from HttpContext.Items rather than
        // re-implementing header parsing in the delegate.
        var result = await handler.HandleAsync(body, ifMatch: null, ct);
        return result switch
        {
            UpdateOrgProfileResult.Ok => Results.NoContent(),
            UpdateOrgProfileResult.NotFound => OrgNotFound(),
            UpdateOrgProfileResult.ConcurrencyConflict => Results.Problem(
                type: ProblemTypes.ConcurrencyConflict,
                title: "Concurrency conflict",
                detail: "The Organization row was modified by a concurrent request.",
                statusCode: StatusCodes.Status412PreconditionFailed),
            _ => Results.StatusCode(500),
        };
    }

    internal static IResult GetAdminOnlyAsync()
    {
        return Results.Ok(new AdminOnlyResponse("ok"));
    }

    internal static IResult GetMePermissions(ICurrentUser currentUser, ClaimsPrincipal user)
    {
        // Spec §3 Decision #2: each user holds exactly one realm role.
        // FirstOrDefault is the explicit choice — if multiple ClaimTypes.Role
        // claims somehow arrive on the principal, only the first is surfaced.
        var role = user.FindAll(ClaimTypes.Role)
                       .Select(c => c.Value)
                       .FirstOrDefault();

        var permissions = user.FindAll(KartovaClaims.Permission)
                              .Select(c => c.Value)
                              .ToArray();

        // Slice 8 §7.2: surface the caller's team memberships so the SPA can
        // gate team-admin-of-this UI without a second round-trip. Role is the
        // string form of TeamRoleKind ("Admin" | "Member"), matching the
        // TeamMemberResponse wire shape.
        var memberships = currentUser.TeamMemberships
            .Select(m => new MeTeamMembership(m.TeamId, m.Role.ToString()))
            .ToArray();

        return Results.Ok(new MePermissionsResponse(role, permissions, memberships));
    }

    // ----- Logo upload / clear / serve (slice 9 spec §6.4) --------------

    /// <summary>
    /// PUT <c>/me/logo</c>: streams the request body (capped at
    /// <see cref="LogoMaxBytes"/>), validates content-type + magic bytes,
    /// sanitizes SVG, and persists via <see cref="LogoCommands.UploadAsync"/>.
    /// Returns 200 + <see cref="UploadLogoResponse"/> on success, RFC 7807
    /// envelopes for 413/415/422/404. Content-Type is parsed via
    /// <see cref="MediaTypeHeaderValue"/> so <c>image/PNG</c> and
    /// <c>image/png; charset=utf-8</c> both normalize correctly.
    /// </summary>
    internal static async Task<IResult> UploadLogoAsync(
        HttpRequest req,
        LogoCommands cmds,
        CancellationToken ct)
    {
        var rawContentType = req.Headers.ContentType.ToString();
        string? mediaType = null;
        if (MediaTypeHeaderValue.TryParse(rawContentType, out var mt))
        {
            mediaType = mt.MediaType.Value?.ToLowerInvariant();
        }

        if (mediaType is not ("image/png" or "image/jpeg" or "image/svg+xml"))
        {
            return Results.Problem(
                type: ProblemTypes.UnsupportedLogoMedia,
                title: "Unsupported logo media type",
                detail: "Content-Type must be image/png, image/jpeg, or image/svg+xml.",
                statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        // Stream the body with an explicit size ceiling rather than trusting
        // Content-Length (which a hostile client can spoof or omit).
        using var ms = new MemoryStream();
        var buffer = new byte[LogoUploadReadBuffer];
        int read;
        while ((read = await req.Body.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            ms.Write(buffer, 0, read);
            if (ms.Length > LogoMaxBytes)
            {
                return Results.Problem(
                    type: ProblemTypes.LogoTooLarge,
                    title: "Logo too large",
                    detail: $"Logo bytes must be <= {LogoMaxBytes:N0} bytes.",
                    statusCode: StatusCodes.Status413PayloadTooLarge);
            }
        }
        var bytes = ms.ToArray();

        var result = await cmds.UploadAsync(bytes, mediaType, ct);
        return result switch
        {
            UploadLogoResult.Accepted a => Results.Ok(new UploadLogoResponse(a.Etag, a.MimeType)),
            UploadLogoResult.Rejected r => Results.Problem(
                type: ProblemTypes.LogoInvalidContent,
                title: "Logo rejected",
                detail: r.Reason,
                statusCode: StatusCodes.Status422UnprocessableEntity),
            UploadLogoResult.NotFound => OrgNotFound(),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>
    /// DELETE <c>/me/logo</c>: clears the logo on the current tenant's
    /// Organization aggregate. 204 on success, RFC 7807 404 when no
    /// Organization is visible (RLS or missing row).
    /// </summary>
    internal static async Task<IResult> DeleteLogoAsync(
        LogoCommands cmds,
        CancellationToken ct)
    {
        var ok = await cmds.ClearAsync(ct);
        if (!ok) return OrgNotFound();
        return Results.NoContent();
    }

    /// <summary>
    /// GET <c>/me/logo</c>: serves the raw logo bytes with a strong ETag
    /// (SHA-256 hex of the bytes) and <c>Cache-Control: private, max-age=300</c>.
    /// Honors <c>If-None-Match</c> for 304 short-circuit. This is the cache-
    /// validation header (RFC 7232 §3.2), distinct from <c>If-Match</c> which
    /// is used for optimistic concurrency on the profile PUT.
    /// </summary>
    internal static async Task<IResult> GetLogoAsync(
        LogoCommands cmds,
        HttpContext http,
        CancellationToken ct)
    {
        var data = await cmds.GetServeDataAsync(ct);
        if (data is null)
        {
            return Results.Problem(
                type: ProblemTypes.ResourceNotFound,
                title: "Organization logo not found",
                detail: "No logo is set on the current tenant's Organization.",
                statusCode: StatusCodes.Status404NotFound);
        }

        // RFC 7232: parse If-None-Match via EntityTagHeaderValue so we correctly
        // handle quoted strong validators ("hash") and reject weak validators
        // (W/"hash"). The server only emits strong ETags, so a weak If-None-Match
        // cannot match anything we store and must fall through to a 200.
        string? ifNoneMatch = null;
        var rawIfNoneMatch = http.Request.Headers.IfNoneMatch.FirstOrDefault();
        if (!string.IsNullOrEmpty(rawIfNoneMatch)
            && EntityTagHeaderValue.TryParse(rawIfNoneMatch, out var etag)
            && !etag.IsWeak)
        {
            // EntityTagHeaderValue.Tag includes the surrounding quotes
            // (e.g. "abc123"); strip them to match the raw ContentHash hex.
            ifNoneMatch = etag.Tag.Value?.Trim('"');
        }

        // ETag is the validator; Cache-Control gives downstream caches freshness.
        // Both must be present on 304 and 200 per RFC 7232 §4.1, otherwise an
        // intermediate cache won't extend its freshness window after a successful
        // revalidation. Set both before branching so neither path can forget one.
        http.Response.Headers.ETag = $"\"{data.ContentHash}\"";
        http.Response.Headers.CacheControl = "private, max-age=300";

        // Defence-in-depth on the SVG render path. D3's allow-list sanitizer is the
        // primary defence (no <script>, no event handlers, no http(s) hrefs, only
        // data: scheme). These two headers cover the case where an attacker bypasses
        // the sanitizer AND a user opens /me/logo directly in a browser tab (where
        // SVG renders as a *document* and inline JS would execute — unlike <img src>
        // usage which never executes SVG scripts).
        //   • CSP sandbox + default-src 'none' blocks JS execution regardless of
        //     load mode; style-src 'unsafe-inline' keeps inline SVG presentation
        //     attributes working.
        //   • X-Content-Type-Options: nosniff prevents content-type sniffing from
        //     reinterpreting the response as HTML/JS.
        http.Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
        http.Response.Headers.XContentTypeOptions = "nosniff";

        if (ifNoneMatch is not null && ifNoneMatch == data.ContentHash)
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        return Results.File(data.Bytes, data.MimeType);
    }

    /// <summary>
    /// RFC 7807 404 envelope shared by every Organization profile endpoint that
    /// resolves the current tenant's Organization row. RLS hides cross-tenant
    /// rows so "row absent" and "row exists but not visible" both surface as
    /// the same 404 (intentional, ADR-0090). Mirrors the private
    /// <c>TeamEndpointDelegates.TeamNotFound</c> helper and the
    /// <c>EndpointResultExtensions.ApplicationNotFound</c> helper in Catalog.
    /// </summary>
    private static IResult OrgNotFound() => Results.Problem(
        type: ProblemTypes.ResourceNotFound,
        title: "Organization not found",
        detail: "The current tenant has no visible Organization row.",
        statusCode: StatusCodes.Status404NotFound);
}

/// <summary>
/// Route composition for the Organization profile surface (`/me`, `/me/logo`,
/// `/me/permissions`, `/me/admin-only`). Extracted from
/// <c>OrganizationModule.MapEndpoints</c> in slice-9 carry-forward S6 so each
/// resource's route registration colocates with its endpoint delegates (the
/// H5 R2 split that introduced one delegate file per resource). Behavior is
/// identical to the inline registrations — the only change is composition.
/// </summary>
internal static class OrganizationProfileRoutes
{
    public static void MapTo(Microsoft.AspNetCore.Routing.RouteGroupBuilder tenant)
    {
        tenant.MapGet("/me", OrganizationProfileEndpointDelegates.GetMeAsync)
            .RequireAuthorization(KartovaPermissions.OrgProfileRead)
            .WithName("GetOrganizationMe")
            .Produces<OrgProfileResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
        tenant.MapPut("/me", OrganizationProfileEndpointDelegates.UpdateMeAsync)
            .RequireAuthorization(KartovaPermissions.OrgProfileEdit)
            .WithName("UpdateOrganizationMe")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        // Logo upload / clear / serve — slice 9 spec §6.4. The upload endpoint
        // accepts raw image/png|jpeg|svg+xml bodies (NOT multipart/form-data)
        // and is capped server-side at 256 KiB to match the OrgLogo invariant.
        tenant.MapPut("/me/logo", OrganizationProfileEndpointDelegates.UploadLogoAsync)
            .RequireAuthorization(KartovaPermissions.OrgProfileEdit)
            .WithName("UploadOrganizationLogo")
            .Produces<UploadLogoResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        tenant.MapDelete("/me/logo", OrganizationProfileEndpointDelegates.DeleteLogoAsync)
            .RequireAuthorization(KartovaPermissions.OrgProfileEdit)
            .WithName("DeleteOrganizationLogo")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);
        tenant.MapGet("/me/logo", OrganizationProfileEndpointDelegates.GetLogoAsync)
            .RequireAuthorization(KartovaPermissions.OrgProfileRead)
            .WithName("GetOrganizationLogo")
            // 200 streams the raw bytes — content type comes from the stored MIME.
            // 304 is the cache-revalidation path (If-None-Match match).
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status404NotFound);
        tenant.MapGet("/me/permissions", OrganizationProfileEndpointDelegates.GetMePermissions)
            .WithName("GetMePermissions")
            .Produces<MePermissionsResponse>(StatusCodes.Status200OK);
        tenant.MapGet("/me/admin-only", OrganizationProfileEndpointDelegates.GetAdminOnlyAsync)
            .RequireAuthorization(p => p.RequireRole(KartovaRoles.OrgAdmin))
            .WithName("GetOrganizationMeAdminOnly")
            .Produces<AdminOnlyResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }
}
