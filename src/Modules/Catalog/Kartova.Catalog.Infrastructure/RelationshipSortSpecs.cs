using System.Linq.Expressions;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Pagination;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

internal static class RelationshipSortSpecs
{
    public static readonly Expression<Func<Relationship, Guid>> IdSelector =
        x => EF.Property<Guid>(x, EfRelationshipConfiguration.IdFieldName);

    public static readonly SortSpec<Relationship> CreatedAt = new("createdAt", x => x.CreatedAt);

    // Type is stored as a string column (HasConversion<string>()). Use .ToString() so
    // the cursor key round-trips as a string — sorting directly on the enum boxes it as
    // an integer, and ConvertCursorValue cannot cast Int64 back to RelationshipType.
    public static readonly SortSpec<Relationship> Type = new("type", x => x.Type.ToString());

    public static readonly IReadOnlyList<string> AllowedFieldNames = [CreatedAt.FieldName, Type.FieldName];

    public static Expression<Func<Relationship, bool>> IdEquals(Guid id) =>
        x => EF.Property<Guid>(x, EfRelationshipConfiguration.IdFieldName) == id;

    public static SortSpec<Relationship> Resolve(RelationshipSortField field) => field switch
    {
        RelationshipSortField.CreatedAt => CreatedAt,
        RelationshipSortField.Type => Type,
        _ => throw new InvalidSortFieldException(field.ToString(), AllowedFieldNames),
    };
}
