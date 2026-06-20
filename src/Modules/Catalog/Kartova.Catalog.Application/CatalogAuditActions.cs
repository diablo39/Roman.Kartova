namespace Kartova.Catalog.Application;

/// <summary>
/// Audit action taxonomy for Catalog-module mutations (design §4). Action strings
/// are the stable contract written to <c>audit_log.action</c>; do not rename without
/// a migration of historical rows. The four lifecycle transitions share a single
/// <c>application.lifecycle_changed</c> action, distinguished by <c>from</c>/<c>to</c>
/// in the row's <c>data</c>.
/// </summary>
public static class CatalogAuditActions
{
    public const string ApplicationRegistered = "application.registered";
    public const string ApplicationEdited = "application.edited";
    public const string ApplicationLifecycleChanged = "application.lifecycle_changed";
    public const string ApplicationTeamAssigned = "application.team_assigned";
    public const string ServiceRegistered = "service.registered";
}

/// <summary>
/// Audit <c>target_type</c> literals for Catalog (design §4). Catalog-local because
/// it cannot reference Organization's <c>AuditTargetTypes</c> (ADR-0082).
/// </summary>
public static class CatalogAuditTargetTypes
{
    public const string Application = "Application";
    public const string Service = "Service";
}
