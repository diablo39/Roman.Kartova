namespace Kartova.Catalog.Domain;

/// <summary>Allowed serializations for a stored API spec document (ADR-0112). The semantic
/// format (OpenAPI vs AsyncAPI) derives from <see cref="Api.Style"/>; this only constrains the
/// wire serialization we accept and echo back. XML/WSDL is a planned future member.</summary>
public static class ApiMediaType
{
    public const string ApplicationJson = "application/json";
    public const string ApplicationYaml = "application/yaml";

    public static bool IsAllowed(string? mediaType)
        => mediaType is ApplicationJson or ApplicationYaml;
}
