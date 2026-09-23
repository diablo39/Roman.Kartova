using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Handler for <see cref="GetEnvironmentByIdQuery"/>. RLS scopes the result set;
/// returns <c>null</c> for an unknown or cross-tenant id.</summary>
public sealed class GetEnvironmentByIdHandler
{
    public async Task<EnvironmentDetailResponse?> Handle(
        GetEnvironmentByIdQuery q, CatalogDbContext db, CancellationToken ct)
    {
        var env = await db.Environments.SingleOrDefaultAsync(EnvironmentSortSpecs.IdEquals(q.Id), ct);
        if (env is null) return null;

        return new EnvironmentDetailResponse(
            env.Id.Value, env.TenantId.Value, env.DisplayName, env.Description, env.Type, env.Region,
            EnvironmentResourceDetails.FromJson(env.ResourceDetails), env.CreatedByUserId, env.CreatedAt,
            VersionEncoding.Encode(env.Xmin));
    }
}
