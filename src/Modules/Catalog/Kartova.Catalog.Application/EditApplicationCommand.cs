namespace Kartova.Catalog.Application;

/// <summary>
/// Edit metadata on an existing Application. ExpectedVersion drives optimistic
/// concurrency — handler sets it as EF OriginalValue so SaveChanges raises
/// DbUpdateConcurrencyException on stale ETag.
///
/// <c>Id</c> is fully qualified to <see cref="Kartova.Catalog.Domain.ApplicationId"/>
/// because <c>System.ApplicationId</c> exists in the BCL and the unqualified
/// name would be ambiguous in this Application-namespaced file.
/// </summary>
public sealed record EditApplicationCommand(
    Kartova.Catalog.Domain.ApplicationId Id,
    string DisplayName,
    string Description,
    uint ExpectedVersion);
