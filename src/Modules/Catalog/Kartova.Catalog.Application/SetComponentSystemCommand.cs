using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Application;

/// <summary>Set (or clear, when <paramref name="SystemId"/> is null) the System a component
/// belongs to. At-most-one — see <see cref="SystemMembership"/>.</summary>
public sealed record SetComponentSystemCommand(EntityRef Component, Guid? SystemId);
