namespace Kartova.SharedKernel.Multitenancy;

/// <summary>
/// Marker for resources that always belong to a team (non-nullable TeamId).
/// </summary>
public interface ITeamOwnedResource
{
    Guid TeamId { get; }
}
