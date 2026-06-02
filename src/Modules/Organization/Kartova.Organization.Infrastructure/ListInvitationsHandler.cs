using Kartova.Organization.Application;
using Kartova.Organization.Contracts;
using Kartova.Organization.Domain;
using Kartova.SharedKernel.Pagination;
using Kartova.SharedKernel.Postgres.Pagination;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Wolverine-style handler for <see cref="ListInvitationsQuery"/>. RLS
/// auto-filters cross-tenant rows so the result set is implicitly scoped to
/// the current tenant (ADR-0090). Pagination applied via
/// <see cref="QueryablePagingExtensions.ToCursorPagedAsync{T}"/> (ADR-0095).
/// </summary>
public sealed class ListInvitationsHandler
{
    // EF.Property is not invokable outside an EF query context, so the
    // cursor-encoder uses an in-memory extractor (x.Id.Value via the strong-
    // typed domain property). Mirrors the split used by ListTeamsHandler.
    private static readonly Func<Invitation, Guid> IdExtractor = x => x.Id.Value;

    public async Task<CursorPage<InvitationResponse>> Handle(
        ListInvitationsQuery q,
        OrganizationDbContext db,
        CancellationToken ct)
    {
        var spec = InvitationSortSpecs.Resolve(q.SortBy);

        IQueryable<Invitation> source = db.Invitations;
        if (q.StatusFilter is { } statusFilter)
        {
            source = source.Where(i => i.Status == statusFilter);
        }

        var page = await source.ToCursorPagedAsync(
            spec, q.SortOrder, q.Cursor, q.Limit,
            InvitationSortSpecs.IdSelector, IdExtractor, ct);

        var items = page.Items
            .Select(i => new InvitationResponse(
                i.Id.Value, i.Email, i.Role, i.InvitedAt, i.ExpiresAt,
                i.Status.ToString(), i.InvitedByUserId, i.AcceptedAt, i.RevokedAt))
            .ToList();

        return new CursorPage<InvitationResponse>(items, page.NextCursor, page.PrevCursor);
    }
}
