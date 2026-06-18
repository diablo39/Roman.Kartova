namespace Kartova.Audit.Domain;

/// <summary>
/// Who performed an audited action. <see cref="User"/> is written for authenticated HTTP requests.
/// <see cref="System"/> is written by background sweeps with no HTTP principal (e.g. the
/// invitation-expiry sweep). <see cref="ServiceAccount"/> (ADR-0009) is reserved for a future
/// service-account caller.
/// </summary>
public enum AuditActorType
{
    User = 1,
    System = 2,
    ServiceAccount = 3,
}
