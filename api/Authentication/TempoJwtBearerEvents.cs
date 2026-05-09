using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Tempo.Api.Authentication;

/// <summary>
/// JWT Bearer events: cookie-first token, JSON 401 body on challenge (Theme C).
/// </summary>
public static class TempoJwtBearerEvents
{
    public static JwtBearerEvents Create()
    {
        return new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                context.Token = context.Request.Cookies["authToken"];
                return Task.CompletedTask;
            },
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new { error = AuthErrorMessages.Unauthorized });
            }
        };
    }
}
