using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Pagination;
using Kartova.SharedKernel.Postgres.Pagination;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Kartova.Api.OpenApi;

/// <summary>
/// Aligns the OpenAPI schemas for cursor-list query parameters with the runtime
/// contract defined in ADR-0095:
/// <list type="bullet">
///   <item><description><c>?sortBy</c> — per-resource string enum (e.g. <see cref="ApplicationSortField"/>).</description></item>
///   <item><description><c>?sortOrder</c> — global string enum (<see cref="SortOrder"/>).</description></item>
///   <item><description><c>?limit</c> — bounded integer in <c>[1, 200]</c>.</description></item>
/// </list>
///
/// Endpoints bind these as <c>string?</c> rather than typed parameters so the
/// custom RFC 7807 envelopes (<c>invalid-sort-field</c>, <c>invalid-sort-order</c>,
/// <c>invalid-limit</c>) carry the allowlist / bounds fields. This transformer
/// keeps the published schema honest for the generated TypeScript client even
/// though the C# binding is loose.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class CursorListQueryParameterTransformer : IOpenApiOperationTransformer
{
    /// <summary>
    /// Per-(operationId, parameter-name) map of the enum that backs each sort
    /// query parameter. Add a row when a new list endpoint is introduced.
    /// </summary>
    private static readonly Dictionary<(string OperationId, string ParameterName), Type> EnumByOperationParameter = new()
    {
        [("ListApplications", "sortBy")] = typeof(ApplicationSortField),
        [("ListApplications", "sortOrder")] = typeof(SortOrder),
    };

    /// <summary>
    /// Operation IDs that expose a cursor-list <c>?limit</c> parameter. The set
    /// drives the bounded-integer schema rewrite; non-list operations with an
    /// incidental "limit" parameter are not affected.
    /// </summary>
    private static readonly HashSet<string> OperationsWithLimitParameter = new()
    {
        "ListApplications",
    };

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (operation.OperationId is null || operation.Parameters is null)
        {
            return Task.CompletedTask;
        }

        foreach (var parameter in operation.Parameters)
        {
            if (parameter.Name is null) continue;
            if (parameter.Schema is not OpenApiSchema schema) continue;

            if (EnumByOperationParameter.TryGetValue((operation.OperationId, parameter.Name), out var enumType))
            {
                schema.Type = JsonSchemaType.String;
                schema.Enum = Enum.GetNames(enumType)
                    .Select(name => (JsonNode)JsonValue.Create(JsonNamingPolicy.CamelCase.ConvertName(name))!)
                    .ToList();
                continue;
            }

            if (parameter.Name == "limit" && OperationsWithLimitParameter.Contains(operation.OperationId))
            {
                // Default schema (driven by `[FromQuery] string? limit` binding) is type:string —
                // overwrite to the runtime contract: bounded int32 in [MinLimit, MaxLimit].
                schema.Type = JsonSchemaType.Integer;
                schema.Format = "int32";
                schema.Minimum = QueryablePagingExtensions.MinLimit.ToString(System.Globalization.CultureInfo.InvariantCulture);
                schema.Maximum = QueryablePagingExtensions.MaxLimit.ToString(System.Globalization.CultureInfo.InvariantCulture);
                schema.Pattern = null;
                schema.Enum = null;
            }
        }

        return Task.CompletedTask;
    }
}
