namespace Kartova.Catalog.Application;

/// <summary>Upsert (create-or-replace) the stored spec document for an API. Content/media-type
/// are validated by <c>ApiSpec</c>; the caller/clock/tenant come from context, not the command.</summary>
public sealed record UpsertApiSpecCommand(Guid ApiId, string Content, string MediaType);
