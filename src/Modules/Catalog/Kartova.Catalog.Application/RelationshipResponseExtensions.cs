using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Application;

public static class RelationshipResponseExtensions
{
    public static RelationshipResponse ToResponse(this Relationship r, EntityRefDto source, EntityRefDto target)
        => new(r.Id.Value, source, target, r.Type, r.Origin, r.CreatedByUserId, r.CreatedAt);
}
