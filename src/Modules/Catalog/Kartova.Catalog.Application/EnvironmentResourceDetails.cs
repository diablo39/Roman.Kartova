using System.Text.Json;

namespace Kartova.Catalog.Application;

/// <summary>
/// App-layer wrapper for an environment's opaque "resource details" jsonb map
/// (E-02.F-05.S-01). Validates key/value length caps and canonicalizes null → empty,
/// then (de)serializes camelCase JSON. Mirrors the VmAttributes validate/ToJson/FromJson
/// pattern, minus a fixed schema — the map is free-form key/value strings.
/// </summary>
public sealed class EnvironmentResourceDetails
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IReadOnlyDictionary<string, string> _map;

    private EnvironmentResourceDetails(IReadOnlyDictionary<string, string> map) => _map = map;

    public static EnvironmentResourceDetails Validate(IReadOnlyDictionary<string, string>? dto)
    {
        var map = dto ?? new Dictionary<string, string>();
        foreach (var (k, v) in map)
        {
            if (string.IsNullOrWhiteSpace(k) || k.Length > 128)
                throw new ArgumentException($"Resource-detail key '{k}' must be 1..128 non-blank characters.", nameof(dto));
            if (v is null || v.Length > 1024)
                throw new ArgumentException($"Resource-detail value for '{k}' must be <= 1024 characters.", nameof(dto));
        }
        return new EnvironmentResourceDetails(map);
    }

    public string ToJson() => JsonSerializer.Serialize(_map, JsonOptions);

    public static IReadOnlyDictionary<string, string> FromJson(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions) ?? new Dictionary<string, string>();
}
