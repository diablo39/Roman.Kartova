using System.Diagnostics.CodeAnalysis;

namespace Kartova.Organization.Contracts;

/// <summary>
/// Wire-shape for a single team member.
/// <para>
/// Slice 9 / E3 (ADR-0098): <see cref="DisplayName"/> + <see cref="Email"/> are
/// enriched from the <c>users</c> projection via <c>IUserDirectory</c>. When a
/// matching user row does not exist in the current tenant (e.g., the user was
/// hard-deleted after the membership was created, or the projection has not yet
/// caught up with KeyCloak), both fields surface as the empty string. This
/// matches the spec §6.6 non-nullable-string contract and gives the SPA a single
/// "missing display info" signal it can render as the user's id.
/// </para>
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record TeamMemberResponse(
    Guid UserId,
    string Role,
    DateTimeOffset AddedAt,
    string DisplayName,
    string Email);
