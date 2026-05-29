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

        // Two server-side queries, joined client-side. Why not a single
        // server-side .Join()? The original implementation joined on
        // `m.TeamId.Value` (outer) → `EF.Property<Guid>(t, "_id")` (inner)
        // and Npgsql refused to translate the outer key selector: the
        // TeamId value object's `.Value` getter is fine in a plain Select
        // (see OrganizationTeamMembershipReader) but is NOT translatable as
        // a Join key. That surfaced as a 500 on `GET /users/{id}` in the
        // H4 verification.
        //
        // Membership cardinality on a single user is small (a handful of
        // teams in practice; spec §6.7's user-detail view is unbounded only
        // in the contract sense), so two round trips with a client-side
        // dictionary lookup is cheap and works on both providers (Npgsql in
        // production, InMemory in UserQueriesTests).
        var memberships = await _db.TeamMembers
            .AsNoTracking()
            .Where(m => m.UserId == id)
            .Select(m => new { TeamGuid = m.TeamId.Value, m.Role })
            .ToListAsync(ct);

        IReadOnlyList<UserTeamMembership> teams;
        if (memberships.Count == 0)
        {
            teams = [];
        }
        else
        {
            // Fetch matching teams by primary key. Team.Id is computed
            // (returns `new TeamId(_id)`) and explicitly Ignore'd in the EF
            // model — only the private `_id` Guid backing field is mapped,
            // accessed here via EF.Property exactly as TeamSortSpecs /
            // DeleteTeamHandler / UpdateTeamHandler already do.
            var teamIds = memberships.Select(m => m.TeamGuid).ToHashSet();
            var teamRows = await _db.Teams
                .AsNoTracking()
                .Where(t => teamIds.Contains(EF.Property<Guid>(t, "_id")))
                .Select(t => new { Id = EF.Property<Guid>(t, "_id"), t.DisplayName })
                .ToListAsync(ct);
            var teamById = teamRows.ToDictionary(t => t.Id, t => t.DisplayName);

            teams = memberships
                .Where(m => teamById.ContainsKey(m.TeamGuid))
                .Select(m => new UserTeamMembership(
                    m.TeamGuid,
                    teamById[m.TeamGuid],
                    m.Role.ToString()))
                .ToList();
        }

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
