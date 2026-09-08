using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kartova.Catalog.Contracts;

namespace Kartova.Catalog.Application;

public enum VmPowerState
{
    Running,
    Stopped,
    Suspended,
}

/// <summary>Typed application-layer representation of the opaque jsonb payload stored on
/// <c>InfrastructureResource.Attributes</c> for VM-kind resources. Validates
/// <see cref="VmAttributesDto"/> input and round-trips to/from JSON with camelCase keys —
/// the exact key literals (<c>powerState</c>, <c>os</c>, <c>vcpu</c>, <c>memoryGb</c>,
/// <c>hostname</c>, <c>ipAddresses</c>, <c>region</c>) are matched by the list handler's
/// <c>@&gt;</c> jsonb containment filter.</summary>
public sealed record VmAttributes(
    VmPowerState PowerState,
    string Os,
    int Vcpu,
    int MemoryGb,
    string Hostname,
    IReadOnlyList<string> IpAddresses,
    string Region)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    // Mirrors the FE zod caps in web/src/features/catalog/schemas/registerVm.ts
    // (registerVmSchema/vmAttributesSchema) so the server rejects the same oversized
    // input the client would already have blocked, rather than silently accepting
    // whatever a non-SPA caller sends.
    private const int MaxOsLength = 128;
    private const int MaxHostnameLength = 255;
    private const int MaxRegionLength = 128;

    // The FE's InputTags for ipAddresses has no configured maxTags (unbounded on the
    // client) — there is no FE number to mirror here. This cap is a server-side-only
    // defense against an unbounded jsonb array; 32 comfortably covers realistic
    // multi-NIC VMs while keeping the stored attributes payload bounded.
    private const int MaxIpAddresses = 32;

    public static VmAttributes Validate(VmAttributesDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (!Enum.TryParse<VmPowerState>(dto.PowerState, ignoreCase: true, out var powerState))
        {
            throw new ArgumentException($"'{dto.PowerState}' is not a valid power state.", nameof(dto));
        }

        if (string.IsNullOrWhiteSpace(dto.Os))
        {
            throw new ArgumentException("Os must not be empty.", nameof(dto));
        }

        if (dto.Os.Length > MaxOsLength)
        {
            throw new ArgumentException($"Os must be at most {MaxOsLength} characters.", nameof(dto));
        }

        if (string.IsNullOrWhiteSpace(dto.Hostname))
        {
            throw new ArgumentException("Hostname must not be empty.", nameof(dto));
        }

        if (dto.Hostname.Length > MaxHostnameLength)
        {
            throw new ArgumentException($"Hostname must be at most {MaxHostnameLength} characters.", nameof(dto));
        }

        if (string.IsNullOrWhiteSpace(dto.Region))
        {
            throw new ArgumentException("Region must not be empty.", nameof(dto));
        }

        if (dto.Region.Length > MaxRegionLength)
        {
            throw new ArgumentException($"Region must be at most {MaxRegionLength} characters.", nameof(dto));
        }

        if (dto.Vcpu <= 0)
        {
            throw new ArgumentException("Vcpu must be greater than zero.", nameof(dto));
        }

        if (dto.MemoryGb <= 0)
        {
            throw new ArgumentException("MemoryGb must be greater than zero.", nameof(dto));
        }

        if (dto.IpAddresses is null || dto.IpAddresses.Count == 0)
        {
            throw new ArgumentException("IpAddresses must not be empty.", nameof(dto));
        }

        if (dto.IpAddresses.Count > MaxIpAddresses)
        {
            throw new ArgumentException($"At most {MaxIpAddresses} IP addresses may be supplied.", nameof(dto));
        }

        foreach (var ip in dto.IpAddresses)
        {
            if (!IPAddress.TryParse(ip, out _))
            {
                throw new ArgumentException($"'{ip}' is not a valid IP address.", nameof(dto));
            }
        }

        return new VmAttributes(powerState, dto.Os, dto.Vcpu, dto.MemoryGb, dto.Hostname, dto.IpAddresses, dto.Region);
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static VmAttributes FromJson(string json) =>
        JsonSerializer.Deserialize<VmAttributes>(json, JsonOptions)
        ?? throw new ArgumentException("VM attributes JSON deserialized to null.", nameof(json));

    // Serializes PowerState through the same JsonOptions (and therefore the same
    // JsonStringEnumConverter(JsonNamingPolicy.CamelCase)) that ToJson uses, so the DTO's
    // powerState value is guaranteed to equal ToJson's — rather than a hand-rolled
    // ToLowerInvariant() that would silently diverge for any future multi-word member.
    private static string PowerStateWireValue(VmPowerState state) =>
        JsonSerializer.Serialize(state, JsonOptions).Trim('"');

    public VmAttributesDto ToDto() => new(
        PowerStateWireValue(PowerState),
        Os,
        Vcpu,
        MemoryGb,
        Hostname,
        IpAddresses,
        Region);

    // Records default to reference equality on IReadOnlyList<string> (List<T>/arrays don't
    // override Equals), which fails ToJson/FromJson round-trips where the deserialized
    // IpAddresses is a different list instance holding the same elements — so equality is
    // overridden here to compare IpAddresses element-by-element.
    public bool Equals(VmAttributes? other) =>
        other is not null
        && PowerState == other.PowerState
        && Os == other.Os
        && Vcpu == other.Vcpu
        && MemoryGb == other.MemoryGb
        && Hostname == other.Hostname
        && Region == other.Region
        && IpAddresses.SequenceEqual(other.IpAddresses);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(PowerState);
        hash.Add(Os);
        hash.Add(Vcpu);
        hash.Add(MemoryGb);
        hash.Add(Hostname);
        hash.Add(Region);
        foreach (var ip in IpAddresses)
        {
            hash.Add(ip);
        }

        return hash.ToHashCode();
    }
}
