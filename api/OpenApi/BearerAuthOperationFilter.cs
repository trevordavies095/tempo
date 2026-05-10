using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Tempo.Api.OpenApi;

/// <summary>
/// Adds OpenAPI <c>security</c> for endpoints that require authorization; leaves public routes unmarked.
/// </summary>
public sealed class BearerAuthOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor?.EndpointMetadata;
        if (metadata == null || metadata.Count == 0)
        {
            return;
        }

        if (metadata.OfType<IAllowAnonymous>().Any())
        {
            return;
        }

        if (!metadata.OfType<IAuthorizeData>().Any())
        {
            return;
        }

        // OpenAPI.NET 2.x: `OpenApiSecuritySchemeReference` must be tied to the host document or
        // `Target` stays null and serialization emits `{ }` for each requirement (issue #2801).
        // Swashbuckle exposes the in-flight document on the operation filter context.
        operation.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(
                    TempoOpenApi.BearerSecuritySchemeId,
                    context.Document,
                    externalResource: null)] = []
            }
        ];
    }
}
