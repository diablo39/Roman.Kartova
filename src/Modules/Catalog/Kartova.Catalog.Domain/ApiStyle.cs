namespace Kartova.Catalog.Domain;

/// <summary>API style (ADR-0111, amended 2026-07-07). One unified API entity keyed by this
/// value; async ("AsyncApi") is a style, not a separate entity — its protocol/channels live in
/// the stored AsyncAPI document (ADR-0112), not in columns. WSDL is a planned future member.</summary>
public enum ApiStyle
{
    Rest,
    Grpc,
    GraphQL,
    AsyncApi,   // append at end — keeps existing smallint values + byStyle sort stable
}
