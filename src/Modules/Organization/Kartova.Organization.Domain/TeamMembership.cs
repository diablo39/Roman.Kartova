namespace Kartova.Organization.Domain;

public sealed class TeamMembership
{
    public TeamId TeamId { get; private set; }
    public Guid UserId { get; private set; }
    public TeamRole Role { get; private set; }
    public DateTimeOffset AddedAt { get; private set; }

    private TeamMembership() { /* EF */ }

    public static TeamMembership Create(TeamId teamId, Guid userId, TeamRole role, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (userId == Guid.Empty)
            throw new ArgumentException("userId required", nameof(userId));
        return new TeamMembership
        {
            TeamId = teamId,
            UserId = userId,
            Role = role,
            AddedAt = clock.GetUtcNow(),
        };
    }

    public void ChangeRole(TeamRole newRole) => Role = newRole;
}
