namespace Kartova.Catalog.Domain;

/// <summary>Infrastructure resource kind (ADR-0111 amendment). One unified Infrastructure
/// entity keyed by this value; type-variant attributes live in the stored jsonb payload,
/// not in columns. Append at end only — the persisted smallint values must stay stable.</summary>
public enum InfrastructureType
{
    VirtualMachine,   // = 0
}
