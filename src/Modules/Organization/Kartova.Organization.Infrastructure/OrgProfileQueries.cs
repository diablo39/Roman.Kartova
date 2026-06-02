using Kartova.Organization.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Reads the current tenant's <c>Organization</c> profile (slice-9 spec §4).
/// Lives in Infrastructure (not Application) because it depends on
/// <see cref="OrganizationDbContext"/>; the namespace stays
/// <c>Kartova.Organization.Infrastructure</c> to follow the slice-8 handler
/// convention (folder == namespace). RLS guarantees at-most-one visible row
/// for the current tenant — projection is composed inline to avoid pulling
/// EF tracking metadata into the read path.
/// </summary>
public sealed class OrgProfileQueries
{
    private readonly OrganizationDbContext _db;

    public OrgProfileQueries(OrganizationDbContext db)
    {
        _db = db;
    }

    public async Task<OrgProfileResponse?> GetMyOrgAsync(CancellationToken ct)
    {
        var row = await _db.Organizations
            .AsNoTracking()
            .Select(o => new
            {
                Id = o.Id.Value,
                o.DisplayName,
                o.Description,
                o.DefaultTimeZone,
                LogoEtag = o.Logo != null ? o.Logo.ContentHash : null,
                LogoMimeType = o.Logo != null ? o.Logo.MimeType : null,
                o.CreatedAt,
            })
            .FirstOrDefaultAsync(ct);
        if (row is null) return null;
        return new OrgProfileResponse(
            row.Id,
            row.DisplayName,
            row.Description,
            row.DefaultTimeZone,
            row.LogoEtag,
            row.LogoMimeType,
            row.CreatedAt);
    }
}
