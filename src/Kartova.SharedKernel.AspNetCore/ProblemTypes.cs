namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Canonical problem-type URI slugs per ADR-0091.
/// URIs resolve to docs pages at https://kartova.io/problems/&lt;slug&gt; (published in a later phase).
/// </summary>
public static class ProblemTypes
{
    private const string Base = "https://kartova.io/problems/";

    public const string InvalidToken           = Base + "invalid-token";
    public const string MissingTenantClaim     = Base + "missing-tenant-claim";
    public const string Forbidden              = Base + "forbidden";
    public const string ResourceNotFound       = Base + "resource-not-found";
    public const string ServiceUnavailable     = Base + "service-unavailable";
    public const string InternalServerError    = Base + "internal-server-error";
    public const string TenantScopeRequired    = Base + "tenant-scope-required";
    public const string ValidationFailed       = Base + "validation-failed";

    // Pagination / sorting — ADR-0095.
    public const string InvalidSortField       = Base + "invalid-sort-field";
    public const string InvalidSortOrder       = Base + "invalid-sort-order";
    public const string InvalidCursor          = Base + "invalid-cursor";
    public const string InvalidLimit           = Base + "invalid-limit";
    public const string CursorFilterMismatch   = Base + "cursor-filter-mismatch";

    // Optimistic concurrency / preconditions — slice 5 (ADR-0096 + spec §7).
    public const string ConcurrencyConflict    = Base + "concurrency-conflict";
    public const string PreconditionRequired   = Base + "precondition-required";

    // Lifecycle transitions — ADR-0073, slice 5.
    public const string LifecycleConflict      = Base + "lifecycle-conflict";

    // Team management — slice 8 (ADR-0098 spec §6.5).
    public const string TeamHasApplications    = Base + "team-has-applications";
    public const string InvalidTeam            = Base + "invalid-team";

    // Catalog ?ownerUserId= filter — slice 9 / E2 (spec §6.5).
    public const string InvalidOwner           = Base + "invalid-owner";

    // Logo upload validation — slice 9 (spec §6.4). One URI per failure mode
    // so SPA / API consumers can dispatch on `type` instead of HTTP status.
    public const string UnsupportedLogoMedia   = Base + "unsupported-logo-media";   // 415: Content-Type not in allow-list.
    public const string LogoTooLarge           = Base + "logo-too-large";           // 413: streamed bytes exceeded LogoMaxBytes.
    public const string LogoInvalidContent     = Base + "logo-invalid-content";     // 422: SVG-script strip, magic-byte mismatch, etc.

    // Invitation lifecycle conflicts — slice 9 (spec §6.7).
    public const string EmailAlreadyInTenant   = Base + "email-already-in-tenant";
    public const string EmailAlreadyInvited    = Base + "email-already-invited";
    public const string EmailAlreadyOnPlatform = Base + "email-already-on-platform";
    public const string InvitationNotPending   = Base + "invitation-not-pending";

    // Invitation accept — slice 9 (spec §6.8).
    public const string InvitationGone         = Base + "invitation-gone";         // 410: expired, revoked, or already accepted.

    // Member lifecycle — slice 10.
    public const string LastOrgAdmin       = Base + "last-orgadmin";          // 409
    public const string CannotOffboardSelf = Base + "cannot-offboard-self";   // 409
    public const string InvalidSuccessor   = Base + "invalid-successor";      // 422
}
