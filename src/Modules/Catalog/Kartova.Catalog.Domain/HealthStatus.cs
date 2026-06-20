namespace Kartova.Catalog.Domain;

/// <summary>Operational health of a service. Defaults to <c>Unknown</c>; the
/// write path (probe/agent ingestion) lands in a later phase (E-15/E-16).</summary>
public enum HealthStatus
{
    Unknown,
    Healthy,
    Degraded,
    Unhealthy,
}
