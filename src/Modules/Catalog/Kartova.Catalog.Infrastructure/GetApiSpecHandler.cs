using Kartova.Catalog.Application;
using Kartova.Catalog.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Handler for <see cref="GetApiSpecQuery"/>. RLS scopes visibility; returns null when
/// no spec is stored (or the API is invisible cross-tenant).</summary>
public sealed class GetApiSpecHandler
{
    public async Task<(string Content, string MediaType)?> Handle(
        GetApiSpecQuery q, CatalogDbContext db, CancellationToken ct)
    {
        var row = await db.ApiSpecs
            .Where(s => s.ApiId == new ApiId(q.ApiId))
            .Select(s => new { s.Content, s.MediaType })
            .FirstOrDefaultAsync(ct);
        return row is null ? null : (row.Content, row.MediaType);
    }
}
