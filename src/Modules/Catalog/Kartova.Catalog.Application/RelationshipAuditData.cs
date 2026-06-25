using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Application;

/// <summary>
/// Audit <c>data</c> payload shared by the relationship create/remove handlers so the
/// two never drift in their key set.
/// </summary>
public static class RelationshipAuditData
{
    public static Dictionary<string, string?> For(EntityRef source, EntityRef target, RelationshipType type) => new()
    {
        ["sourceKind"] = source.Kind.ToString(),
        ["sourceId"]   = source.Id.ToString(),
        ["type"]       = type.ToString(),
        ["targetKind"] = target.Kind.ToString(),
        ["targetId"]   = target.Id.ToString(),
    };
}
