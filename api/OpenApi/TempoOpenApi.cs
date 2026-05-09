using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using Tempo.Api.Services;

namespace Tempo.Api.OpenApi;

public static class TempoOpenApi
{
    public const string BearerSecuritySchemeId = "Bearer";

    public static void ConfigureSwaggerGen(SwaggerGenOptions options)
    {
        var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath))
        {
            options.IncludeXmlComments(xmlPath);
        }

        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "Tempo API",
            Version = ReadRepositoryVersionOrFallback(),
            Description =
                "HTTP API for Tempo, a self-hosted running tracker. " +
                "Protected routes accept `Authorization: Bearer` with either a JWT (interactive web session) " +
                $"or an admin-issued API key (prefix `{ApiKeyService.KeyMaterialPrefix}`). " +
                "Machine clients (CLI, automation) typically use API keys; JWTs are usually issued via cookie after login."
        });

        options.AddSecurityDefinition(BearerSecuritySchemeId, new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description =
                "JWT from login (often delivered as httpOnly cookie `authToken`; Swagger may use a pasted token) " +
                $"or API key string starting with `{ApiKeyService.KeyMaterialPrefix}`. " +
                "API key management endpoints require a full JWT session (not an API key)."
        });

        options.OperationFilter<BearerAuthOperationFilter>();
    }

    private static string ReadRepositoryVersionOrFallback()
    {
        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                var versionFile = Path.Combine(dir.FullName, "VERSION");
                if (File.Exists(versionFile))
                {
                    var text = File.ReadAllText(versionFile).Trim();
                    if (!string.IsNullOrEmpty(text))
                    {
                        return text;
                    }
                }
            }
        }
        catch
        {
            // ignored — fall through
        }

        return "0.0.0";
    }
}
