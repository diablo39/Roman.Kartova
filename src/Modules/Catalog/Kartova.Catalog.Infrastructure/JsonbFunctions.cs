using System.Text.Json;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Maps Postgres's builtin <c>jsonb_extract_path_text(jsonb, text)</c> scalar function into LINQ
/// (ADR-0115 slice 2a, Task 7) — the Npgsql EF Core provider does not expose an
/// <c>EF.Functions.JsonExtractPathText</c>-style helper for this, so this is registered
/// explicitly via <c>CatalogDbContext.OnModelCreating</c> (<c>HasDbFunction</c>). Used by
/// <see cref="VmSortSpecs"/>'s JSONB sort selectors so the EF-translated <c>ORDER BY</c> SQL is
/// byte-identical to the 6 partial expression indexes in the
/// <c>AddInfrastructureProviderAndSortIndexes</c> migration.
/// <para>
/// The body below also runs for real client-side: <see cref="Kartova.SharedKernel.Pagination.SortSpec{TEntity}.CompiledKeySelector"/>
/// compiles the same expression and invokes it in-memory against an already-materialized page
/// row to encode the cursor boundary value — so this mirrors Postgres's
/// <c>jsonb_extract_path_text</c> semantics (a JSON string returned unquoted; any other scalar
/// kind returned as its raw JSON text, matching how Postgres renders a JSON number/bool as text)
/// rather than throwing <see cref="NotSupportedException"/> the way a translation-only stub
/// normally would.
/// </para>
/// </summary>
internal static class JsonbFunctions
{
    public static string? JsonbExtractPathText(string attributesJson, string key)
    {
        using var doc = JsonDocument.Parse(attributesJson);
        if (!doc.RootElement.TryGetProperty(key, out var prop) || prop.ValueKind == JsonValueKind.Null)
            return null;
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.GetRawText();
    }
}
