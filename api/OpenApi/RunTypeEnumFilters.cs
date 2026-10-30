using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using Tempo.Api.Models;

namespace Tempo.Api.OpenApi;

/// <summary>
/// Publishes <see cref="RunTypes.All"/> as the <c>runType</c> enum on the PATCH workout body and on
/// <c>runType</c> query filters, so the OpenAPI contract cannot drift from validation.
/// </summary>
public sealed class RunTypeEnumFilters : ISchemaFilter, IParameterFilter
{
    private const string RunTypeName = "runType";

    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type != typeof(UpdateWorkoutRequest) ||
            schema.Properties == null ||
            !schema.Properties.TryGetValue(RunTypeName, out var property) ||
            property is not OpenApiSchema runTypeSchema)
        {
            return;
        }

        runTypeSchema.Enum = BuildEnum();
    }

    public void Apply(IOpenApiParameter parameter, ParameterFilterContext context)
    {
        if (parameter.In != ParameterLocation.Query ||
            parameter.Name != RunTypeName ||
            parameter.Schema is not OpenApiSchema schema)
        {
            return;
        }

        schema.Enum = BuildEnum();
    }

    private static List<JsonNode> BuildEnum() =>
        RunTypes.All.Select(value => (JsonNode)JsonValue.Create(value)).ToList();
}
