using System.Collections.Frozen;

namespace Kartova.SharedKernel.Multitenancy;

public sealed class TenantContextAccessor : ITenantContext
{
    private TenantId _id = TenantId.Empty;
    private IReadOnlyCollection<string> _roles = Array.Empty<string>();
    private bool _populated;
    private IReadOnlyList<TeamMembershipInfo> _teamMemberships = Array.Empty<TeamMembershipInfo>();
    private IReadOnlySet<Guid> _teamIds = FrozenSet<Guid>.Empty;

    public TenantId Id => _id;
    public bool IsTenantScoped => _populated && _id != TenantId.Empty;
    public IReadOnlyCollection<string> Roles => _roles;
    public IReadOnlyList<TeamMembershipInfo> TeamMemberships => _teamMemberships;
    public IReadOnlySet<Guid> TeamIds => _teamIds;
    public Guid? JustAcceptedInvitationId { get; private set; }

    public void Populate(TenantId id, IReadOnlyCollection<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        _id = id;
        _roles = roles;
        _populated = true;
    }

    public void PopulateTeamMemberships(IReadOnlyList<TeamMembershipInfo> memberships)
    {
        ArgumentNullException.ThrowIfNull(memberships);
        _teamMemberships = memberships;
        _teamIds = memberships.Select(m => m.TeamId).ToFrozenSet();
    }

    public void SetJustAcceptedInvitation(Guid invitationId) => JustAcceptedInvitationId = invitationId;

    public void Clear()
    {
        _id = TenantId.Empty;
        _roles = Array.Empty<string>();
        _populated = false;
        _teamMemberships = Array.Empty<TeamMembershipInfo>();
        _teamIds = FrozenSet<Guid>.Empty;
        JustAcceptedInvitationId = null;
    }
}
