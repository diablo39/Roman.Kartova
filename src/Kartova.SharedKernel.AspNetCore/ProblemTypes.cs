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
}
