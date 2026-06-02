using System.Diagnostics.CodeAnalysis;

namespace Kartova.Organization.Domain;

/// <summary>
/// Raised by the users-projection upsert path when two distinct KeyCloak <c>sub</c>
/// claims share the same lower-cased email within a single tenant — the exact
/// invariant ADR-0100 forbids. Surfaces from the new partial UNIQUE index
/// <c>ix_users_tenant_email</c> on <c>(tenant_id, lower(email))</c> as a
/// PostgreSQL 23505. The handler logs the event at <c>Error</c> level and
/// rethrows this typed exception so ops can intervene (the realm setting
/// <c>duplicateEmailsAllowed=false</c> should make this impossible at the IdP
/// layer, so reaching this branch means a config drift or a manual data
/// import bypassed KeyCloak).
/// <para>
/// Distinct from <see cref="System.InvalidOperationException"/> /
/// <see cref="Microsoft.EntityFrameworkCore.DbUpdateException"/> so callers
/// can pattern-match on this type without parsing inner exceptions.
/// </para>
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class OneEmailPerTenantViolationException : Exception
{
    public Guid TenantId { get; }
    public string Email { get; }

    public OneEmailPerTenantViolationException(Guid tenantId, string email, Exception inner)
        : base(BuildMessage(tenantId, email), inner)
    {
        TenantId = tenantId;
        Email = email;
    }

    private static string BuildMessage(Guid tenantId, string email) =>
        $"ADR-0100 one-email-per-tenant invariant violated for tenant {tenantId}, email '{email}'. "
        + "Two distinct KeyCloak users now share this email in the same tenant — investigate the realm "
        + "configuration (duplicateEmailsAllowed must remain false) and any out-of-band user imports.";
}
