using System.Diagnostics.CodeAnalysis;

namespace Kartova.Organization.Contracts;

[ExcludeFromCodeCoverage]
public sealed record UpdateMemberRoleRequest(string Role);
