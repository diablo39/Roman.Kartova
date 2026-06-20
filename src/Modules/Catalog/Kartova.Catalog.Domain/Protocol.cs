namespace Kartova.Catalog.Domain;

/// <summary>Transport/interface style of a service endpoint. Closed vocabulary;
/// <c>Other</c> is the escape hatch so the enum need not churn (mirrors the
/// fixed-vocabulary stance of ADR-0068).</summary>
public enum Protocol
{
    Rest,
    Grpc,
    GraphQL,
    WebSocket,
    Tcp,
    Other,
}
