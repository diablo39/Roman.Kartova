namespace Kartova.Catalog.Contracts;

/// <summary>How an API appears on a component's surface: a direct edge, or derived via instance-of (ADR-0111).</summary>
public enum ApiSurfaceOrigin
{
    Direct,
    Derived,
}
