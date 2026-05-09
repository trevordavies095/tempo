using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
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

        operation.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = TempoOpenApi.BearerSecuritySchemeId
                    }
                }] = Array.Empty<string>()
            }
        ];
    }
}
