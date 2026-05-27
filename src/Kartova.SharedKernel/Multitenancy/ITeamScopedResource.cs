namespace Kartova.SharedKernel.Multitenancy;

/// <summary>
/// Marker for resources that may optionally belong to a team. A null TeamId
/// means the resource is tenant-scoped but unassigned.
/// </summary>
public interface ITeamScopedResource
{
    Guid? TeamId { get; }
}
