using Kartova.Organization.Contracts;
using Kartova.SharedKernel.Pagination;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Slice-9 spec §6.7: typeahead user search (<c>GET /users?q=...</c>) plus
/// user detail with team memberships (<c>GET /users/{id}</c>). Lives in
/// Infrastructure (not Application) because both queries depend on
/// <see cref="OrganizationDbContext"/> — same placement as
/// <see cref="OrgProfileQueries"/>. RLS filters cross-tenant rows out of the
/// underlying tables, so a result that comes back is guaranteed visible to
/// the current tenant.
/// </summary>
/// <remarks>
/// Search uses <c>string.ToLower().Contains(...)</c> rather than
/// <c>EF.Functions.ILike</c> so both the Postgres provider (which translates
/// to <c>LOWER(...) LIKE</c>) and the InMemory provider used by unit tests
/// execute the predicate. <c>ILike</c> is Npgsql-only and throws on InMemory.
/// </remarks>
[BoundedListResult(
    "Typeahead search cap is 20 results (Math.Clamp at SearchAsync); not user-controlled paging.")]
public sealed class UserQueries
{
    private readonly OrganizationDbContext _db;

    public UserQueries(OrganizationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<UserSummaryResponse>> SearchAsync(
        string q, int limit, CancellationToken ct)
    {
        // Endpoint validates `q` (RFC 7807 422) before reaching us; this guards
        // direct callers / future internal consumers from accidentally probing
        // the index with a single character that would scan the entire users
        // table.
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
        {
            throw new ArgumentException("Query must be at least 2 chars.", nameof(q));
        }

        var clipped = Math.Clamp(limit, 1, 20);
        var lowered = q.ToLowerInvariant();

        return await _db.Users
            .AsNoTracking()
            .Where(u => u.DisplayName.ToLower().Contains(lowered)
                     || u.Email.ToLower().Contains(lowered))
            .OrderBy(u => u.DisplayName)
            .Take(clipped)
            .Select(u => new UserSummaryResponse(u.Id, u.DisplayName, u.Email))
            .ToListAsync(ct);
    }

    public async Task<UserDetailResponse?> GetDetailAsync(Guid id, CancellationToken ct)
    {
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return null;

        // Join team_members → teams to project TeamName alongside the role.
        //
        // Plan suggested joining on the TeamId value-object directly, but
        // Team.Id is computed (returns new TeamId(_id)) and explicitly Ignore'd
        // in the EF model — only the private `_id` Guid backing field is
        // mapped. EF Core therefore cannot translate `t => t.Id` to SQL nor
        // evaluate it on the InMemory provider. The same pattern shows up in
        // TeamSortSpecs / DeleteTeamHandler / UpdateTeamHandler — they all
        // reach the PK via `EF.Property<Guid>(t, "_id")`. Doing the same here
        // also lets us read the Guid into the response directly without an
        // extra `.Value` accessor on a value object EF doesn't know how to
        // project.
        var teams = await _db.TeamMembers
            .AsNoTracking()
            .Where(m => m.UserId == id)
            .Join(_db.Teams,
                m => m.TeamId.Value,
                t => EF.Property<Guid>(t, "_id"),
                (m, t) => new UserTeamMembership(
                    EF.Property<Guid>(t, "_id"),
                    t.DisplayName,
                    m.Role.ToString()))
            .ToListAsync(ct);

        return new UserDetailResponse(
            user.Id,
            user.Email,
            user.DisplayName,
            user.GivenName,
            user.FamilyName,
            teams,
            user.CreatedAt,
            user.LastSeenAt);
    }
}
