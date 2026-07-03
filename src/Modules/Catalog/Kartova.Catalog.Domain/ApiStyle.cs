namespace Kartova.Catalog.Domain;

/// <summary>Synchronous API style (ADR-0111). Async styles are a separate entity (E-02.F-03.S-02).</summary>
public enum ApiStyle
{
    Rest,
    Grpc,
    GraphQL,
}
