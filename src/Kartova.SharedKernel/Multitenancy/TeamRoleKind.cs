namespace Kartova.SharedKernel.Multitenancy;

/// <summary>
/// Cross-module role identity used by team-scoped authorization. Mirrors the
/// Organization module's TeamRole enum without leaking that domain type.
/// </summary>
public enum TeamRoleKind : byte
{
    Member = 1,
    Admin = 2,
}
