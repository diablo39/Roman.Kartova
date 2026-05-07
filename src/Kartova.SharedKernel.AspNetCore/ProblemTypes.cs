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

    // Optimistic concurrency / preconditions — slice 5 (ADR-0096 + spec §7).
    public const string ConcurrencyConflict    = Base + "concurrency-conflict";
    public const string PreconditionRequired   = Base + "precondition-required";

    // Lifecycle transitions — ADR-0073, slice 5.
    public const string LifecycleConflict      = Base + "lifecycle-conflict";
}
