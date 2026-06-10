using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Wolverine handler for <see cref="RegisterApplicationCommand"/>. Lives in
/// Infrastructure (not Application) because it depends on
/// <see cref="CatalogDbContext"/> — and Infrastructure already references
/// Application, so placing the handler here avoids a project cycle.
///
/// The Catalog module's Wolverine discovery (CatalogModule.ConfigureWolverine)
/// already includes <c>typeof(CatalogModule).Assembly</c>, so this handler is
/// picked up automatically.
///
/// Tenant id and created-by user id are sourced from <see cref="ITenantScope"/> and
/// <see cref="ICurrentUser"/> respectively (ADR-0090) — not from the payload. The
/// owning team id (<c>cmd.TeamId</c>, required per ADR-0103) does come from the
/// payload; the delegate validates it exists in the tenant before dispatching here.
/// </summary>
public sealed class RegisterApplicationHandler
{
    private readonly TimeProvider _clock;

    public RegisterApplicationHandler(TimeProvider clock) => _clock = clock;

    public async Task<ApplicationResponse> Handle(
        RegisterApplicationCommand cmd,
        CatalogDbContext db,
        ITenantContext tenant,
        ICurrentUser user,
        CancellationToken ct)
    {
        var app = Kartova.Catalog.Domain.Application.Create(
            cmd.DisplayName, cmd.Description, user.UserId, cmd.TeamId, tenant.Id, _clock);
        db.Applications.Add(app);
        await db.SaveChangesAsync(ct);
        return app.ToResponse();
    }
}
