using System.Diagnostics.CodeAnalysis;

namespace Kartova.SharedKernel;

[ExcludeFromCodeCoverage]
public sealed record UserDisplayInfo(Guid Id, string DisplayName, string Email);
