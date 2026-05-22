using System.Diagnostics.CodeAnalysis;

namespace Kartova.Catalog.Contracts;

[ExcludeFromCodeCoverage]
public sealed record UnDecommissionApplicationRequest(DateTimeOffset SunsetDate);
