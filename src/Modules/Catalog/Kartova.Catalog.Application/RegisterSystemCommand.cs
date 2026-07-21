namespace Kartova.Catalog.Application;

/// <summary>Register a new System (grouping node) in the current tenant. Tenant id +
/// created-by come from request context (ADR-0090); <c>TeamId</c> is the required
/// steward team (ADR-0103), validated by the delegate before dispatch.</summary>
public sealed record RegisterSystemCommand(
    string DisplayName,
    string? Description,
    Guid TeamId);
