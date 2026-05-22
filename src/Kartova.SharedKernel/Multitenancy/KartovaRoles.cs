namespace Kartova.SharedKernel.Multitenancy;

public static class KartovaRoles
{
    public const string PlatformAdmin = "platform-admin";
    public const string OrgAdmin = "OrgAdmin";
    public const string TeamAdmin = "TeamAdmin";
    public const string Member = "Member";
    public const string Viewer = "Viewer";
    public const string ServiceAccount = "ServiceAccount"; // forward-compat — no realm role yet (ADR-0009)
}
