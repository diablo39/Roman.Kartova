using Kartova.Organization.Application;
using Kartova.Organization.Contracts;
using Kartova.Organization.Domain;
using Kartova.SharedKernel.Audit;
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Wolverine handler for <see cref="CreateTeamCommand"/>. Lives in
/// Infrastructure (not Application) because it depends on
/// <see cref="OrganizationDbContext"/> — and Infrastructure already references
/// Application, so placing the handler here avoids a project cycle.
///
/// The Organization module's Wolverine discovery (OrganizationModule.ConfigureWolverine)
/// already includes <c>typeof(OrganizationModule).Assembly</c>, so this handler is
/// picked up automatically.
///
/// Tenant id is sourced from <see cref="ITenantContext"/> (ADR-0090) — not from
/// the payload.
/// </summary>
public sealed class CreateTeamHandler
{
    private readonly TimeProvider _clock;
    private readonly ITenantContext _tenant;
    private readonly IAuditWriter _audit;

    public CreateTeamHandler(TimeProvider clock, ITenantContext tenant, IAuditWriter audit)
    {
        _clock = clock;
        _tenant = tenant;
        _audit = audit;
    }

    public async Task<TeamResponse> Handle(
        CreateTeamCommand cmd,
        OrganizationDbContext db,
        CancellationToken ct)
    {
        var team = Team.Create(cmd.DisplayName, cmd.Description, _tenant.Id, _clock);
        db.Teams.Add(team);
        await db.SaveChangesAsync(ct);
        await _audit.AppendAsync(new AuditEntry(
            OrganizationAuditActions.TeamCreated, AuditTargetTypes.Team, team.Id.Value.ToString(),
            new Dictionary<string, string?> { ["displayName"] = team.DisplayName }), ct);
        return new TeamResponse(team.Id.Value, team.DisplayName, team.Description, team.CreatedAt);
    }
}
