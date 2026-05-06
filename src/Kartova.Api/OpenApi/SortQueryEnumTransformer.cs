using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Pagination;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Kartova.Api.OpenApi;

/// <summary>
/// Surfaces per-resource sort-field enums and the global <see cref="SortOrder"/>
/// enum on <c>?sortBy</c> / <c>?sortOrder</c> query parameters in the OpenAPI
/// document — fulfilling ADR-0095 §Consequences ("OpenAPI generates per-resource
/// sort-field enums; the frontend gets compile-time-safe sort values").
///
/// Endpoints bind <c>?sortBy</c> / <c>?sortOrder</c> as <c>string?</c> rather than
/// the typed enum so that <see cref="InvalidSortFieldException"/> +
/// <c>PagingExceptionHandler</c> can return RFC 7807 400 with the
/// <c>allowedFields</c> list (typed-enum binding would short-circuit with a
/// generic framework error). This transformer adds the missing schema enum
/// values back to the OpenAPI document so the generated TypeScript client
/// derives a string-literal union for sort values.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class SortQueryEnumTransformer : IOpenApiOperationTransformer
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
            if (!EnumByOperationParameter.TryGetValue((operation.OperationId, parameter.Name), out var enumType))
            {
                continue;
            }
            if (parameter.Schema is not OpenApiSchema schema)
            {
                continue;
            }

            schema.Type = JsonSchemaType.String;
            schema.Enum = Enum.GetNames(enumType)
                .Select(name => (JsonNode)JsonValue.Create(JsonNamingPolicy.CamelCase.ConvertName(name))!)
                .ToList();
        }

        return Task.CompletedTask;
    }
}
