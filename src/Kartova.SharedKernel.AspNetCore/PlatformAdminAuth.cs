namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// ADR-0118: the platform-operator auth surface. <see cref="Scheme"/> validates tokens from the
/// separate <c>kartova-platform</c> realm only; <see cref="Policy"/> binds that scheme plus the
/// <c>platform-admin</c> role and is the sole authorization on <c>/api/v1/admin/**</c>.
/// </summary>
public static class PlatformAdminAuth
{
    public const string Scheme = "PlatformAdmin";
    public const string Policy = "PlatformAdminOnly";
}
