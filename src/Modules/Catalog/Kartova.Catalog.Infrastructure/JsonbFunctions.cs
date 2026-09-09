using System.Text.Json;
using Microsoft.EntityFrameworkCore.Query;

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
/// <para>
/// <b>[NotParameterized] on <paramref name="key"/> below (gate-8 review finding, fix round 1):</b>
/// without it, EF Core is free to lift the "powerState"/"vcpu"/etc. literal out of the LINQ
/// expression into a bound SQL parameter for query-plan-cache reuse. A bound parameter breaks
/// the whole point of this class — Postgres expression-index matching requires the same Const
/// node (not a Param) in the parsed query tree as in the index definition, so a parameterized key
/// would silently stop matching the partial indexes in the
/// <c>AddInfrastructureProviderAndSortIndexes</c> migration, and cursor keyset paging would
/// seq-scan with every existing test still green (the tests only ever exercise ONE key value per
/// field, so parameterization vs. literal-inlining is externally invisible without inspecting the
/// actual emitted <see cref="System.Data.Common.DbCommand"/> — see
/// <c>InfrastructureVmSortTests.ListVms_sortBy_powerState_emits_literal_key_and_uses_partial_index</c>).
/// </para>
/// </summary>
internal static class JsonbFunctions
{
    public static string? JsonbExtractPathText(string attributesJson, [NotParameterized] string key)
    {
        using var doc = JsonDocument.Parse(attributesJson);
        if (!doc.RootElement.TryGetProperty(key, out var prop) || prop.ValueKind == JsonValueKind.Null)
            return null;
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.GetRawText();
    }
}
