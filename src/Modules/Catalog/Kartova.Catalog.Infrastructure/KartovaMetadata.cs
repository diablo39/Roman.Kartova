namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Technical table proving the migration pipeline works end-to-end.
/// One row per module; schema_version incremented when that module's
/// migration set changes. Read/written only by the migrator — not by the API.
/// </summary>
internal sealed class KartovaMetadata
{
    public string ModuleName { get; set; } = default!;
    public int SchemaVersion { get; set; }
    public DateTimeOffset AppliedAt { get; set; }
}
