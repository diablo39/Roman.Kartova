namespace Kartova.SharedKernel.Multitenancy;

public interface ITenantContext
{
    TenantId Id { get; }
    bool IsTenantScoped { get; }
    IReadOnlyCollection<string> Roles { get; }
    IReadOnlyList<TeamMembershipInfo> TeamMemberships { get; }
    IReadOnlySet<Guid> TeamIds { get; }
    Guid? JustAcceptedInvitationId { get; }

    void Populate(TenantId id, IReadOnlyCollection<string> roles);
    void PopulateTeamMemberships(IReadOnlyList<TeamMembershipInfo> memberships);
    void SetJustAcceptedInvitation(Guid invitationId);
    void Clear();
}
