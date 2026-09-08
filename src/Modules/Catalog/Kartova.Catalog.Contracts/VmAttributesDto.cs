using System.Diagnostics.CodeAnalysis;

namespace Kartova.Catalog.Contracts;

[ExcludeFromCodeCoverage]
public sealed record VmAttributesDto(
    string PowerState,
    string Os,
    int Vcpu,
    int MemoryGb,
    string Hostname,
    IReadOnlyList<string> IpAddresses,
    string Region);
